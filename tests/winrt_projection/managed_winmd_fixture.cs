using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

// The shape winmdexp gives a C# component: a bodiless WinRT declaration, the
// hidden <CLR> class with the code, and a synthesized interface whose
// signatures name the declaration and WinRT collection types.
class ManagedWinmdFixture
{
    static void Main(string[] args)
    {
        using (var assembly = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("ManagedFixture", new Version(1, 2, 3, 4)), "ManagedFixture.winmd", ModuleKind.Dll))
        {
            var module = assembly.MainModule;
            // Readers take the WinMD kind from this version string and the
            // <CLR> types; Cecil cannot write a created module with that kind.
            module.RuntimeVersion = "WindowsRuntime 1.3;CLR v4.0.30319";
            assembly.Name.IsWindowsRuntime = true;
            var windows = new AssemblyNameReference("Windows", new Version(255, 255, 255, 255)) { IsWindowsRuntime = true };
            module.AssemblyReferences.Add(windows);
            var vector = new TypeReference("Windows.Foundation.Collections", "IVector`1", module, windows);
            vector.GenericParameters.Add(new GenericParameter(vector));
            var winrtValues = new GenericInstanceType(vector);
            winrtValues.GenericArguments.Add(module.TypeSystem.Int32);
            var clrValues = module.ImportReference(typeof(IList<int>));

            var declaration = new TypeDefinition("Example", "Component",
                TypeAttributes.Public | TypeAttributes.WindowsRuntime | TypeAttributes.Sealed, module.TypeSystem.Object);
            module.Types.Add(declaration);
            var contract = new TypeDefinition("Example", "IComponentClass",
                TypeAttributes.Public | TypeAttributes.WindowsRuntime | TypeAttributes.Interface | TypeAttributes.Abstract);
            module.Types.Add(contract);
            const MethodAttributes slot = MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot;
            var count = new MethodDefinition("Count", slot | MethodAttributes.Abstract, module.TypeSystem.Int32);
            count.Parameters.Add(new ParameterDefinition("values", ParameterAttributes.None, winrtValues));
            contract.Methods.Add(count);
            var echo = new MethodDefinition("Echo", slot | MethodAttributes.Abstract, declaration);
            contract.Methods.Add(echo);

            var implementation = new TypeDefinition("Example", "<CLR>Component",
                TypeAttributes.NotPublic | TypeAttributes.SpecialName | TypeAttributes.Sealed, module.TypeSystem.Object);
            implementation.Interfaces.Add(new InterfaceImplementation(contract));
            module.Types.Add(implementation);
            var constructor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.HideBySig |
                MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, module.TypeSystem.Void);
            var il = constructor.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)));
            il.Emit(OpCodes.Ret);
            implementation.Methods.Add(constructor);
            var read = new MethodDefinition("Read", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Int32);
            read.Body.GetILProcessor().Emit(OpCodes.Ldc_I4, 42);
            read.Body.GetILProcessor().Emit(OpCodes.Ret);
            implementation.Methods.Add(read);
            var counted = new MethodDefinition("Count", slot | MethodAttributes.Final, module.TypeSystem.Int32);
            counted.Parameters.Add(new ParameterDefinition("values", ParameterAttributes.None, clrValues));
            il = counted.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, module.ImportReference(typeof(ICollection<int>).GetProperty("Count").GetGetMethod()));
            il.Emit(OpCodes.Ret);
            implementation.Methods.Add(counted);
            var echoed = new MethodDefinition("Echo", slot | MethodAttributes.Final, implementation);
            il = echoed.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ret);
            implementation.Methods.Add(echoed);
            assembly.Write(args[0]);
        }
    }
}

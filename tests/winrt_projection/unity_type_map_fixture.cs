using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Unity 5's Store UnityEngine.dll: BootstrapHelpers.FillTypeMaps loads raw
// WinMD type tokens, which the desktop CLR projects and Mono cannot resolve.
// "plain" writes the same library without the table (as Unity 4.x ships).
class UnityTypeMapFixture
{
    static void Main(string[] args)
    {
        using (var assembly = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("UnityEngine", new Version(0, 0, 0, 0)), "UnityEngine.dll", ModuleKind.Dll))
        {
            var module = assembly.MainModule;
            var helpers = new TypeDefinition("UnityEngineInternal", "BootstrapHelpers",
                TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, module.TypeSystem.Object);
            module.Types.Add(helpers);
            if (args.Length > 1 && args[1] == "plain")
            {
                assembly.Write(args[0]);
                return;
            }
            var windows = new AssemblyNameReference("Windows", new Version(255, 255, 255, 255)) { IsWindowsRuntime = true };
            module.AssemblyReferences.Add(windows);
            var vector = new TypeReference("Windows.Foundation.Collections", "IVector`1", module, windows);
            vector.GenericParameters.Add(new GenericParameter(vector));
            var vectorOfInt = new GenericInstanceType(vector);
            vectorOfInt.GenericArguments.Add(module.TypeSystem.Int32);
            var closable = new TypeReference("Windows.Foundation", "IClosable", module, windows);

            var map = module.ImportReference(typeof(Dictionary<Type, int>));
            var list = module.ImportReference(typeof(List<Type>));
            var fill = new MethodDefinition("FillTypeMaps", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
            fill.Parameters.Add(new ParameterDefinition("map", ParameterAttributes.None, new ByReferenceType(map)));
            fill.Parameters.Add(new ParameterDefinition("types", ParameterAttributes.None, new ByReferenceType(list)));
            helpers.Methods.Add(fill);
            // The table lives in a helper FillTypeMaps calls, as Unity splits it.
            var part = new MethodDefinition("FillTypeMapsPart", MethodAttributes.Private | MethodAttributes.Static, module.TypeSystem.Void);
            part.Parameters.Add(new ParameterDefinition("map", ParameterAttributes.None, map));
            part.Parameters.Add(new ParameterDefinition("types", ParameterAttributes.None, list));
            helpers.Methods.Add(part);
            var fromHandle = module.ImportReference(typeof(Type).GetMethod("GetTypeFromHandle"));
            var add = module.ImportReference(typeof(Dictionary<Type, int>).GetMethod("Add"));
            var append = module.ImportReference(typeof(List<Type>).GetMethod("Add"));
            var il = part.Body.GetILProcessor();
            int index = 0;
            foreach (var token in new TypeReference[] { vectorOfInt, closable, module.TypeSystem.String })
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldtoken, token);
                il.Emit(OpCodes.Call, fromHandle);
                il.Emit(OpCodes.Ldc_I4, index++);
                il.Emit(OpCodes.Callvirt, add);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldtoken, token);
                il.Emit(OpCodes.Call, fromHandle);
                il.Emit(OpCodes.Callvirt, append);
            }
            il.Emit(OpCodes.Ret);
            il = fill.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldind_Ref);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldind_Ref);
            il.Emit(OpCodes.Call, part);
            il.Emit(OpCodes.Ret);
            assembly.Write(args[0]);
        }
    }
}

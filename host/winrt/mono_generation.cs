// Build-time normalization of SharpDX's postprocessed generic copy IL.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static partial class WinrtProjectionGenerator
{
    // Older Unity WinRT players embed a reflection table containing raw
    // WinMD type tokens. The desktop CLR projects these tokens. Mono needs
    // their CLR equivalents written explicitly. Leave all executable method
    // references and signatures alone.
    private static int NormalizeUnityTypeMap(string input, string output)
    {
        using (var module = ModuleDefinition.ReadModule(input, new ReaderParameters { InMemory = true }))
        {
            if (module.Assembly.Name.Name != "UnityEngine" || module.Kind != ModuleKind.Dll ||
                module.Assembly.Name.HasPublicKey || (module.Attributes & ModuleAttributes.StrongNameSigned) != 0)
                throw new InvalidDataException("Expected an unsigned UnityEngine managed library");
            var helper = module.Types.SingleOrDefault(t => t.FullName == "UnityEngineInternal.BootstrapHelpers");
            var fill = helper == null ? null : helper.Methods.SingleOrDefault(m => m.Name == "FillTypeMaps" &&
                m.IsStatic && m.HasBody && m.ReturnType.FullName == "System.Void" &&
                m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(new[] {
                    "System.Collections.Generic.Dictionary`2<System.Type,System.Int32>&",
                    "System.Collections.Generic.List`1<System.Type>&"
                }));
            // Players without the table (Unity 4.x) or without WinMD tokens in it
            // need no override; say so without writing one.
            if (fill == null)
            {
                Console.WriteLine("UNCHANGED:no legacy Unity type table");
                return 0;
            }
            var visited = new HashSet<MethodDefinition>();
            var pending = new Stack<MethodDefinition>();
            pending.Push(fill);
            int changed = 0;
            while (pending.Count != 0)
            {
                var method = pending.Pop();
                if (!visited.Add(method)) continue;
                foreach (var instruction in method.Body.Instructions)
                {
                    var called = instruction.Operand as MethodDefinition;
                    if (instruction.OpCode == OpCodes.Call && called != null && called.HasBody && called.DeclaringType == helper)
                        pending.Push(called);
                    var token = instruction.Operand as TypeReference;
                    if (instruction.OpCode != OpCodes.Ldtoken || token == null) continue;
                    var projected = ProjectUnityToken(module, token);
                    if (projected != token)
                    {
                        instruction.Operand = projected;
                        ++changed;
                    }
                }
            }
            if (changed == 0)
            {
                Console.WriteLine("UNCHANGED:the Unity type table names no WinRT types");
                return 0;
            }
            module.Write(output);
            Console.WriteLine("Rebound " + changed + " Unity WinRT type tokens");
            Console.WriteLine("NORMALIZED:" + module.Assembly.Name.FullName);
            return 0;
        }
    }

    private static TypeReference ProjectUnityToken(ModuleDefinition module, TypeReference token)
    {
        return ProjectWinrtType(module, token, t => t);
    }

    // The CLR form of a signature type: WinRT names the desktop CLR projects
    // become their .NET types, and replace() substitutes module types.
    private static TypeReference ProjectWinrtType(ModuleDefinition module, TypeReference token, Func<TypeReference, TypeReference> replace)
    {
        if (token == null || token is GenericParameter) return token;
        var generic = token as GenericInstanceType;
        if (generic != null)
        {
            var definition = ProjectWinrtType(module, generic.ElementType, replace);
            var arguments = generic.GenericArguments.Select(t => ProjectWinrtType(module, t, replace)).ToArray();
            if (definition == generic.ElementType && arguments.SequenceEqual(generic.GenericArguments)) return token;
            var result = new GenericInstanceType(definition);
            foreach (var argument in arguments) result.GenericArguments.Add(argument);
            return result;
        }
        var array = token as ArrayType;
        if (array != null)
        {
            var element = ProjectWinrtType(module, array.ElementType, replace);
            if (element == array.ElementType) return token;
            var result = new ArrayType(element, array.Rank);
            for (int i = 0; i < array.Rank; ++i) result.Dimensions[i] = array.Dimensions[i];
            return result;
        }
        var byReference = token as ByReferenceType;
        if (byReference != null)
        {
            var element = ProjectWinrtType(module, byReference.ElementType, replace);
            return element == byReference.ElementType ? token : new ByReferenceType(element);
        }
        var pointer = token as PointerType;
        if (pointer != null)
        {
            var element = ProjectWinrtType(module, pointer.ElementType, replace);
            return element == pointer.ElementType ? token : new PointerType(element);
        }
        var pinned = token as PinnedType;
        if (pinned != null)
        {
            var element = ProjectWinrtType(module, pinned.ElementType, replace);
            return element == pinned.ElementType ? token : new PinnedType(element);
        }
        var required = token as RequiredModifierType;
        if (required != null)
        {
            var element = ProjectWinrtType(module, required.ElementType, replace);
            var modifier = ProjectWinrtType(module, required.ModifierType, replace);
            return element == required.ElementType && modifier == required.ModifierType ? token : new RequiredModifierType(modifier, element);
        }
        var optional = token as OptionalModifierType;
        if (optional != null)
        {
            var element = ProjectWinrtType(module, optional.ElementType, replace);
            var modifier = ProjectWinrtType(module, optional.ModifierType, replace);
            return element == optional.ElementType && modifier == optional.ModifierType ? token : new OptionalModifierType(modifier, element);
        }
        if (token is TypeSpecification)
            throw new NotSupportedException("Unsupported WinMD signature type: " + token.GetType().Name + " " + token.FullName);
        var replaced = replace(token);
        if (replaced != token) return replaced;
        var reference = token.Scope as AssemblyNameReference;
        if (reference == null || reference.Name != "Windows" || reference.Version != new Version(255, 255, 255, 255) ||
            reference.PublicKeyToken.Length != 0 || !string.IsNullOrEmpty(reference.Culture))
            return token;
        string name;
        if (token.FullName == "Windows.Foundation.IClosable") name = "System.IDisposable";
        else if (!clrNames.TryGetValue(token.FullName, out name)) return token;
        int arity = token.Name.IndexOf('`');
        if (arity >= 0) name += token.Name.Substring(arity);
        var clr = typeof(object).Assembly.GetType(name) ?? typeof(Uri).Assembly.GetType(name);
        if (clr == null) throw new NotSupportedException("Missing CLR alias for " + token.FullName);
        return module.ImportReference(clr);
    }

    // A C# Windows Runtime component (.winmd with CLR implementation) as the
    // assembly Mono can load. winmdexp declares each public runtime class
    // twice: a bodiless WinRT declaration, and the hidden <CLR> class with the
    // code. The desktop CLR swaps them at load; here the declaration is
    // removed, every reference moves to the implementation, which takes its
    // name, and WinRT signature names become their CLR projections. The
    // assembly identity and all method bodies are kept.
    private static int ConvertManagedWinmd(string input, string output)
    {
        using (var module = ModuleDefinition.ReadModule(input, new ReaderParameters { ApplyWindowsRuntimeProjections = false, InMemory = true }))
        {
            if (!module.Types.Any(t => t.Name.StartsWith("<CLR>", StringComparison.Ordinal)))
                throw new InvalidDataException("No CLR implementation in " + input);
            if (!module.Assembly.Name.IsWindowsRuntime || module.Kind != ModuleKind.Dll)
                throw new InvalidDataException("Expected Windows Runtime metadata: " + input);
            var declarations = new Dictionary<TypeDefinition, TypeDefinition>();
            foreach (var implementation in module.Types.Where(t => t.Name.StartsWith("<CLR>", StringComparison.Ordinal)).ToArray())
            {
                string name = implementation.Name.Substring(5);
                var declaration = module.Types.SingleOrDefault(t => t.Namespace == implementation.Namespace && t.Name == name);
                if (declaration == null || !declaration.IsWindowsRuntime || declaration.HasNestedTypes || implementation.IsPublic ||
                    declaration.Methods.Any(m => m.HasBody))
                    throw new InvalidDataException("Unexpected CLR implementation shape: " + implementation.FullName);
                declarations.Add(declaration, implementation);
            }
            Func<TypeReference, TypeReference> replace = t =>
            {
                var definition = t as TypeDefinition;
                TypeDefinition implementation;
                return definition != null && declarations.TryGetValue(definition, out implementation) ? implementation : t;
            };
            Func<TypeReference, TypeReference> project = t => ProjectWinrtType(module, t, replace);
            foreach (var declaration in declarations.Keys) module.Types.Remove(declaration);
            RewriteSignatures(module, project);
            // Nothing may still name a removed declaration.
            RewriteSignatures(module, t =>
            {
                ProjectWinrtType(module, t, x =>
                {
                    var definition = x as TypeDefinition;
                    if (definition != null && declarations.ContainsKey(definition))
                        throw new InvalidDataException("Unconverted reference to " + definition.FullName);
                    return x;
                });
                return t;
            });
            foreach (var pair in declarations)
            {
                var implementation = pair.Value;
                implementation.Name = pair.Key.Name;
                implementation.Attributes = (implementation.Attributes & ~(TypeAttributes.VisibilityMask | TypeAttributes.SpecialName)) |
                    (pair.Key.Attributes & TypeAttributes.VisibilityMask);
            }
            foreach (var type in module.GetTypes()) type.IsWindowsRuntime = false;
            foreach (var reference in module.AssemblyReferences) reference.IsWindowsRuntime = false;
            module.Assembly.Name.IsWindowsRuntime = false;
            module.MetadataKind = MetadataKind.Ecma335;
            module.RuntimeVersion = "v4.0.30319";
            module.Write(output);
            Console.WriteLine("Converted " + declarations.Count + " runtime classes");
            Console.WriteLine("CONVERTED:" + module.Assembly.Name.FullName);
            return 0;
        }
    }

    private static void RewriteSignatures(ModuleDefinition module, Func<TypeReference, TypeReference> map)
    {
        Action<MethodReference> member = method =>
        {
            if (method is MethodDefinition) return;
            var instance = method as GenericInstanceMethod;
            if (instance != null)
            {
                for (int i = 0; i < instance.GenericArguments.Count; ++i) instance.GenericArguments[i] = map(instance.GenericArguments[i]);
                method = instance.ElementMethod;
            }
            method.DeclaringType = map(method.DeclaringType);
            method.ReturnType = map(method.ReturnType);
            foreach (var parameter in method.Parameters) parameter.ParameterType = map(parameter.ParameterType);
        };
        foreach (var type in module.GetTypes())
        {
            type.BaseType = map(type.BaseType);
            foreach (var implemented in type.Interfaces) implemented.InterfaceType = map(implemented.InterfaceType);
            foreach (var parameter in type.GenericParameters)
                foreach (var constraint in parameter.Constraints) constraint.ConstraintType = map(constraint.ConstraintType);
            foreach (var field in type.Fields) field.FieldType = map(field.FieldType);
            foreach (var property in type.Properties) property.PropertyType = map(property.PropertyType);
            foreach (var declared in type.Events) declared.EventType = map(declared.EventType);
            foreach (var method in type.Methods)
            {
                method.ReturnType = map(method.ReturnType);
                foreach (var parameter in method.Parameters) parameter.ParameterType = map(parameter.ParameterType);
                foreach (var parameter in method.GenericParameters)
                    foreach (var constraint in parameter.Constraints) constraint.ConstraintType = map(constraint.ConstraintType);
                foreach (var overridden in method.Overrides) member(overridden);
                if (!method.HasBody) continue;
                foreach (var variable in method.Body.Variables) variable.VariableType = map(variable.VariableType);
                foreach (var handler in method.Body.ExceptionHandlers) handler.CatchType = map(handler.CatchType);
                foreach (var instruction in method.Body.Instructions)
                {
                    var typeOperand = instruction.Operand as TypeReference;
                    if (typeOperand != null) instruction.Operand = map(typeOperand);
                    var methodOperand = instruction.Operand as MethodReference;
                    if (methodOperand != null) member(methodOperand);
                    var fieldOperand = instruction.Operand as FieldReference;
                    if (fieldOperand != null && !(fieldOperand is FieldDefinition))
                    {
                        fieldOperand.DeclaringType = map(fieldOperand.DeclaringType);
                        fieldOperand.FieldType = map(fieldOperand.FieldType);
                    }
                }
            }
        }
    }

    private static int NormalizeSharpDx(string input, string output)
    {
        using (var module = ModuleDefinition.ReadModule(input, new ReaderParameters { InMemory = true }))
        {
            if (module.Assembly.Name.Name != "SharpDX") throw new InvalidDataException("Expected SharpDX assembly");
            var interopType = module.Types.SingleOrDefault(t => t.FullName == "SharpDX.Interop");
            if (interopType == null) throw new InvalidDataException("No SharpDX.Interop type");
            var methods = interopType.Methods.Where(m => m.Name == "Read" || m.Name == "Write").ToArray();
            if (methods.Length != 4) throw new InvalidDataException("Unsupported SharpDX generic copy overloads");
            foreach (var method in methods)
            {
                bool read = method.Name == "Read", array = method.Parameters.Count == 4;
                if (!method.IsStatic || method.GenericParameters.Count != 1 || !method.HasBody ||
                    method.ReturnType.FullName != "System.Void*" || (method.Parameters.Count != 2 && !array) ||
                    method.Parameters[0].ParameterType.FullName != "System.Void*" || method.Body.ExceptionHandlers.Count != 0 ||
                    method.Body.Variables.Count != 2 || method.Body.Variables[0].VariableType.FullName != "System.Int32" ||
                    !method.Body.Variables[1].VariableType.IsPinned)
                    throw new InvalidDataException("Unsupported SharpDX generic copy signature: " + method.FullName);
                var item = method.GenericParameters[0];
                var parameter = method.Parameters[1].ParameterType;
                if (array ? !(parameter is ArrayType) || !((ArrayType)parameter).IsVector || ((ArrayType)parameter).ElementType != item ||
                        method.Parameters[2].ParameterType.FullName != "System.Int32" || method.Parameters[3].ParameterType.FullName != "System.Int32"
                    : !(parameter is ByReferenceType) || ((ByReferenceType)parameter).ElementType != item)
                    throw new InvalidDataException("Unsupported SharpDX copy parameters: " + method.FullName);
                string expected = read
                    ? (array ? "ldarg.1 ldarg.2 ldelema stloc.1 ldloc.1 ldarg.0 sizeof conv.i4 ldarg.3 conv.i4 mul" : "ldarg.1 stloc.1 ldloc.1 ldarg.0 sizeof conv.i4")
                    : (array ? "ldarg.0 ldarg.1 ldarg.2 ldelema stloc.1 ldloc.1 sizeof conv.i4 ldarg.3 mul" : "ldarg.0 ldarg.1 stloc.1 ldloc.1 sizeof conv.i4");
                expected += " stloc.0 ldloc.0 unaligned. cpblk ldloc.0 conv.i ldarg.0 add ret";
                if (string.Join(" ", method.Body.Instructions.Select(i => i.OpCode.Name)) != expected ||
                    method.Body.Instructions.Where(i => i.OpCode == OpCodes.Sizeof || i.OpCode == OpCodes.Ldelema).Any(i => i.Operand != item) ||
                    method.Body.Instructions.Where(i => i.OpCode == OpCodes.Unaligned).Any(i => !Equals(i.Operand, (byte)1)))
                    throw new InvalidDataException("Unsupported SharpDX generic copy IL: " + method.FullName);
            }
            // Sizeof on a generic value can expand to a runtime helper under Mono.
            // Compute its byte count with an empty evaluation stack, then form the
            // copy pointers. This also avoids a pinned local across that helper.
            foreach (var method in methods)
            {
                bool read = method.Name == "Read", array = method.Parameters.Count == 4;
                var item = method.GenericParameters[0];
                method.Body.Instructions.Clear();
                var il = method.Body.GetILProcessor();
                il.Emit(OpCodes.Sizeof, item); il.Emit(OpCodes.Conv_I4);
                if (array) { il.Emit(OpCodes.Ldarg_3); il.Emit(OpCodes.Conv_I4); il.Emit(OpCodes.Mul); }
                il.Emit(OpCodes.Stloc_0);
                if (!read) il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                if (array) { il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldelema, item); }
                if (read) il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldloc_0); il.Emit(OpCodes.Unaligned, (byte)1); il.Emit(OpCodes.Cpblk);
                il.Emit(OpCodes.Ldloc_0); il.Emit(OpCodes.Conv_I); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Add); il.Emit(OpCodes.Ret);
            }
            module.Write(output);
            Console.WriteLine("NORMALIZED:" + module.Assembly.Name.FullName);
            return 0;
        }
    }
}

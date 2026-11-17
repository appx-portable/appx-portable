// Build-time normalization of SharpDX's postprocessed generic copy IL.
using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static partial class WinrtProjectionGenerator
{
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

using System;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class SharpDxWriteTest
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex { public float X, Y, Z; public uint Color; }
    private static int Main(string[] args)
    {
        IntPtr memory = Marshal.AllocHGlobal(64);
        try
        {
            var vertices = new[] { new Vertex { X = 1.25f, Y = -2.5f, Z = 3, Color = 0xff123456 }, new Vertex { X = 7, Y = 8, Z = 9, Color = 0xabcdef01 } };
            if (args.Length > 1) ComponentProjections.Install(args[1]);
            var assembly = Assembly.LoadFrom(args[0]);
            var utilities = assembly.GetType("SharpDX.Utilities", true);
            MethodInfo write = null;
            foreach(var method in utilities.GetMethods(BindingFlags.Static|BindingFlags.Public))
                if(method.Name == "Write" && method.IsGenericMethodDefinition && method.GetParameters().Length == 4 && method.GetParameters()[1].ParameterType.IsArray) write = method;
            if(write == null) throw new Exception("SharpDX array writer not found");
            object result = write.MakeGenericMethod(typeof(Vertex)).Invoke(null, new object[] { memory, vertices, 0, vertices.Length });
            if((IntPtr)result != IntPtr.Add(memory, 32) || Marshal.ReadInt32(memory, 12) != unchecked((int)0xff123456) || Marshal.ReadInt32(memory, 28) != unchecked((int)0xabcdef01)) throw new Exception("vertex bytes or advanced pointer differ");
            var readVertices = new Vertex[3];
            MethodInfo read = null, writeOne = null, readOne = null;
            foreach (var method in utilities.GetMethods(BindingFlags.Static | BindingFlags.Public))
            {
                if (!method.IsGenericMethodDefinition) continue;
                var parameters = method.GetParameters();
                if (method.Name == "Read" && parameters.Length == 4 && parameters[1].ParameterType.IsArray) read = method;
                if (parameters.Length == 2 && parameters[1].ParameterType.IsByRef)
                {
                    if (method.Name == "Write") writeOne = method;
                    if (method.Name == "Read") readOne = method;
                }
            }
            if (read == null || writeOne == null || readOne == null) throw new Exception("SharpDX generic copy overload missing");
            result = read.MakeGenericMethod(typeof(Vertex)).Invoke(null, new object[] { memory, readVertices, 1, 2 });
            if ((IntPtr)result != IntPtr.Add(memory, 32) || readVertices[0].Color != 0 || readVertices[1].Color != vertices[0].Color || readVertices[2].X != vertices[1].X) throw new Exception("array read offset or contents differ");
            result = writeOne.MakeGenericMethod(typeof(Vertex)).Invoke(null, new object[] { memory, vertices[1] });
            if (result != null || Marshal.ReadInt32(memory, 12) != unchecked((int)vertices[1].Color)) throw new Exception("single write differs");
            object[] single = { memory, new Vertex() };
            result = readOne.MakeGenericMethod(typeof(Vertex)).Invoke(null, single);
            if (result != null || ((Vertex)single[1]).Color != vertices[1].Color) throw new Exception("single read differs");
            Console.WriteLine("PASS: SharpDX generic array/ref copies preserve bytes, offsets and pointer advancement"); return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Marshal.FreeHGlobal(memory); }
    }
}

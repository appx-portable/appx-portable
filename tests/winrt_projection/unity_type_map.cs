using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

// The normalized Unity type table runs on Mono and names the CLR projections.
class UnityTypeMapTest
{
    static int Main(string[] args)
    {
        var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        var fill = assembly.GetType("UnityEngineInternal.BootstrapHelpers", true).GetMethod("FillTypeMaps");
        var call = new object[] { new Dictionary<Type, int>(), new List<Type>() };
        fill.Invoke(null, call);
        var map = (Dictionary<Type, int>)call[0];
        var types = (List<Type>)call[1];
        var expected = new[] { typeof(IList<int>), typeof(IDisposable), typeof(string) };
        if (types.Count != expected.Length) throw new Exception("Unity type table lists " + types.Count + " types");
        for (int i = 0; i < expected.Length; ++i)
            if (types[i] != expected[i] || map[expected[i]] != i)
                throw new Exception("Unity type table entry " + i + " is " + types[i]);
        Console.WriteLine("PASS: Unity type table names CLR projections");
        return 0;
    }
}

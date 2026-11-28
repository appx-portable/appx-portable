using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

// The converted component keeps its identity and code, and its CLR class
// still implements the synthesized interface after the WinRT names change.
class ManagedWinmdTest
{
    static int Main(string[] args)
    {
        var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        if (assembly.GetName().Name != "ManagedFixture" || assembly.GetName().Version != new Version(1, 2, 3, 4))
            throw new Exception("Managed WinMD identity changed");
        var component = assembly.GetType("Example.Component", true);
        if (!component.IsPublic || (int)component.GetMethod("Read").Invoke(null, null) != 42)
            throw new Exception("Managed WinMD implementation was not preserved");
        foreach (var type in assembly.GetTypes())
            if (type.Name.StartsWith("<CLR>", StringComparison.Ordinal) || (type.Attributes & TypeAttributes.WindowsRuntime) != 0)
                throw new Exception("Managed WinMD still declares " + type.FullName);
        var contract = assembly.GetType("Example.IComponentClass", true);
        object instance = Activator.CreateInstance(component);
        if (!contract.IsInstanceOfType(instance) ||
            (int)contract.GetMethod("Count").Invoke(instance, new object[] { new List<int> { 1, 2, 3 } }) != 3 ||
            contract.GetMethod("Echo").ReturnType != component || contract.GetMethod("Echo").Invoke(instance, null) != instance)
            throw new Exception("Managed WinMD interface does not bind to its CLR implementation");
        Console.WriteLine("PASS: managed WinMD retains assembly identity and executable IL");
        return 0;
    }
}

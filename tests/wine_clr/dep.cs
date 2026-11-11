// Accessibility's missing NX_COMPAT bit used to disable DEP process-wide in
// Wine, making imported Vulkan staging pages executable and slow under Rosetta.
using System;
using System.Reflection;
using System.Runtime.InteropServices;

class DepProbe
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr LoadLibraryW(string path);
    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetProcessDEPPolicy(IntPtr process, out uint flags, out bool permanent);
    [DllImport("kernel32.dll")]
    static extern bool FreeLibrary(IntPtr module);

    static bool Enabled()
    {
        uint flags;
        bool permanent;
        if (!GetProcessDEPPolicy(GetCurrentProcess(), out flags, out permanent))
            throw new InvalidOperationException("GetProcessDEPPolicy error " + Marshal.GetLastWin32Error());
        return (flags & 1) != 0;
    }

    static int Main()
    {
        if (IntPtr.Size != 4 || !Enabled())
        {
            Console.WriteLine("DEP probe requires an x86 process with DEP initially enabled");
            return 1;
        }
        var assembly = Assembly.Load("Accessibility, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a");
        // Also take the native loader path that checks the PE header in Wine.
        IntPtr module = LoadLibraryW(assembly.Location);
        if (module == IntPtr.Zero)
            throw new InvalidOperationException("LoadLibraryW error " + Marshal.GetLastWin32Error());
        try
        {
            if (!Enabled())
            {
                Console.WriteLine("Accessibility.dll disabled process DEP");
                return 1;
            }
            Console.WriteLine("x86 Accessibility DEP PASS");
            return 0;
        }
        finally { FreeLibrary(module); }
    }
}

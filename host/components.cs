// The bridge to C# Windows Runtime components, built into appx-components.dll
// for native games and into appx-host.exe for managed ones.
using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;

// Called by the shim in native games, through ICLRRuntimeHost::
// ExecuteInDefaultAppDomain, for a class the package serves from Windows'
// CLRHost.dll (a C# Windows Runtime component), which needs package identity.
// The desktop CLR resolves the component's .winmd beside the game executable
// and makes the same managed activation factory CLRHost would.
public static class ManagedComponents
{
    private const int ClassNotRegistered = unchecked((int)0x80040154);
    private static int observing;

    // Component exceptions surface in native code only as HRESULTs; log them
    // to managed.log, as the managed launcher does for C# games.
    private static void Observe()
    {
        if (Interlocked.Exchange(ref observing, 1) != 0)
        {
            return;
        }
        string log = Path.Combine(Path.GetDirectoryName(typeof(ManagedComponents).Assembly.Location), "managed.log");
        ManagedLog.AppendLog(log, "[components] desktop CLR started for C# components\n");
        AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
            ManagedLog.AppendLog(log, string.Format("[first-chance] {0} 0x{1:X8}: {2}\n{3}\n",
                e.Exception.GetType().FullName, e.Exception.HResult, e.Exception.Message,
                new System.Diagnostics.StackTrace(1, false)));
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            ManagedLog.AppendLog(log, "[unhandled] " + e.ExceptionObject + "\n");
    }

    // request is "<hex address>|<class id>"; the AddRef'd IActivationFactory is
    // written to the address. Returns an HRESULT.
    public static int GetFactory(string request)
    {
        try
        {
            Observe();
            int split = request.IndexOf('|');
            IntPtr slot = new IntPtr(long.Parse(request.Substring(0, split), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            Type type = Type.GetType(request.Substring(split + 1) + ", Windows, ContentType=WindowsRuntime", false);
            if (type == null)
            {
                return ClassNotRegistered;
            }
            IActivationFactory factory = WindowsRuntimeMarshal.GetActivationFactory(type);
            Marshal.WriteIntPtr(slot, Marshal.GetComInterfaceForObject(factory, typeof(IActivationFactory)));
            return 0;
        }
        catch (TypeLoadException)
        {
            return ClassNotRegistered;
        }
        catch (Exception ex)
        {
            return ex.HResult < 0 ? ex.HResult : unchecked((int)0x80004005);
        }
    }
}

internal static class ManagedLog
{
    private static readonly object logLock = new object();
    [ThreadStatic]
    private static bool writingLog;

    internal static void AppendLog(string path, string text)
    {
        // First-chance events can arrive concurrently. A sharing violation in
        // the exception observer itself can terminate the CLR. Logging must
        // never throw or recursively log its own failures.
        if (writingLog)
        {
            return;
        }
        writingLog = true;
        try
        {
            lock (logLock) { File.AppendAllText(path, text); }
        }
        catch (Exception) { }
        finally { writingLog = false; }
    }

}

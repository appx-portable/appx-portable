using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Threading;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;

// Exercise the real native host with managed view objects, including the view
// returned through CreateView's owned COM output. No game or XAML tree is needed.
internal static class CoreApplicationTest
{
    private static readonly List<string> calls = new List<string>();
    private static readonly InvalidOperationException marker = new InvalidOperationException("framework-view marker");
    private static string mode;
    private static int caller, ui;
    private static WeakReference view;
    private static CoreWindow window;
    private static CoreApplicationView applicationView;
    private static System.Runtime.InteropServices.WindowsRuntime.EventRegistrationToken activationToken;
    private static bool activated;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private static void Step(string phase)
    {
        calls.Add(phase);
        if (ui == 0) ui = Thread.CurrentThread.ManagedThreadId;
        Check(ui != caller && Thread.CurrentThread.ManagedThreadId == ui, "lifecycle must use one separate view thread");
        Check(Thread.CurrentThread.GetApartmentState() == ApartmentState.STA, "view apartment must be STA");
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        if (mode == phase) throw marker;
    }
    private sealed class Source : IFrameworkViewSource
    {
        public IFrameworkView CreateView()
        {
            Step("create");
            if (mode == "null-view") return null;
            var created = new View();
            view = new WeakReference(created);
            return created;
        }
    }
    private sealed class View : IFrameworkView
    {
        public void Initialize(CoreApplicationView applicationView)
        {
            Step("initialize");
            Check(applicationView != null, "application view must be supplied");
            var property = applicationView.GetType().GetProperty("CoreWindow");
            Check(property != null, "application view must expose its native window");
            window = (CoreWindow)property.GetValue(applicationView, null);
            CoreApplicationTest.applicationView = applicationView;
            activationToken = applicationView.add_Activated((sender, args) =>
            {
                Check(ReferenceEquals(sender, applicationView) && args.Kind == Windows.ApplicationModel.Activation.ActivationKind.Launch, "activation sender and kind");
                Check(args.SplashScreen != null, "launch must supply the native splash screen");
                var image = args.SplashScreen.GetType().GetProperty("ImageLocation");
                Check(image != null, "splash image bounds projection");
                var location = (Windows.Foundation.Rect)image.GetValue(args.SplashScreen, null);
                Check(location.Width > 0 && location.Height > 0, "native splash image bounds");
                Step("activated");
                activated = true;
            });
            Check(ReferenceEquals(applicationView, CoreApplication.GetCurrentView()), "current view identity");
        }
        public void SetWindow(CoreWindow value)
        {
            Step("set-window");
            Check(ReferenceEquals(value, window) && ReferenceEquals(value, CoreWindow.GetForCurrentThread()), "window identity");
            Check(value.Dispatcher.HasThreadAccess, "dispatcher must belong to view thread");
        }
        public void Load(string entryPoint)
        {
            Step("load");
            Check(entryPoint == "Fixture.Entry", "manifest entry point must reach Load");
        }
        public void Run()
        {
            Step("run");
            Check(activated, "activation must precede Run");
            if (mode == "window-apis") WindowApis();
            Check(view.IsAlive, "native view reference must keep the managed view alive through GC");
        }
        public void Uninitialize() { Step("uninitialize"); }
    }
    [DllImport("appxportable.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr ShimGetHostWindow();
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr key, IntPtr status);
    private static void WindowApis()
    {
        var bounds = window.GetType().GetProperty("Bounds");
        Check(bounds != null && ((Windows.Foundation.Rect)bounds.GetValue(window, null)).Width > 0, "native window bounds");
        int activationEvents = 0;
        var activation = window.add_Activated((sender, args) =>
        {
            if (args.WindowActivationState == CoreWindowActivationState.CodeActivated) ++activationEvents;
        });
        var activate = window.GetType().GetMethod("Activate");
        Check(activate != null, "window activation projection");
        activate.Invoke(window, null);
        var cursor = typeof(CoreCursor).GetConstructor(new[] { typeof(CoreCursorType), typeof(uint) });
        Check(cursor != null, "cursor construction projection");
        var arrow = cursor.Invoke(new object[] { CoreCursorType.Arrow, (uint)0 });
        var pointer = window.GetType().GetProperty("PointerCursor");
        Check(pointer != null, "pointer cursor projection");
        pointer.SetValue(window, arrow, null);
        Check(ReferenceEquals(pointer.GetValue(window, null), arrow), "cursor object identity");
        pointer.SetValue(window, null, null);
        Check(pointer.GetValue(window, null) == null, "hidden cursor round trip");
        var dpi = typeof(Windows.Graphics.Display.DisplayProperties).GetProperty("LogicalDpi");
        Check(dpi != null && (float)dpi.GetValue(null, null) > 0, "native logical DPI");
        int keyboard = 0, released = 0;
        var keyToken = window.add_KeyDown((sender, args) =>
        {
            var property = args.GetType().GetProperty("KeyStatus");
            Check(property != null, "physical key status projection");
            object status = property.GetValue(args, null);
            var type = status.GetType();
            Check(Marshal.SizeOf(type) == 12, "physical key status ABI size");
            Check((uint)type.GetField("RepeatCount").GetValue(status) == 2, "key repeat count");
            Check((uint)type.GetField("ScanCode").GetValue(status) == 30, "key scan code");
            Check((bool)type.GetField("IsExtendedKey").GetValue(status), "one-byte boolean ABI");
            keyboard++;
        });
        var upToken = window.add_KeyUp((sender, args) => ++released);
        Check(PostMessage(ShimGetHostWindow(), 0x101, new IntPtr(65), new IntPtr(unchecked((int)0xc01e0001))), "queue key-up event");
        Check(PostMessage(ShimGetHostWindow(), 0x100, new IntPtr(65), new IntPtr(2 | (30 << 16) | (1 << 24))), "queue key event");
        Check(PostMessage(ShimGetHostWindow(), 6, new IntPtr(1), IntPtr.Zero), "queue activation event");
        var process = window.Dispatcher.GetType().GetMethod("ProcessEvents");
        Check(process != null, "dispatcher message pumping projection");
        process.Invoke(window.Dispatcher, new object[] { CoreProcessEventsOption.ProcessAllIfPresent });
        window.remove_KeyDown(keyToken);
        window.remove_KeyUp(upToken);
        window.remove_Activated(activation);
        Check(activationEvents > 0, "core window activation event delivery");
        Check(keyboard == 1 && released == 1, "dispatcher must deliver key-down and key-up with native physical-key data");
        Check(Windows.Devices.Sensors.Accelerometer.GetDefault() == null, "desktop without an accelerometer");
        var orientation = typeof(Windows.Graphics.Display.DisplayProperties).GetProperty("CurrentOrientation");
        Check(orientation != null && Convert.ToInt32(orientation.GetValue(null, null)) != 0, "native display orientation");
        var identity = Windows.ApplicationModel.Package.Current.Id;
        var family = identity.GetType().GetProperty("FamilyName");
        Check(family != null && ((string)family.GetValue(identity, null)).StartsWith(identity.Name + "_"), "native package family name");
        var updater = Windows.UI.Notifications.TileUpdateManager.CreateTileUpdaterForApplication();
        var periodic = updater.GetType().GetMethod("StartPeriodicUpdate", new[] { typeof(Uri), typeof(Windows.UI.Notifications.PeriodicUpdateRecurrence) });
        Check(periodic != null, "periodic tile notification projection");
        periodic.Invoke(updater, new object[] { new Uri("https://example.invalid/tile.xml"), Windows.UI.Notifications.PeriodicUpdateRecurrence.HalfHour });
        Check(new Windows.Devices.Input.KeyboardCapabilities().KeyboardPresent == 1, "keyboard capabilities");
        Check(new Windows.Devices.Input.TouchCapabilities().TouchPresent >= 0, "touch capabilities");
        Check(new Windows.Devices.Input.MouseCapabilities().MousePresent >= 0, "mouse capabilities");
    }
    private sealed class Combined : IFrameworkViewSource, IFrameworkView
    {
        public IFrameworkView CreateView() { if (mode == "out-failure") throw marker; return this; }
        public void Initialize(CoreApplicationView value) { }
        public void SetWindow(CoreWindow value) { }
        public void Load(string value) { }
        public void Run() { }
        public void Uninitialize() { }
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateViewCall(IntPtr self, IntPtr result);
    private static void AbiContract()
    {
        var assembly = typeof(CoreApplication).Assembly;
        var errors = assembly.GetType("AppxPortable.WinRT.Errors");
        var callbacks = assembly.GetType("AppxPortable.WinRT.Projected.ComponentInterfaces");
        var native = assembly.GetType("AppxPortable.WinRT.NativeObject");
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var target = new Combined();
        object contracts = callbacks.GetMethod("Of", flags).Invoke(null, new object[] { target });
        var sourceIid = new Guid("cd770614-65c4-426c-9494-34fc43554862");
        using ((IDisposable)errors.GetMethod("Begin", flags).Invoke(null, null))
        using (var source = (SafeHandle)native.GetMethod("CallbackArgument", flags).Invoke(null, new object[] { target, sourceIid, contracts }))
        {
            IntPtr pointer = source.DangerousGetHandle();
            IntPtr method = Marshal.ReadIntPtr(Marshal.ReadIntPtr(pointer), 6 * IntPtr.Size);
            Delegate callback = Marshal.GetDelegateForFunctionPointer(method, typeof(CreateViewCall));
            var invoke = callback as CreateViewCall ?? (CreateViewCall)Delegate.CreateDelegate(typeof(CreateViewCall), callback.Target, callback.Method);
            IntPtr result = Marshal.AllocCoTaskMem(IntPtr.Size);
            IntPtr returned = IntPtr.Zero, sourceIdentity = IntPtr.Zero, viewIdentity = IntPtr.Zero;
            try
            {
                Marshal.WriteIntPtr(result, new IntPtr(123));
                int hr = invoke(pointer, result);
                returned = Marshal.ReadIntPtr(result);
                if (mode == "out-failure")
                {
                    Check(hr < 0 && returned == IntPtr.Zero, "failed interface callback must clear its output pointer");
                    return;
                }
                Check(hr == 0 && returned != IntPtr.Zero, "combined object must return a view");
                var iid = new Guid("00000000-0000-0000-c000-000000000046");
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(pointer, ref iid, out sourceIdentity));
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(returned, ref iid, out viewIdentity));
                Check(sourceIdentity == viewIdentity, "source and returned view of one managed object must share controlling IUnknown");
            }
            finally
            {
                if (returned != IntPtr.Zero && returned != new IntPtr(123)) Marshal.Release(returned);
                if (sourceIdentity != IntPtr.Zero) Marshal.Release(sourceIdentity);
                if (viewIdentity != IntPtr.Zero) Marshal.Release(viewIdentity);
                Marshal.FreeCoTaskMem(result);
            }
        }
    }
    [MTAThread]
    private static int Main(string[] args)
    {
        mode = args.Length == 0 ? "success" : args[0];
        caller = Thread.CurrentThread.ManagedThreadId;
        try
        {
            if (mode == "combined" || mode == "out-failure")
            {
                AbiContract();
                Console.WriteLine("PASS: lifecycle ABI " + mode); return 0;
            }
            if (mode == "reuse")
            {
                var source = new Source();
                mode = "create";
                for (int attempt = 0; attempt != 2; ++attempt)
                {
                    ui = 0;
                    try { CoreApplication.Run(source); throw new Exception("callback exception swallowed on reused source"); }
                    catch (InvalidOperationException error) { Check(ReferenceEquals(error, marker), "reused callback must preserve its invocation exception"); }
                }
                Console.WriteLine("PASS: reused source keeps exceptions bound to each invocation"); return 0;
            }
            if (mode == "null-source" || mode == "null-view")
            {
                try { CoreApplication.Run(mode == "null-source" ? null : new Source()); }
                catch (Exception error)
                {
                    int expected = mode == "null-source" ? unchecked((int)0x80070057) : unchecked((int)0x8000ffff);
                    Check(error.HResult == expected, "null source/view HRESULT: " + error);
                    Console.WriteLine("PASS: " + mode); return 0;
                }
                throw new Exception("null source/view accepted");
            }
            try { CoreApplication.Run(new Source()); }
            catch (InvalidOperationException error)
            {
                Check(mode != "success" && ReferenceEquals(error, marker), "original lifecycle exception must return to Run caller");
                Check(calls.Contains(mode), "expected failing callback was not reached");
                Check(!calls.Contains("run") || mode == "run" || mode == "uninitialize", "lifecycle continued past failure");
                Console.WriteLine("PASS: callback exception " + mode); return 0;
            }
            Check(mode == "success" || mode == "window-apis", "callback exception swallowed: " + mode);
            Check(string.Join(",", calls) == "create,initialize,set-window,load,activated,run,uninitialize", "callback order: " + string.Join(",", calls));
            window = null;
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Check(!view.IsAlive, "native lifecycle must release the returned managed view");
            Console.WriteLine("PASS: managed CoreApplication lifecycle, STA, identity, GC ownership and return"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            if (applicationView != null) applicationView.remove_Activated(activationToken);
            applicationView = null;
        }
    }
}

using System;
using System.Runtime.InteropServices;
using AppxPortable.WinRT.Interop;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

class KeyboardControlApp : Application
{
    [DllImport("appxportable.dll", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr ShimGetHostWindow();

    [DllImport("component_projection_fixture.dll", CallingConvention = CallingConvention.Cdecl)]
    static extern int ProbeKeyboardControl(SafeHandle control, IntPtr window, int expectedEvents);

    [DllImport("component_projection_fixture.dll", CallingConvention = CallingConvention.Cdecl)]
    static extern int ProbeFocusEvents(IntPtr identity);

    protected override void OnLaunched(Windows.ApplicationModel.Activation.LaunchActivatedEventArgs args)
    {
        var page = new Page();
        Window.Current.Content = page;
        var native = (INativeObject)page;
        var controlId = new Guid("a8912263-2951-4f58-a9c5-5a134eaa7f07");
        if (!ReferenceEquals(ProjectionInterop.Wrap<Control>(native.AcquireInterface(controlId)), page))
            throw new Exception("Native control returned a different page");
        using (var lease = native.AcquireInterface(controlId))
        {
            Marshal.ThrowExceptionForHR(ProbeKeyboardControl(lease, ShimGetHostWindow(), 2));
            Window.Current.Content = new Page();
            Marshal.ThrowExceptionForHR(ProbeKeyboardControl(lease, ShimGetHostWindow(), 0));
        }
        // Unity subscribes to focus events on its native swap chain panel.
        var panel = (INativeObject)new SwapChainPanel();
        using (var identity = panel.AcquireInterface(new Guid("00000000-0000-0000-c000-000000000046")))
            Marshal.ThrowExceptionForHR(ProbeFocusEvents(identity.DangerousGetHandle()));
        Console.WriteLine("PASS: native panel focus events retain identity and remove handlers");
        Console.WriteLine("PASS: native keyboard events follow the managed XAML page");
        Exit();
    }

    [STAThread]
    static void Main() { Application.Start(p => new KeyboardControlApp()); }
}

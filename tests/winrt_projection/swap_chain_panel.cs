using System;
using System.IO;
using System.Runtime.InteropServices;
using AppxPortable.WinRT.Interop;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

class SwapChainPanelApp : Application
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int SetSwapChain(IntPtr self, IntPtr chain);

    protected override void OnLaunched(Windows.ApplicationModel.Activation.LaunchActivatedEventArgs args)
    {
        var page = new Page();
        File.WriteAllText("panel.xaml", "<Page xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"><SwapChainPanel x:Name=\"unityPanel\"/></Page>");
        Application.LoadComponent(page, new Uri("ms-appx:///panel.xaml"));
        var panel = page.FindName("unityPanel") as SwapChainPanel;
        if (panel == null || !ReferenceEquals(page.Content, panel)) throw new Exception("XAML did not bind the swap chain panel");
        var native = (INativeObject)panel;
        using (var lease = native.AcquireInterface(new Guid("c589644f-eba8-427a-b75a-9f1f93a11ae9")))
            if (!ReferenceEquals(ProjectionInterop.Wrap<SwapChainPanel>(lease), panel))
                throw new Exception("Swap chain panel did not retain its managed identity");
        using (var lease = native.AcquireInterface(new Guid("f92f19d2-3ade-45a6-a20c-f6f1ea90554b")))
        {
            var pointer = lease.DangerousGetHandle();
            var slot = Marshal.ReadIntPtr(Marshal.ReadIntPtr(pointer), 3 * IntPtr.Size);
            var set = (SetSwapChain)Marshal.GetDelegateForFunctionPointer(slot, typeof(SetSwapChain));
            if (set(pointer, IntPtr.Zero) < 0) throw new Exception("Native panel rejected clearing its swap chain");
        }
        Console.WriteLine("PASS: SwapChainPanel markup binds the native panel");
        Exit();
    }

    [STAThread]
    static void Main() { Application.Start(p => new SwapChainPanelApp()); }
}

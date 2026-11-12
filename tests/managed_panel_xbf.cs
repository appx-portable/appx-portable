using System;
using System.IO;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Markup;

// Unity's Windows 8.1 template: compiled XAML declares a built-in
// SwapChainBackgroundPanel as the page content, and the page becomes the
// window content. XAML refuses that panel outside a CoreWindow's root visual.
sealed class CompiledPanelPage : Page, IComponentConnector {
    public SwapChainBackgroundPanel DXSwapChainBackgroundPanel;
    public void Connect(int id, object target) {
        if (id == 2) DXSwapChainBackgroundPanel = (SwapChainBackgroundPanel)target;
        else if (id != 1) throw new Exception("Unexpected compiled connection ID: " + id);
    }
    public CompiledPanelPage() {
        Application.LoadComponent(this, new Uri("ms-appx:///PanelFixture.xaml"));
    }
}
sealed class CompiledPanelApp : Application, IXamlMetadataProvider {
    public IXamlType GetXamlType(Type type) { return null; }
    public IXamlType GetXamlType(string name) { return null; }
    public XmlnsDefinition[] GetXmlnsDefinitions() { return new XmlnsDefinition[0]; }
    protected override void OnLaunched(LaunchActivatedEventArgs args) {
        var page = new CompiledPanelPage();
        var panel = page.DXSwapChainBackgroundPanel;
        if (panel == null || panel != page.FindName("DXSwapChainBackgroundPanel") || page.Content != panel)
            throw new Exception("Compiled SwapChainBackgroundPanel connection or namescope failed");
        // Unity hands its swap chain to the panel's native interface.
        var native = new Guid("43bebd4e-add5-4035-8f85-5608d08e9dc9");
        IntPtr unknown = Marshal.GetIUnknownForObject(panel), nativePanel;
        try {
            if (Marshal.QueryInterface(unknown, ref native, out nativePanel) != 0)
                throw new Exception("ISwapChainBackgroundPanelNative missing");
            Marshal.Release(nativePanel);
        } finally { Marshal.Release(unknown); }
        Window.Current.Content = page;
        Window.Current.Activate();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += (s, e) => {
            timer.Stop();
            File.WriteAllText("startup-passed.txt", "compiled SwapChainBackgroundPanel page as island content");
            Window.Current.Close();
        };
        timer.Start();
    }
}
static class CompiledPanelTest {
    [STAThread] static void Main() { Application.Start(p => new CompiledPanelApp()); }
}

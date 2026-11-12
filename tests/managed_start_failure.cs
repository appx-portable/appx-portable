using System;
using System.IO;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Markup;

// A launch that fails ends Application.Start with an error, and the process
// must then exit as the host reports it rather than crash during teardown.
sealed class FailingApp : Application {
    protected override void OnLaunched(LaunchActivatedEventArgs args) {
        // XAML refuses a page holding a parser-built SwapChainBackgroundPanel
        // unless the page is the window's root visual, which an island's is not.
        Window.Current.Content = (UIElement)XamlReader.Load(
            "<Page xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
            "<SwapChainBackgroundPanel/></Page>");
        Window.Current.Activate();
        File.WriteAllText("startup-passed.txt", "launch reached");
    }
}
static class ManagedStartFailureTest {
    [STAThread] static void Main() { Application.Start(p => new FailingApp()); }
}

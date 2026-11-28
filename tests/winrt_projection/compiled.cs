using System;
using System.IO;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace Fixture
{
    public class LateControl : UserControl
    {
        public static readonly DependencyProperty ScoreProperty;
        static LateControl() { ScoreProperty = DependencyProperty.Register("Score", typeof(int), typeof(LateControl), new PropertyMetadata(0)); }
        public int Score { get { return (int)GetValue(ScoreProperty); } set { SetValue(ScoreProperty, value); } }
    }
}
class CompiledPage : Page, Windows.UI.Xaml.Markup.IComponentConnector
{
    internal object Target;
    internal int Id;
    public void Connect(int id, object target) { Id = id; Target = target; }
}
class CompiledApp : Application
{
    public class Model { public string Label { get { return "bound content"; } } }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    protected override void OnLaunched(Windows.ApplicationModel.Activation.LaunchActivatedEventArgs args)
    {
        try
        {
            var page = new CompiledPage();
            Application.LoadComponent(page, new Uri("ms-appx:///Compiled.xaml"));
            var title = (Button)page.FindName("title");
            Check(title != null && Equals(title.Content, "đá 💎"), "XBF names and Unicode text");
            Check(title.Height == 42.5 && !title.IsEnabled && title.Margin.Left == 1 && title.Margin.Bottom == 4, "XBF converted values");
            Check(page.Id == 37 && ReferenceEquals(page.Target, title), "XBF connection and wrapper identity");
            var original = page.Content;
            var bytes = File.ReadAllBytes("Compiled.xbf");
            File.WriteAllBytes("Compiled.xbf", new byte[32]);
            try { Application.LoadComponent(page, new Uri("ms-appx:///Compiled.xaml")); throw new Exception("invalid XBF accepted"); }
            catch (InvalidDataException) { }
            Check(ReferenceEquals(original, page.Content), "malformed XBF cannot replace the tree");
            File.WriteAllBytes("Compiled.xbf", bytes);
            var styledPage = new CompiledPage { DataContext = new Model() };
            Application.LoadComponent(styledPage, new Uri("ms-appx:///Templates.xaml"));
            var styled = (Button)styledPage.FindName("styled");
            Check(Equals(styled.Content, "bound content"), "XBF binding and static resource style");
            var values = typeof(Application).Assembly.GetType("AppxPortable.WinRT.Xaml.Values");
            var peer = (System.Windows.Controls.Button)values.GetMethod("Dependency", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, new object[] { styled });
            peer.ApplyTemplate();
            var face = (System.Windows.Controls.Border)peer.Template.FindName("face", peer);
            Check(face != null && ((System.Windows.Media.SolidColorBrush)face.Background).Color == System.Windows.Media.Colors.Red, "XBF deferred template instantiation");
            Check(Equals(styledPage.Resources["Word"], "template text"), "XBF Foundation string resource");
            var inherited = new CompiledPage();
            Application.LoadComponent(inherited, new Uri("ms-appx:///Inheritance.xaml"));
            var gridPeer = (System.Windows.Controls.Grid)values.GetMethod("Dependency", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, new object[] { inherited.Content });
            Check(((System.Windows.Media.SolidColorBrush)gridPeer.Background).Color == System.Windows.Media.Colors.Red, "XBF member inherited across distinct WPF peer classes");
            Check(Grid.GetRow((Button)inherited.FindName("rowButton")) == 2, "XBF declared attached-property owner");
            var late = new CompiledPage();
            Application.LoadComponent(late, new Uri("ms-appx:///LateControl.xaml"));
            Check(((Fixture.LateControl)late.Content).Score == 17, "XBF resolves game dependency properties before instance creation");
            File.WriteAllText("Shared.xaml", "<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'><Style x:Key='BasicTextStyle' TargetType='TextBlock'><Setter Property='FontSize' Value='27'/></Style></ResourceDictionary>");
            File.WriteAllText("Deferred.xaml", "<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'><ControlTemplate x:Key='Deferred' TargetType='Button'><TextBlock x:Name='deferredText' Style='{StaticResource BasicTextStyle}' Text='content'/></ControlTemplate></ResourceDictionary>");
            File.WriteAllText("AppResources.xaml", "<Application xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Application.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries><ResourceDictionary Source='Shared.xaml'/><ResourceDictionary Source='Deferred.xaml'/></ResourceDictionary.MergedDictionaries></ResourceDictionary></Application.Resources></Application>");
            Application.LoadComponent(this, new Uri("ms-appx:///AppResources.xaml"));
            var deferred = new System.Windows.Controls.Button { Template = (System.Windows.Controls.ControlTemplate)System.Windows.Application.Current.FindResource("Deferred") };
            deferred.ApplyTemplate();
            Check(((System.Windows.Controls.TextBlock)deferred.Template.FindName("deferredText", deferred)).FontSize == 27, "deferred template retains preceding merged application resources");
            Console.WriteLine("PASS: XBF 1 content, converted values, names, connections and malformed input");
            Environment.Exit(0);
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.Exit(1); }
    }
}
class CompiledTest
{
    [STAThread]
    static int Main() { Application.Start(p => new CompiledApp()); return 1; }
}

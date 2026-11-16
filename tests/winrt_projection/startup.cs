using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
class LayoutPanel : Panel
{
    internal int Measures, Arranges;
    protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size size)
    {
        Measures++;
        foreach (var child in (System.Collections.Generic.IEnumerable<UIElement>)Children) child.Measure(new Windows.Foundation.Size(100, 30));
        return new Windows.Foundation.Size(100, 30);
    }
    protected override Windows.Foundation.Size ArrangeOverride(Windows.Foundation.Size size)
    {
        Arranges++;
        foreach (var child in (System.Collections.Generic.IEnumerable<UIElement>)Children) child.Arrange(new Windows.Foundation.Rect(0, 0, size.Width, size.Height));
        return size;
    }
}
public class NavigationProbePage : Page { }
public class OtherNavigationProbePage : Page { }
class StartupApp : Application
{
    [DllImport("appxportable.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    static extern IntPtr ShimGetHostWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string className, string title);
    [DllImport("user32.dll", ExactSpelling = true)]
    static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("appxportable.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    static extern uint ShimXamlFrameCount();
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static object Peer(DependencyObject value)
    {
        return typeof(Application).Assembly.GetType("AppxPortable.WinRT.Xaml.Values").GetMethod("Dependency", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { value });
    }
    static object Call(object value, string method, params object[] args) { return value.GetType().GetMethod(method).Invoke(value, args); }
    static void Pump(Func<bool> ready)
    {
        var start = DateTime.UtcNow;
        while (!ready() && (DateTime.UtcNow - start).TotalSeconds < 8)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            System.Threading.Thread.Sleep(10);
        }
        Check(ready(), "dispatcher condition timed out");
    }
    protected override void OnLaunched(Windows.ApplicationModel.Activation.LaunchActivatedEventArgs args)
    {
        try
        {
            string mode = Environment.GetCommandLineArgs()[1];
            if (mode == "grid")
            {
                var facade = Assembly.Load("System.Runtime.WindowsRuntime.UI.Xaml");
                var length = facade.GetType("Windows.UI.Xaml.GridLength", true);
                var grid = new Grid();
                var gridPeer = (System.Windows.Controls.Grid)Peer(grid);
                gridPeer.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
                MethodInfo columnsGetter = null;
                foreach (var method in grid.GetType().GetMethods())
                    if (method.Name == "get_ColumnDefinitions" && method.ReturnType.FullName == "Windows.UI.Xaml.Controls.ColumnDefinitionCollection") columnsGetter = method;
                Check(columnsGetter != null, "Grid exposes the CLR-projected column list");
                var columns = (System.Collections.Generic.IList<Windows.UI.Xaml.Controls.ColumnDefinition>)columnsGetter.Invoke(grid, null);
                Check(columns.Count == 1, "Grid column collection shares markup definitions");
                var column = columns[0];
                var pixel = Activator.CreateInstance(length, new object[] { 240.0 });
                Call(column, "put_Width", pixel);
                Check(gridPeer.ColumnDefinitions[0].Width.Value == 240 && gridPeer.ColumnDefinitions[0].Width.IsAbsolute, "pixel width reaches WPF layout");
                var read = Call(column, "get_Width");
                Check((double)length.GetProperty("Value").GetValue(read, null) == 240, "GridLength round trip");
                Check(ReferenceEquals(column, columns[0]), "Grid definition wrapper identity");
                Check(ReferenceEquals(columns, columnsGetter.Invoke(grid, null)), "Grid collection wrapper identity");
                var added = new Windows.UI.Xaml.Controls.ColumnDefinition();
                columns.Add(added);
                Check(gridPeer.ColumnDefinitions.Count == 2 && ReferenceEquals(columns[1], added), "Grid definition list adds peers");
                columns.Remove(added);
                Check(gridPeer.ColumnDefinitions.Count == 1, "Grid definition list removes peers");
                var rows = (System.Collections.Generic.IList<Windows.UI.Xaml.Controls.RowDefinition>)Call(grid, "get_RowDefinitions");
                var row = new Windows.UI.Xaml.Controls.RowDefinition(); rows.Add(row);
                Call(row, "put_Height", pixel);
                Check(gridPeer.RowDefinitions.Count == 1 && gridPeer.RowDefinitions[0].Height.Value == 240, "row definitions share the layout peer");

                var units = facade.GetType("Windows.UI.Xaml.GridUnitType", true);
                var star = Activator.CreateInstance(length, new object[] { 2.0, Enum.Parse(units, "Star") });
                Call(column, "put_Width", star);
                Check(gridPeer.ColumnDefinitions[0].Width.IsStar && gridPeer.ColumnDefinitions[0].Width.Value == 2, "star width reaches WPF layout");
                Call(column, "put_Width", length.GetProperty("Auto").GetValue(null, null));
                Check(gridPeer.ColumnDefinitions[0].Width.IsAuto, "automatic width reaches WPF layout");
                try { Activator.CreateInstance(length, new object[] { double.NaN }); throw new Exception("invalid GridLength accepted"); }
                catch (TargetInvocationException error) { Check(error.InnerException is ArgumentException, "GridLength validates input"); }

            }
            else if (mode == "files")
            {
                var folder = Windows.Storage.ApplicationData.Current.LocalFolder;
                string path = folder.Path;
                Directory.CreateDirectory(Path.Combine(path, "nested-folder"));
                File.WriteAllText(Path.Combine(path, "Unicode-đá.txt"), "saved data");
                var files = (Windows.Foundation.IAsyncOperation<System.Collections.Generic.IReadOnlyList<Windows.Storage.StorageFile>>)folder.GetType().GetMethod("GetFilesAsync", Type.EmptyTypes).Invoke(folder, null);
                Pump(() => files.Status != Windows.Foundation.AsyncStatus.Started);
                var result = files.GetResults();
                bool found = false;
                foreach (var file in result)
                {
                    Check(file.Name != "nested-folder" && File.Exists(file.Path), "file listing excludes folders");
                    if (file.Name == "Unicode-đá.txt") found = true;
                }
                Check(found, "file listing retains Unicode names");
                var list = result as System.Collections.Generic.IList<Windows.Storage.StorageFile>;
                Check(list != null && list.IsReadOnly, "file listing is read-only");
                var child = (Windows.Foundation.IAsyncOperation<Windows.Storage.StorageFolder>)Call(folder, "GetFolderAsync", "nested-folder");
                Pump(() => child.Status != Windows.Foundation.AsyncStatus.Started);
                Check(child.GetResults().Name == "nested-folder" && Directory.Exists(child.GetResults().Path), "folder lookup returns an existing child");
                var missing = (Windows.Foundation.IAsyncOperation<Windows.Storage.StorageFolder>)Call(folder, "GetFolderAsync", "absent-folder");
                Pump(() => missing.Status != Windows.Foundation.AsyncStatus.Started);
                Check(missing.Status == Windows.Foundation.AsyncStatus.Error && missing.ErrorCode != null, "missing folder fails asynchronously");
                var wrongKind = (Windows.Foundation.IAsyncOperation<Windows.Storage.StorageFolder>)Call(folder, "GetFolderAsync", "Unicode-đá.txt");
                Pump(() => wrongKind.Status != Windows.Foundation.AsyncStatus.Started);
                Check(wrongKind.Status == Windows.Foundation.AsyncStatus.Error, "folder lookup rejects a file");
                try { Call(folder, "GetFolderAsync", "../outside"); throw new Exception("parent traversal accepted"); }
                catch (TargetInvocationException error) { Check(error.InnerException is ArgumentException, "folder lookup validates relative paths"); }
                var empty = (Windows.Foundation.IAsyncOperation<System.Collections.Generic.IReadOnlyList<Windows.Storage.StorageFile>>)child.GetResults().GetType().GetMethod("GetFilesAsync", Type.EmptyTypes).Invoke(child.GetResults(), null);
                Pump(() => empty.Status != Windows.Foundation.AsyncStatus.Started);
                Check(empty.GetResults().Count == 0, "empty folder returns an empty file view");
            }
            else if (mode == "panel")
            {
                var panel = new LayoutPanel(); panel.Children.Add(new Button());
                panel.Measure(new Windows.Foundation.Size(300, 200)); panel.Arrange(new Windows.Foundation.Rect(0, 0, 300, 200));
                Check(panel.Measures == 1 && panel.Arranges == 1 && panel.DesiredSize.Width == 100, "custom panel layout overrides and child measurement");
            }
            else if (mode == "appbar")
            {
                var bar = new AppBar(); int closed = 0;
                var token = bar.add_Closed((s, e) => { Check(ReferenceEquals(s, bar), "AppBar sender"); closed++; });
                Call(bar, "put_IsOpen", true); Call(bar, "put_IsOpen", false); Call(bar, "put_IsOpen", false);
                Check(closed == 1, "AppBar closes on a state transition");
                bar.remove_Closed(token); Call(bar, "put_IsOpen", true); Call(bar, "put_IsOpen", false);
                Check(closed == 1, "AppBar token removal");
                var pagePeer = (System.Windows.Controls.UserControl)Peer(new Page());
                pagePeer.Background = System.Windows.Media.Brushes.Red;
                pagePeer.ApplyTemplate();
                var pageRoot = (System.Windows.Controls.Panel)System.Windows.Media.VisualTreeHelper.GetChild(pagePeer, 0);
                Check(ReferenceEquals(pageRoot.Background, pagePeer.Background), "AppBar page template retains its background");
            }
            else if (mode == "search")
            {
                var search = new SearchBox(); int changed = 0;
                var token = search.add_QueryChanged((s, e) => { Check(ReferenceEquals(s, search) && (string)Call(e, "get_QueryText") == "Oxford", "search event arguments"); changed++; });
                ((System.Windows.Controls.TextBox)Peer(search)).Text = "Oxford";
                Check(changed == 1 && (string)Call(search, "get_QueryText") == "Oxford", "search text and event");
                search.remove_QueryChanged(token); ((System.Windows.Controls.TextBox)Peer(search)).Text = "removed";
                Check(changed == 1, "search token removal");
            }
            else if (mode == "light")
            {
                File.WriteAllText("Light.xaml", "<Application xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' RequestedTheme='Light'><Application.Resources><ResourceDictionary><ResourceDictionary.ThemeDictionaries>" +
                    "<ResourceDictionary x:Key='Dark'><x:String x:Key='Selected'>dark</x:String></ResourceDictionary><ResourceDictionary x:Key='Light'><x:String x:Key='Selected'>light</x:String></ResourceDictionary>" +
                    "</ResourceDictionary.ThemeDictionaries></ResourceDictionary></Application.Resources></Application>");
                Application.LoadComponent(this, new Uri("ms-appx:///Light.xaml"));
                var brush = (System.Windows.Media.SolidColorBrush)System.Windows.Application.Current.FindResource("ApplicationPageBackgroundThemeBrush");
                Check(brush.Color == System.Windows.Media.Colors.White, "light application palette");
                Check(Equals(Resources["Selected"], "light"), "requested theme dictionary selection");
                foreach (string key in new[] { "ListBoxBorderThemeBrush", "ListBoxDisabledForegroundThemeBrush", "ListBoxItemDisabledForegroundThemeBrush", "HeaderTextBlockStyle" })
                    Check(System.Windows.Application.Current.FindResource(key) != null, "startup theme resource " + key);
            }
            else if (mode == "navigation")
            {
                var frame = new Frame();
                Check(Call(frame, "get_CurrentSourcePageType") == null, "new frame has no source page");
                frame.Navigate(typeof(NavigationProbePage));
                Check(Equals(Call(frame, "get_CurrentSourcePageType"), typeof(NavigationProbePage)), "current page after navigation");
                frame.Navigate(typeof(OtherNavigationProbePage)); frame.GoBack();
                Check(Equals(Call(frame, "get_CurrentSourcePageType"), typeof(NavigationProbePage)), "current page after going back");
                frame.GoForward();
                Check(Equals(Call(frame, "get_CurrentSourcePageType"), typeof(OtherNavigationProbePage)), "current page after going forward");
            }
            else if (mode == "selection")
            {
                var list = new ListView(); var peer = (System.Windows.Controls.ListBox)Peer(list);
                peer.Items.Add("first"); peer.Items.Add("second"); int called = 0;
                Action<object, SelectionChangedEventArgs> handler = (s, e) =>
                {
                    Check(ReferenceEquals(s, list), "selection sender identity");
                    var added = (System.Collections.Generic.IList<object>)Call(e, "get_AddedItems");
                    var removed = (System.Collections.Generic.IList<object>)Call(e, "get_RemovedItems");
                    Check(added.Count == 1 && Equals(added[0], "second") && removed.Count == 0, "selection payload");
                    called++;
                };
                var add = typeof(Windows.UI.Xaml.Controls.Primitives.Selector).GetMethod("add_SelectionChanged");
                var callback = Delegate.CreateDelegate(add.GetParameters()[0].ParameterType, handler.Target, handler.Method);
                var token = add.Invoke(list, new object[] { callback });
                peer.SelectedItem = "second"; Check(called == 1, "selection delivery");
                Call(list, "remove_SelectionChanged", token); peer.SelectedItem = "first";
                Check(called == 1, "selection token removal");
                Check(Peer(new ListBox()) is System.Windows.Controls.ListBox, "ListBox constructor and peer");
            }
            else if (mode == "scroll")
            {
                var inner = new ScrollViewer(); var outer = new ScrollViewer();
                var input = (System.Windows.Controls.ScrollViewer)Peer(inner);
                var parent = (System.Windows.Controls.ScrollViewer)Peer(outer);
                input.Height = 60; input.Content = new System.Windows.Controls.Border { Height = 600 };
                var stack = new System.Windows.Controls.StackPanel(); stack.Children.Add(input); stack.Children.Add(new System.Windows.Controls.Border { Height = 600 });
                parent.Height = 100; parent.Width = 150; parent.Content = stack;
                Window.Current.Content = outer; Window.Current.Activate();
                Pump(() => input.ScrollableHeight > 0 && parent.ScrollableHeight > 0);
                AppxPortable.WinRT.Xaml.ScrollProperties.SetIsVerticalScrollChainingEnabled(input, true);
                input.ScrollToEnd(); parent.ScrollToTop(); parent.UpdateLayout();
                Pump(() => input.VerticalOffset == input.ScrollableHeight);
                input.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, -120) { RoutedEvent = System.Windows.UIElement.MouseWheelEvent });
                Pump(() => parent.VerticalOffset > 0);
                AppxPortable.WinRT.Xaml.ScrollProperties.SetIsVerticalScrollChainingEnabled(input, false);
                parent.ScrollToTop(); parent.UpdateLayout();
                Pump(() => parent.VerticalOffset == 0);
                input.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, -120) { RoutedEvent = System.Windows.UIElement.MouseWheelEvent });
                parent.UpdateLayout(); Check(parent.VerticalOffset == 0, "disabled scroll chaining retains the wheel");
                AppxPortable.WinRT.Xaml.ScrollProperties.SetHorizontalScrollMode(input, ScrollMode.Enabled);
                AppxPortable.WinRT.Xaml.ScrollProperties.SetIsHorizontalRailEnabled(input, true);
                AppxPortable.WinRT.Xaml.ScrollProperties.SetIsVerticalRailEnabled(input, false);
                Check(input.PanningMode == System.Windows.Controls.PanningMode.HorizontalFirst, "horizontal rail maps to WPF panning");
                AppxPortable.WinRT.Xaml.ScrollProperties.SetIsHorizontalRailEnabled(input, false);
                AppxPortable.WinRT.Xaml.ScrollProperties.SetIsVerticalRailEnabled(input, true);
                Check(input.PanningMode == System.Windows.Controls.PanningMode.VerticalFirst, "vertical rail maps to WPF panning");
            }
            else if (mode == "webview-loaded" || mode == "mainloop")
            {
                var grid = new Grid();
                var peer = (System.Windows.Controls.Grid)Peer(grid); peer.Background = System.Windows.Media.Brushes.LightSkyBlue;
                var web = new WebView(); var other = new WebView();
                if (mode == "webview-loaded") { grid.Children.Add(web); grid.Children.Add(other); } web.Width = 400; other.Width = 400; other.Margin = new Thickness(420, 0, 0, 0);
                web.add_Loaded((s, e) => web.NavigateToString("<html><body>article</body></html>"));
                other.add_Loaded((s, e) => other.NavigateToString("<html><body>welcome</body></html>"));
                Window.Current.Content = grid; Window.Current.Activate();
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) }; int attempts = 0;
                timer.Tick += (s, e) =>
                {
                    if (++attempts < 80 && ShimXamlFrameCount() == 0) return;
                    timer.Stop();
                    try { Check(ShimXamlFrameCount() > 0, "native host loop presents the first XAML frame"); Console.WriteLine("PASS: startup " + mode); Environment.Exit(0); }
                    catch (Exception error) { Console.Error.WriteLine(error); Environment.Exit(1); }
                };
                timer.Start(); return;
            }
            else if (mode == "webview")
            {
                var web = new WebView(); int notified = 0;
                var token = web.add_ScriptNotify((s, e) => { Check(ReferenceEquals(s, web) && (string)Call(e, "get_Value") == "ready", "ScriptNotify sender/value"); notified++; });
                var background = new Grid(); background.Children.Add(web); web.Margin = new Thickness(20);
                ((System.Windows.Controls.Grid)Peer(background)).Background = System.Windows.Media.Brushes.LightSkyBlue;
                Window.Current.Content = background; Window.Current.Activate();
                web.NavigateToString("<html><body>PEU HTML probe<script>window.external.notify('ready');</script></body></html>");
                Pump(() => notified == 1);
                IntPtr fallback = IntPtr.Zero;
                Pump(() => { fallback = FindWindowExW(ShimGetHostWindow(), IntPtr.Zero, "AppxPortableXamlFrame", null); return fallback != IntPtr.Zero; });
                var nativeView = Peer(web).GetType().GetProperty("Browser", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Peer(web), null);
                var browserWindow = (IntPtr)Call(nativeView, "get_Handle");
                bool browserAbove = false;
                for (IntPtr child = GetWindow(ShimGetHostWindow(), 5); child != IntPtr.Zero; child = GetWindow(child, 2))
                {
                    if (child == browserWindow) { browserAbove = true; break; }
                    if (child == fallback) break;
                }
                Check(browserAbove, "native HTML view is above the painted XAML fallback");
                web.remove_ScriptNotify(token);
                File.WriteAllText("Anchor.html", "<html><body><div id='second'>local navigation</div></body></html>");
                web.Navigate(new Uri("ms-appx:///Anchor.html?mode=1#second"));
                var nativeBrowser = Peer(web).GetType().GetProperty("Browser", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Peer(web), null);
                Pump(() => { var url = (Uri)Call(nativeBrowser, "get_Url"); return url != null && url.Query == "?mode=1" && url.Fragment == "#second"; });
                web.NavigateToString("<html><body>Second document<script>window.external.notify('ready');</script></body></html>");
                var browser = Peer(web).GetType().GetProperty("Browser", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Peer(web), null);
                Pump(() => { var document = Call(browser, "get_Document"); return document != null && ((string)Call(Call(document, "get_Body"), "get_InnerText")).Contains("Second document"); });
                Check(notified == 1, "ScriptNotify token removal");
                Window.Current.Content = new Page();
                Pump(() => (bool)Call(browser, "get_IsDisposed"));
            }
            else throw new Exception("Unknown case " + mode);
            Console.WriteLine("PASS: startup " + mode);
            Environment.Exit(0);
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.Exit(1); }
    }
}
class StartupTest
{
    [STAThread] static int Main(string[] args) { Application.Start(p => new StartupApp()); return 1; }
}

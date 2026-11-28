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
    [DllImport("user32.dll", ExactSpelling = true)]
    static extern IntPtr SendMessageW(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll", ExactSpelling = true)]
    static extern bool PostMessageW(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll", ExactSpelling = true)]
    static extern IntPtr GetFocus();
    [DllImport("user32.dll", ExactSpelling = true)]
    static extern bool GetKeyboardState(byte[] keys);
    [DllImport("user32.dll", ExactSpelling = true)]
    static extern bool SetKeyboardState(byte[] keys);
    [DllImport("user32.dll", ExactSpelling = true)]
    static extern uint MapVirtualKeyW(uint code, uint kind);
    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", ExactSpelling = true)]
    static extern bool GetClientRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll", ExactSpelling = true)]
    static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll", ExactSpelling = true)]
    static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", ExactSpelling = true)]
    static extern int GetWindowRgn(IntPtr window, IntPtr region);
    [DllImport("gdi32.dll", ExactSpelling = true)]
    static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll", ExactSpelling = true)]
    static extern bool PtInRegion(IntPtr region, int x, int y);
    [DllImport("gdi32.dll", ExactSpelling = true)]
    static extern bool DeleteObject(IntPtr value);
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static object Peer(DependencyObject value)
    {
        return typeof(Application).Assembly.GetType("AppxPortable.WinRT.Xaml.Values").GetMethod("Dependency", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { value });
    }
    static object Call(object value, string method, params object[] args) { return value.GetType().GetMethod(method).Invoke(value, args); }
    static void ClickHost(IntPtr host, int x, int y)
    {
        var keys = new byte[256]; Check(GetKeyboardState(keys), "read thread keyboard state");
        byte previous = keys[1];
        try
        {
            foreach (uint message in new uint[] { 0x0200, 0x0201, 0x0202 })
            {
                if (message == 0x0201) keys[1] = 0x80;
                if (message == 0x0202) keys[1] = 0;
                Check(SetKeyboardState(keys), "set scripted button state");
                SendMessageW(host, message, (keys[1] & 0x80) != 0 ? new IntPtr(1) : IntPtr.Zero, new IntPtr((y << 16) | x));
                var turn = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => turn.Continue = false));
                Dispatcher.PushFrame(turn);
            }
        }
        finally { keys[1] = previous; SetKeyboardState(keys); }
    }
    [DllImport("user32.dll", ExactSpelling = true)]
    static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll", ExactSpelling = true)]
    static extern bool SetForegroundWindow(IntPtr window);
    static void PressHostKey(IntPtr host, int key, bool extended)
    {
        Check(SetForegroundWindow(host), "activate physical keyboard target");
        keybd_event((byte)key, (byte)MapVirtualKeyW((uint)key, 0), extended ? 1U : 0U, UIntPtr.Zero);
        keybd_event((byte)key, (byte)MapVirtualKeyW((uint)key, 0), (extended ? 1U : 0U) | 2U, UIntPtr.Zero);
        for (int i = 0; i < 10; ++i)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            System.Threading.Thread.Sleep(10);
        }
    }
    static T Visual<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
    {
        var match = root as T; if (match != null) return match;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); ++i)
        {
            match = Visual<T>(System.Windows.Media.VisualTreeHelper.GetChild(root, i));
            if (match != null) return match;
        }
        return null;
    }
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
            if (mode == "index-recycle")
            {
                var list = new ListBox();
                var peer = (System.Windows.Controls.ListBox)Peer(list); peer.Width = 240; peer.Height = 480;
                var labels = new string[2046]; for (int i = 0; i < labels.Length; ++i) labels[i] = "Index " + i;
                var text = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));
                text.SetValue(System.Windows.FrameworkElement.HeightProperty, 60.0);
                text.SetBinding(System.Windows.Controls.TextBlock.TextProperty, new System.Windows.Data.Binding());
                peer.ItemTemplate = new System.Windows.DataTemplate { VisualTree = text }; peer.ItemsSource = labels;
                Window.Current.Content = list; Window.Current.Activate();
                Pump(() => peer.ItemContainerGenerator.ContainerFromIndex(0) != null);
                var visited = new System.Collections.Generic.Dictionary<System.Windows.FrameworkElement, object>(); int reused = 0;
                var panel = Visual<System.Windows.Controls.VirtualizingStackPanel>(peer);
                Check(panel != null && panel.Children.Count < 50, "index list virtualizes offscreen entries");
                foreach (System.Windows.FrameworkElement child in panel.Children) visited[child] = child.DataContext;
                foreach (int index in new[] { 200, 600, 1200 })
                {
                    peer.ScrollIntoView(labels[index]); peer.UpdateLayout();
                    Pump(() => peer.ItemContainerGenerator.ContainerFromIndex(index) != null);
                    var idle = new DispatcherFrame();
                    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => idle.Continue = false));
                    Dispatcher.PushFrame(idle);
                    foreach (System.Windows.FrameworkElement child in panel.Children)
                    {
                        object previous;
                        if (visited.TryGetValue(child, out previous) && !Equals(previous, child.DataContext)) reused++;
                        visited[child] = child.DataContext;
                    }
                    var item = (System.Windows.Controls.ListBoxItem)peer.ItemContainerGenerator.ContainerFromIndex(index);
                    Check(Equals(item.DataContext, labels[index]) && Visual<System.Windows.Controls.TextBlock>(item).Text == labels[index], "reused index rows display the new entry");
                }
                Console.WriteLine("INDEX: mode=" + System.Windows.Controls.VirtualizingPanel.GetVirtualizationMode(peer) + " created=" + visited.Count + " reused=" + reused);
                Check(reused > 0, "scrolling distant index entries recycles row containers instead of rebuilding every template");
            }
            else if (mode == "grid")
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
            else if (mode == "settings-flyout")
            {
                var page = new Grid(); var behind = new WebView(); page.Children.Add(behind);
                behind.NavigateToString("<html><head><title>Background article</title></head><body>Original article</body></html>");
                Window.Current.Content = page; Window.Current.Activate();
                var root = (System.Windows.FrameworkElement)typeof(Application).Assembly.GetType("AppxPortable.WinRT.Xaml.Host").GetProperty("Root", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null, null);
                Pump(() => root.ActualHeight > 150);
                Directory.CreateDirectory("Assets");
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(System.Windows.Media.Imaging.BitmapSource.Create(1, 1, 96, 96,
                    System.Windows.Media.PixelFormats.Bgra32, null, new byte[] { 0, 0, 255, 255 }, 4)));
                using (var stream = File.Create("Assets/pixel.png")) encoder.Save(stream);
                File.WriteAllText("Images.xaml", "<Grid xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'><Grid.Background><ImageBrush ImageSource='/Assets/pixel.png'/></Grid.Background><Image x:Name='valid' Source='/Assets/pixel.png'/></Grid>");
                var images = new Grid(); Application.LoadComponent(images, new Uri("ms-appx:///Images.xaml"));
                var image = (System.Windows.Controls.Image)Peer((Image)images.FindName("valid"));
                var brush = (System.Windows.Media.ImageBrush)((System.Windows.Controls.Grid)Peer(images)).Background;
                Check(((System.Windows.Media.Imaging.BitmapSource)image.Source).PixelWidth == 1 && ((System.Windows.Media.Imaging.BitmapSource)brush.ImageSource).PixelHeight == 1, "package Image and ImageBrush still decode valid assets");
                var flyout = new SettingsFlyout();
                Directory.CreateDirectory("View");
                File.WriteAllText("View/Settings.xaml", "<SettingsFlyout xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Width='500' Title='Layout' IconSource='Assets/SmallLogo.png' HeaderBackground='#FF143C70'><Grid><WebView x:Name='articleView' HorizontalAlignment='Left' VerticalAlignment='Top'/></Grid></SettingsFlyout>");
                Application.LoadComponent(flyout, new Uri("ms-appx:///View/Settings.xaml"));
                var peer = (System.Windows.Controls.ContentControl)Peer(flyout);
                var optionalIcon = (System.Windows.Media.Imaging.BitmapImage)peer.GetType().GetProperty("IconSource").GetValue(peer, null);
                Check(optionalIcon.UriSource.AbsoluteUri.EndsWith("/View/Assets/SmallLogo.png"), "optional icon retains the XAML-relative URI rather than guessing a different asset");
                var web = (WebView)flyout.FindName("articleView");
                var webPeer = (System.Windows.FrameworkElement)Peer(web);
                peer.SizeChanged += (sender, change) => { webPeer.Width = peer.Width - 80; webPeer.Height = peer.Height - 150; };
                web.NavigateToString("<html><head><title>Settings article</title></head><body>Layout help</body></html>");
                Call(flyout, "ShowIndependent");
                Pump(() => peer.IsLoaded && webPeer.IsLoaded && peer.IsArrangeValid);
                Check(root.IsAncestorOf(peer), "settings content belongs to the composited host tree");
                var point = peer.TranslatePoint(new System.Windows.Point(), root);
                Check(Math.Abs(point.X - (root.ActualWidth - 500)) < 1 && Math.Abs(peer.ActualHeight - root.ActualHeight) < 1, "settings flyout fills the right edge of the host");
                Check(peer.Height == root.ActualHeight && webPeer.Width == 420 && webPeer.Height == peer.Height - 150, "SizeChanged handlers can read finite Width and Height to size HTML content");
                Check((string)Call(flyout, "get_Title") == "Layout", "settings title survives compiled/source loading");
                var nativeBehind = (System.Windows.Forms.WebBrowser)Peer(behind).GetType().GetProperty("Browser", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Peer(behind), null);
                var region = CreateRectRgn(0, 0, 0, 0);
                try { Check(GetWindowRgn(nativeBehind.Handle, region) != 0 && !PtInRegion(region, (int)root.ActualWidth - 10, 10), "native article does not cover the settings header"); }
                finally { DeleteObject(region); }

                var back = (System.Windows.Controls.Button)peer.Template.FindName("SettingsBackButton", peer);
                Check(back != null && back.IsVisible, "settings header has a visible back button");
                var snapshot = new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                snapshot.Render(root);
                var screenshot = new System.Windows.Media.Imaging.PngBitmapEncoder(); screenshot.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(snapshot));
                using (var stream = File.Create("settings-tree.png")) screenshot.Save(stream);
                back.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Pump(() => !peer.IsLoaded);
                Check(!root.IsAncestorOf(peer), "Back dismisses an independent settings flyout");
                region = CreateRectRgn(0, 0, 0, 0);
                try { Check(GetWindowRgn(nativeBehind.Handle, region) == 0 || PtInRegion(region, (int)root.ActualWidth - 10, 10), "native article is restored after settings close"); }
                finally { DeleteObject(region); }

                Check(double.IsNaN(peer.Height) && peer.Width == 500, "closing restores the flyout's declared dimensions");
                Call(flyout, "Show"); Pump(() => peer.IsLoaded);
                var second = new SettingsFlyout(); Call(second, "ShowIndependent");
                var secondPeer = (System.Windows.FrameworkElement)Peer(second);
                Pump(() => secondPeer.IsLoaded);
                Check(!root.IsAncestorOf(peer) && root.IsAncestorOf(secondPeer), "showing another flyout replaces the first");
                Call(second, "Hide"); Pump(() => !secondPeer.IsLoaded);
                Call(flyout, "ShowIndependent"); Pump(() => peer.IsLoaded);
                var host = ShimGetHostWindow(); NativeRect bounds; GetWindowRect(host, out bounds);
                SetWindowPos(host, IntPtr.Zero, 0, 0, bounds.Right - bounds.Left + 70, bounds.Bottom - bounds.Top + 50, 0x0002 | 0x0004 | 0x0010);
                Pump(() => peer.Height == root.ActualHeight && Math.Abs(peer.TranslatePoint(new System.Windows.Point(), root).X - (root.ActualWidth - 500)) < 1);
                Check(peer.Height == root.ActualHeight, "open settings flyout follows host resize");
                PressHostKey(host, 0x1b, false);
                Pump(() => !peer.IsLoaded);
                Check(!root.IsAncestorOf(peer), "Escape dismisses settings");
                Call(flyout, "ShowIndependent"); Pump(() => peer.IsLoaded);
                ClickHost(host, 10, 10); Pump(() => !peer.IsLoaded);
                Check(!root.IsAncestorOf(peer), "clicking outside settings light-dismisses it");
                Call(flyout, "Hide");
            }
            else if (mode == "search-input")
            {
                var search = new SearchBox(); var second = new SearchBox();
                var browser = new WebView(); browser.NavigateToString("<html><body>Keyboard companion</body></html>");
                var grid = new Grid(); grid.Children.Add(search); grid.Children.Add(second); grid.Children.Add(browser);
                var browserPeer = (System.Windows.FrameworkElement)Peer(browser); browserPeer.Height = 100; browserPeer.VerticalAlignment = System.Windows.VerticalAlignment.Bottom;
                var text = (System.Windows.Controls.TextBox)Peer(search);
                var other = (System.Windows.Controls.TextBox)Peer(second);
                foreach (var input in new[] { text, other }) { input.Width = 280; input.Height = 40; input.HorizontalAlignment = System.Windows.HorizontalAlignment.Left; input.VerticalAlignment = System.Windows.VerticalAlignment.Top; }
                text.Margin = new System.Windows.Thickness(20, 20, 0, 0); other.Margin = new System.Windows.Thickness(20, 100, 0, 0);
                int changed = 0; search.add_QueryChanged((s, e) => changed++);
                Window.Current.Content = grid; Window.Current.Activate();
                Pump(() => text.IsLoaded && text.IsArrangeValid);
                var host = ShimGetHostWindow();
                ClickHost(host, 35, 35);
                Console.WriteLine("FOCUS: host="+host+" native=" + GetFocus() + " WPF=" + System.Windows.Input.Keyboard.FocusedElement + " keyboard=" + text.IsKeyboardFocused);
                foreach (char value in "Oxford café") Check(PostMessageW(host, 0x0102, new IntPtr(value), new IntPtr(1)), "queue host character");
                Pump(() => text.Text == "Oxford café");
                Check(text.Text == "Oxford café" && (string)Call(search, "get_QueryText") == text.Text && changed > 0, "host character messages update the clicked search box and QueryChanged");
                Console.WriteLine("TYPED: " + Uri.EscapeDataString(text.Text));
                text.PreviewKeyDown += (s, e) => Console.WriteLine("KEY: " + e.Key + " handled=" + e.Handled);
                int caret = text.CaretIndex;
                PressHostKey(host, 0x25, true);
                Console.WriteLine("LEFT: " + caret + " -> " + text.CaretIndex);
                Check(text.CaretIndex == caret - 1, "physical Left moves the caret");
                PressHostKey(host, 0x27, true);
                Check(text.CaretIndex == caret, "physical Right moves the caret");
                PressHostKey(host, 8, false);
                Console.WriteLine("EDITED: " + Uri.EscapeDataString(text.Text));
                Check(text.Text == "Oxford caf", "physical Backspace edits text");
                PressHostKey(host, 0x24, true);
                Check(text.CaretIndex == 0, "physical Home moves to the start");
                PressHostKey(host, 0x2e, true);
                Check(text.Text == "xford caf", "physical Delete removes the next character");
                PressHostKey(host, 0x23, true);
                Check(text.CaretIndex == text.Text.Length, "physical End moves to the end");
                ClickHost(host, 35, 115);
                Console.WriteLine("SECOND FOCUS: " + other.IsKeyboardFocused + " over=" + System.Windows.Input.Mouse.DirectlyOver + " capture=" + System.Windows.Input.Mouse.Captured);
                SendMessageW(host, 0x0102, new IntPtr('Z'), new IntPtr(1));
                Console.WriteLine("SECOND TEXT: " + other.Text + " first=" + text.Text);
                Pump(() => other.Text == "Z");
                Check(text.Text == "xford caf", "typing follows focus without changing the previous search box");
                var dispatcher = Windows.UI.Core.CoreWindow.GetForCurrentThread().Dispatcher;
                int intercepted = 0;
                var accelerator = dispatcher.add_AcceleratorKeyActivated((s, e) => { intercepted++; e.Handled = true; });
                SendMessageW(host, 0x0102, new IntPtr('X'), new IntPtr(1));
                SendMessageW(host, 0x0106, new IntPtr('X'), new IntPtr(0x20000001));
                Check(intercepted == 2 && other.Text == "Z", "handled character accelerators are not forwarded to the text field");
                dispatcher.remove_AcceleratorKeyActivated(accelerator);
            }
            else if (mode == "search")
            {
                var search = new SearchBox(); int changed = 0;
                var searchPeer = (System.Windows.Controls.TextBox)Peer(search);
                searchPeer.Measure(new System.Windows.Size(300, double.PositiveInfinity));
                Check(searchPeer.DesiredSize.Height >= 32 && searchPeer.FontSize >= 14, "search input has a readable default size rather than stock WPF's 15-pixel line");
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
            else if (mode == "webview-layout" || mode == "webview-resize" || mode == "webview-unicode" || mode == "webview-fonts")
            {
                var web = new WebView(); web.Width = 610; web.Height = 510;
                web.Margin = new Thickness(20, 30, 0, 0);
                var grid = new Grid(); grid.Children.Add(web);
                var element = (System.Windows.FrameworkElement)Peer(web);
                element.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                element.VerticalAlignment = System.Windows.VerticalAlignment.Top;
                Window.Current.Content = grid; Window.Current.Activate();
                string unicode = "déjà – naïve … →";
                web.NavigateToString("<html style='width:100%;height:100%'><head><title>Geometry</title></head><body style='width:100%;height:100%;margin:0;background:#1482c8'>" + (mode == "webview-unicode" ? unicode : "browser geometry") + "</body></html>");
                if (mode == "webview-fonts")
                    web.NavigateToString("<!DOCTYPE html><html><head><title>Geometry</title></head><body>" +
                        "<span id='requested' style='display:inline-block;font-family:Segoe UI;font-size:24px'>iiiiWWWWmmmm123456789</span>" +
                        "<span id='fallback' style='display:inline-block;font-family:system-ui,-apple-system,BlinkMacSystemFont,Arial,sans-serif;font-size:24px'>iiiiWWWWmmmm123456789</span>" +
                        "<span id='requestedItalic' style='display:inline-block;font-family:Segoe UI;font-size:24px;font-style:italic'>iiiiWWWWmmmm123456789</span>" +
                        "<span id='fallbackItalic' style='display:inline-block;font-family:system-ui,-apple-system,BlinkMacSystemFont,Arial,sans-serif;font-size:24px;font-style:italic'>iiiiWWWWmmmm123456789</span></body></html>");
                System.Windows.Forms.WebBrowser browser = null;
                Pump(() => { browser = (System.Windows.Forms.WebBrowser)Peer(web).GetType().GetProperty("Browser", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Peer(web), null); return browser != null && browser.Document != null && browser.Document.Body != null && browser.DocumentTitle == "Geometry"; });
                var transform = System.Windows.PresentationSource.FromVisual(element).CompositionTarget.TransformToDevice;
                int width = (int)Math.Round(element.ActualWidth * transform.M11), height = (int)Math.Round(element.ActualHeight * transform.M22);
                NativeRect bounds; GetClientRect(browser.Handle, out bounds);
                Console.WriteLine("GEOMETRY: WPF=" + width + "x" + height + " HWND=" + (bounds.Right - bounds.Left) + "x" + (bounds.Bottom - bounds.Top) + " Forms=" + browser.ClientSize);
                if (mode == "webview-fonts")
                {
                    bool segoe = false;
                    using (var fonts = new System.Drawing.Text.InstalledFontCollection()) foreach (var font in fonts.Families) if (font.Name.Equals("Segoe UI", StringComparison.OrdinalIgnoreCase)) segoe = true;
                    if (!segoe)
                    {
                        string family = browser.Document.GetElementById("requested").Style;
                        Check(family.IndexOf("Arial", StringComparison.OrdinalIgnoreCase) >= 0 && family.IndexOf("system-ui", StringComparison.OrdinalIgnoreCase) >= 0, "missing Segoe UI uses the Arial/system UI font stack");
                    }
                    if (!segoe)
                        foreach (var suffix in new[] { "", "Italic" })
                        {
                            int requested = browser.Document.GetElementById("requested" + suffix).OffsetRectangle.Width;
                            int fallback = browser.Document.GetElementById("fallback" + suffix).OffsetRectangle.Width;
                            Console.WriteLine("FONT METRICS: " + suffix + " requested=" + requested + " fallback=" + fallback);
                            Check(Math.Abs(requested - fallback) <= 1, "missing Segoe UI uses the installed outline UI font with its regular/italic styling");
                        }
                }
                else if (mode == "webview-unicode")
                {
                    string actual = browser.Document.Body.InnerText.Trim();
                    Console.WriteLine("HTML TEXT: " + Uri.EscapeDataString(actual));
                    Check(actual == unicode, "NavigateToString preserves Unicode without requiring a charset declaration");
                }
                else if (mode == "webview-layout")
                {
                    Console.WriteLine("HTML VIEWPORT: " + browser.Document.Window.Size);
                    Check(browser.Document.Window.Size.Width >= width - 20 && browser.Document.Window.Size.Height >= height - 20, "embedded HTML viewport follows its native outer browser beyond the default 250 pixels");
                }
                else
                {
                    var host = ShimGetHostWindow(); NativeRect before, window;
                    GetClientRect(host, out before); GetWindowRect(host, out window);
                    SetWindowPos(host, IntPtr.Zero, 0, 0, window.Right - window.Left + 100, window.Bottom - window.Top + 80, 0x0016);
                    Pump(() => { NativeRect now; GetClientRect(host, out now); return now.Right != before.Right; });
                    element.UpdateLayout();
                    GetClientRect(browser.Handle, out bounds);
                    Console.WriteLine("RESIZED: HWND=" + (bounds.Right - bounds.Left) + "x" + (bounds.Bottom - bounds.Top));
                    Check(bounds.Right - bounds.Left == width && bounds.Bottom - bounds.Top == height, "host resize preserves the HTML view's independently arranged bounds");
                }
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

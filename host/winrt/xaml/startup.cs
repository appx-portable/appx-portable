using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace AppxPortable.WinRT.Xaml
{
    public sealed class PanelPeer : Panel
    {
        protected override Size MeasureOverride(Size available)
        {
            var panel = (global::Windows.UI.Xaml.FrameworkElement)Peers.Existing(this);
            var size = panel.MeasureLayout(new global::Windows.Foundation.Size(available.Width, available.Height));
            return new Size(size.Width, size.Height);
        }
        protected override Size ArrangeOverride(Size finalSize)
        {
            var panel = (global::Windows.UI.Xaml.FrameworkElement)Peers.Existing(this);
            var size = panel.ArrangeLayout(new global::Windows.Foundation.Size(finalSize.Width, finalSize.Height));
            return new Size(size.Width, size.Height);
        }
    }
    public sealed class AppBarPeer : ContentControl
    {
        public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register("IsOpen", typeof(bool), typeof(AppBarPeer),
            new PropertyMetadata(false, (d, e) => ((AppBarPeer)d).Changed((bool)e.OldValue, (bool)e.NewValue)));
        public bool IsOpen { get { return (bool)GetValue(IsOpenProperty); } set { SetValue(IsOpenProperty, value); } }
        internal event EventHandler Closed;
        public AppBarPeer() { Visibility = Visibility.Collapsed; }
        private void Changed(bool previous, bool current)
        {
            Visibility = current ? Visibility.Visible : Visibility.Collapsed;
            if (previous && !current && Closed != null) Closed(this, EventArgs.Empty);
        }
    }
    public sealed class SearchBoxPeer : TextBox
    {
        static SearchBoxPeer()
        {
            MinHeightProperty.OverrideMetadata(typeof(SearchBoxPeer), new FrameworkPropertyMetadata(32.0));
            FontSizeProperty.OverrideMetadata(typeof(SearchBoxPeer), new FrameworkPropertyMetadata(15.0));
            FontFamilyProperty.OverrideMetadata(typeof(SearchBoxPeer), new FrameworkPropertyMetadata(new System.Windows.Media.FontFamily("Segoe UI, Arial, Global User Interface")));
            PaddingProperty.OverrideMetadata(typeof(SearchBoxPeer), new FrameworkPropertyMetadata(new Thickness(8, 4, 8, 4)));
            VerticalContentAlignmentProperty.OverrideMetadata(typeof(SearchBoxPeer), new FrameworkPropertyMetadata(VerticalAlignment.Center));
        }
        public static readonly DependencyProperty SearchHistoryEnabledProperty = DependencyProperty.Register("SearchHistoryEnabled", typeof(bool), typeof(SearchBoxPeer),
            new PropertyMetadata(true, (d, e) => { if ((bool)e.NewValue) throw Errors.Unimplemented("Windows.UI.Xaml.Controls.SearchBox.SearchHistoryEnabled"); }));
        public bool SearchHistoryEnabled { get { return (bool)GetValue(SearchHistoryEnabledProperty); } set { SetValue(SearchHistoryEnabledProperty, value); } }
    }

    public sealed class SettingsFlyoutPeer : ContentControl
    {
        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register("Title", typeof(string), typeof(SettingsFlyoutPeer), new PropertyMetadata(""));
        public static readonly DependencyProperty HeaderBackgroundProperty = DependencyProperty.Register("HeaderBackground", typeof(System.Windows.Media.Brush), typeof(SettingsFlyoutPeer), new PropertyMetadata(System.Windows.Media.Brushes.DimGray));
        public static readonly DependencyProperty HeaderForegroundProperty = DependencyProperty.Register("HeaderForeground", typeof(System.Windows.Media.Brush), typeof(SettingsFlyoutPeer), new PropertyMetadata(System.Windows.Media.Brushes.White));
        public static readonly DependencyProperty IconSourceProperty = DependencyProperty.Register("IconSource", typeof(System.Windows.Media.ImageSource), typeof(SettingsFlyoutPeer));
        public string Title { get { return (string)GetValue(TitleProperty); } set { SetValue(TitleProperty, value); } }
        public System.Windows.Media.Brush HeaderBackground { get { return (System.Windows.Media.Brush)GetValue(HeaderBackgroundProperty); } set { SetValue(HeaderBackgroundProperty, value); } }
        public System.Windows.Media.Brush HeaderForeground { get { return (System.Windows.Media.Brush)GetValue(HeaderForegroundProperty); } set { SetValue(HeaderForegroundProperty, value); } }
        public System.Windows.Media.ImageSource IconSource { get { return (System.Windows.Media.ImageSource)GetValue(IconSourceProperty); } set { SetValue(IconSourceProperty, value); } }
        private static SettingsFlyoutPeer current;
        private PopupPeer popup;
        private bool independent;
        private object savedWidth, savedHeight;
        private double requestedWidth;
        private IInputElement previousFocus;

        internal static Rect Occlusion(WebViewPeer view)
        {
            if (current == null || !current.IsLoaded || current.IsAncestorOf(view)) return Rect.Empty;
            return new Rect(current.TranslatePoint(new Point(), Host.Root), new Size(current.ActualWidth, current.ActualHeight));
        }
        internal static void DismissOutside(WebViewPeer view)
        {
            if (current != null && !current.IsAncestorOf(view)) current.Hide();
        }

        static SettingsFlyoutPeer()
        {
            WidthProperty.OverrideMetadata(typeof(SettingsFlyoutPeer), new FrameworkPropertyMetadata(346.0));
        }
        private static Binding FromControl(string name)
        {
            return new Binding(name) { RelativeSource = RelativeSource.TemplatedParent };
        }
        public SettingsFlyoutPeer()
        {
            SetResourceReference(BackgroundProperty, "ApplicationPageBackgroundThemeBrush");
            SetResourceReference(ForegroundProperty, "ApplicationForegroundThemeBrush");
            var root = new FrameworkElementFactory(typeof(DockPanel));
            root.SetBinding(Panel.BackgroundProperty, FromControl("Background"));
            var header = new FrameworkElementFactory(typeof(DockPanel));
            header.SetValue(DockPanel.DockProperty, Dock.Top);
            header.SetValue(HeightProperty, 80.0);
            header.SetBinding(Panel.BackgroundProperty, FromControl("HeaderBackground"));
            var back = new FrameworkElementFactory(typeof(Button), "SettingsBackButton");
            back.SetValue(WidthProperty, 48.0);
            back.SetBinding(ForegroundProperty, FromControl("HeaderForeground"));
            back.SetValue(BackgroundProperty, System.Windows.Media.Brushes.Transparent);
            back.SetValue(BorderThicknessProperty, new Thickness(0));
            back.AddHandler(Button.ClickEvent, new RoutedEventHandler((s, e) => Back()));
            // Header navigation remains visible when Windows symbol fonts are absent.
            var arrow = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
            arrow.SetValue(System.Windows.Shapes.Path.DataProperty, System.Windows.Media.Geometry.Parse("M 12,0 L 0,12 L 12,24 M 0,12 L 28,12"));
            arrow.SetValue(WidthProperty, 28.0); arrow.SetValue(HeightProperty, 24.0);
            arrow.SetValue(System.Windows.Shapes.Shape.StretchProperty, System.Windows.Media.Stretch.Uniform);
            arrow.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 2.0);
            arrow.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, FromControl("HeaderForeground"));
            back.AppendChild(arrow);
            header.AppendChild(back);
            var icon = new FrameworkElementFactory(typeof(Image));
            icon.SetValue(DockPanel.DockProperty, Dock.Right);
            icon.SetValue(WidthProperty, 30.0); icon.SetValue(HeightProperty, 30.0);
            icon.SetValue(MarginProperty, new Thickness(12));
            icon.SetBinding(Image.SourceProperty, FromControl("IconSource"));
            header.AppendChild(icon);
            var title = new FrameworkElementFactory(typeof(TextBlock));
            title.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            title.SetValue(TextBlock.FontSizeProperty, 24.0);
            title.SetBinding(TextBlock.TextProperty, FromControl("Title"));
            title.SetBinding(TextBlock.ForegroundProperty, FromControl("HeaderForeground"));
            header.AppendChild(title);
            root.AppendChild(header);
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(MarginProperty, new Thickness(40, 0, 40, 70));
            content.SetBinding(ContentPresenter.ContentProperty, FromControl("Content"));
            root.AppendChild(content);
            Template = new ControlTemplate(typeof(SettingsFlyoutPeer)) { VisualTree = root };
            PreviewKeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape) { Hide(); e.Handled = true; } };
        }
        private void Back()
        {
            bool showSettings = !independent;
            Hide();
            if (showSettings) global::Windows.UI.ApplicationSettings.SettingsPane.Show();
        }
        internal void Show(bool showIndependent)
        {
            if (current == this) { independent = showIndependent; return; }
            if (current != null) current.Hide();
            current = this;
            independent = showIndependent;
            savedWidth = ReadLocalValue(WidthProperty); savedHeight = ReadLocalValue(HeightProperty);
            requestedWidth = Width;
            previousFocus = System.Windows.Input.Keyboard.FocusedElement;
            popup = new PopupPeer { Child = this, IsLightDismissEnabled = true };
            popup.Closed += (s, e) => Restore();
            Host.Layers.SizeChanged += Resize;
            Place();
            popup.IsOpen = true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
            {
                if (current != this) return;
                ApplyTemplate();
                var button = Template.FindName("SettingsBackButton", this) as Button;
                if (button != null) button.Focus();
            }));
        }
        internal void Hide()
        {
            if (popup != null) popup.IsOpen = false;
        }
        private void Resize(object sender, SizeChangedEventArgs args) { Place(); }
        private void Place()
        {
            var root = Host.Layers;
            Width = Math.Min(double.IsNaN(requestedWidth) ? 346 : requestedWidth, root.ActualWidth);
            Height = root.ActualHeight;
            popup.HorizontalOffset = root.ActualWidth - Width;
        }
        private void Restore()
        {
            Host.Layers.SizeChanged -= Resize;
            popup.Child = null; popup = null;
            if (current == this) current = null;
            if (savedWidth == DependencyProperty.UnsetValue) ClearValue(WidthProperty); else SetValue(WidthProperty, savedWidth);
            if (savedHeight == DependencyProperty.UnsetValue) ClearValue(HeightProperty); else SetValue(HeightProperty, savedHeight);
            if (previousFocus != null) System.Windows.Input.Keyboard.Focus(previousFocus);
            previousFocus = null;
        }
    }
    public sealed partial class PagePeer
    {
        public static readonly DependencyProperty TopAppBarProperty = DependencyProperty.Register("TopAppBar", typeof(AppBarPeer), typeof(PagePeer));
        public static readonly DependencyProperty BottomAppBarProperty = DependencyProperty.Register("BottomAppBar", typeof(AppBarPeer), typeof(PagePeer));
        public AppBarPeer TopAppBar { get { return (AppBarPeer)GetValue(TopAppBarProperty); } set { SetValue(TopAppBarProperty, value); } }
        public AppBarPeer BottomAppBar { get { return (AppBarPeer)GetValue(BottomAppBarProperty); } set { SetValue(BottomAppBarProperty, value); } }
        public global::Windows.UI.Xaml.Navigation.NavigationCacheMode NavigationCacheMode
        {
            get { return ((global::Windows.UI.Xaml.Controls.Page)Peers.Wrap(this)).NavigationCacheMode; }
            set { ((global::Windows.UI.Xaml.Controls.Page)Peers.Wrap(this)).NavigationCacheMode = value; }
        }
        public PagePeer()
        {
            var root = new FrameworkElementFactory(typeof(Grid));
            root.SetBinding(Panel.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent, TargetNullValue = System.Windows.Media.Brushes.Transparent });
            foreach (string member in new[] { "Content", "TopAppBar", "BottomAppBar" })
            {
                var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
                presenter.SetBinding(ContentPresenter.ContentProperty, new Binding(member) { RelativeSource = RelativeSource.TemplatedParent });
                if (member != "Content") presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, member == "TopAppBar" ? VerticalAlignment.Top : VerticalAlignment.Bottom);
                root.AppendChild(presenter);
            }
            Template = new ControlTemplate(typeof(PagePeer)) { VisualTree = root };
        }
    }
}

namespace Windows.UI.Xaml
{
    public partial class FrameworkElement
    {
        [AppxPortable.WinRT.Xaml.Raised(typeof(Controls.Panel))]
        protected virtual global::Windows.Foundation.Size MeasureOverride(global::Windows.Foundation.Size availableSize) { return new global::Windows.Foundation.Size(0, 0); }
        [AppxPortable.WinRT.Xaml.Raised(typeof(Controls.Panel))]
        protected virtual global::Windows.Foundation.Size ArrangeOverride(global::Windows.Foundation.Size finalSize) { return finalSize; }
        internal global::Windows.Foundation.Size MeasureLayout(global::Windows.Foundation.Size value) { global::Windows.Foundation.Size result = default(global::Windows.Foundation.Size); AppxPortable.WinRT.Xaml.Host.Callback(() => result = MeasureOverride(value)); return result; }
        internal global::Windows.Foundation.Size ArrangeLayout(global::Windows.Foundation.Size value) { global::Windows.Foundation.Size result = default(global::Windows.Foundation.Size); AppxPortable.WinRT.Xaml.Host.Callback(() => result = ArrangeOverride(value)); return result; }
    }
}

namespace Windows.UI.Xaml.Controls
{
    public partial class SettingsFlyout
    {
        public void Show() { CheckThread(); ((AppxPortable.WinRT.Xaml.SettingsFlyoutPeer)AppxPortable.WinRT.Xaml.Values.Dependency(this)).Show(false); }
        public void ShowIndependent() { CheckThread(); ((AppxPortable.WinRT.Xaml.SettingsFlyoutPeer)AppxPortable.WinRT.Xaml.Values.Dependency(this)).Show(true); }
        public void Hide() { CheckThread(); ((AppxPortable.WinRT.Xaml.SettingsFlyoutPeer)AppxPortable.WinRT.Xaml.Values.Dependency(this)).Hide(); }
    }
    public partial class AppBar
    {
        private Dictionary<long, Delegate> closed;
        public EventRegistrationToken add_Closed(EventHandler<object> handler)
        {
            CheckThread(); if (handler == null) throw new ArgumentNullException("handler");
            EventHandler callback = (s, e) => AppxPortable.WinRT.Xaml.Host.Callback(() => handler(this, null));
            ((AppxPortable.WinRT.Xaml.AppBarPeer)AppxPortable.WinRT.Xaml.Values.Dependency(this)).Closed += callback;
            return AppxPortable.WinRT.Xaml.Tokens.Add(ref closed, callback);
        }
        public void remove_Closed(EventRegistrationToken token)
        {
            CheckThread(); var callback = AppxPortable.WinRT.Xaml.Tokens.Remove(closed, token) as EventHandler;
            if (callback != null) ((AppxPortable.WinRT.Xaml.AppBarPeer)AppxPortable.WinRT.Xaml.Values.Dependency(this)).Closed -= callback;
        }
    }
    public sealed partial class SearchBox
    {
        private Dictionary<long, Delegate> queryChanged;
        public string QueryText { get { return ((AppxPortable.WinRT.Xaml.SearchBoxPeer)AppxPortable.WinRT.Xaml.Values.Dependency(this)).Text; } }
        public EventRegistrationToken add_QueryChanged(global::Windows.Foundation.TypedEventHandler<SearchBox, SearchBoxQueryChangedEventArgs> handler)
        {
            CheckThread(); if (handler == null) throw new ArgumentNullException("handler");
            var input = (AppxPortable.WinRT.Xaml.SearchBoxPeer)AppxPortable.WinRT.Xaml.Values.Dependency(this);
            TextChangedEventHandler callback = (s, e) => AppxPortable.WinRT.Xaml.Host.Callback(() => handler(this, new SearchBoxQueryChangedEventArgs(input.Text)));
            input.TextChanged += callback;
            return AppxPortable.WinRT.Xaml.Tokens.Add(ref queryChanged, callback);
        }
        public void remove_QueryChanged(EventRegistrationToken token)
        {
            CheckThread(); var callback = AppxPortable.WinRT.Xaml.Tokens.Remove(queryChanged, token) as TextChangedEventHandler;
            if (callback != null) ((AppxPortable.WinRT.Xaml.SearchBoxPeer)AppxPortable.WinRT.Xaml.Values.Dependency(this)).TextChanged -= callback;
        }
    }
    public sealed partial class SearchBoxQueryChangedEventArgs
    {
        private readonly string text;
        internal SearchBoxQueryChangedEventArgs(string text) { this.text = text; }
        public string QueryText { get { return text; } }
    }
}

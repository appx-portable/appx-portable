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
        public static readonly DependencyProperty SearchHistoryEnabledProperty = DependencyProperty.Register("SearchHistoryEnabled", typeof(bool), typeof(SearchBoxPeer),
            new PropertyMetadata(true, (d, e) => { if ((bool)e.NewValue) throw Errors.Unimplemented("Windows.UI.Xaml.Controls.SearchBox.SearchHistoryEnabled"); }));
        public bool SearchHistoryEnabled { get { return (bool)GetValue(SearchHistoryEnabledProperty); } set { SetValue(SearchHistoryEnabledProperty, value); } }
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

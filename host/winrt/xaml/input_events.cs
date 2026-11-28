using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using AppxPortable.WinRT.Xaml;
using Windows.UI.Xaml.Input;

namespace Windows.UI.Xaml.Input
{
    public sealed partial class PointerRoutedEventArgs
    {
        private readonly global::System.Windows.Input.MouseEventArgs args;
        internal PointerRoutedEventArgs(global::System.Windows.Input.MouseEventArgs args)
            : base(Values.FromWpf(args.OriginalSource, typeof(object))) { this.args = args; }
        public bool Handled { get { return args.Handled; } set { args.Handled = value; } }
    }
    public sealed partial class TappedRoutedEventArgs
    {
        private readonly global::System.Windows.RoutedEventArgs args;
        internal TappedRoutedEventArgs(global::System.Windows.RoutedEventArgs args)
            : base(Values.FromWpf(args.OriginalSource, typeof(object))) { this.args = args; }
        public bool Handled { get { return args.Handled; } set { args.Handled = value; } }
    }
}

namespace Windows.UI.Xaml
{
    public partial class UIElement
    {
        private Dictionary<long, Delegate> entered, exited, pressed, taps;
        private global::System.Windows.UIElement InputPeer { get { return (global::System.Windows.UIElement)Values.Dependency(this); } }

        public EventRegistrationToken add_PointerEntered(PointerEventHandler handler) { return Pointer(ref entered, handler, global::System.Windows.UIElement.MouseEnterEvent, false); }
        public void remove_PointerEntered(EventRegistrationToken token) { RemovePointer(entered, token, global::System.Windows.UIElement.MouseEnterEvent); }
        public EventRegistrationToken add_PointerExited(PointerEventHandler handler) { return Pointer(ref exited, handler, global::System.Windows.UIElement.MouseLeaveEvent, false); }
        public void remove_PointerExited(EventRegistrationToken token) { RemovePointer(exited, token, global::System.Windows.UIElement.MouseLeaveEvent); }
        public EventRegistrationToken add_PointerPressed(PointerEventHandler handler) { return Pointer(ref pressed, handler, global::System.Windows.UIElement.MouseDownEvent, true); }
        public void remove_PointerPressed(EventRegistrationToken token) { RemovePointer(pressed, token, global::System.Windows.UIElement.MouseDownEvent); }

        private EventRegistrationToken Pointer(ref Dictionary<long, Delegate> table, PointerEventHandler handler, global::System.Windows.RoutedEvent routed, bool button)
        {
            if (handler == null) throw new ArgumentNullException("handler");
            var peer = InputPeer;
            Delegate callback;
            if (button) callback = new global::System.Windows.Input.MouseButtonEventHandler((s, e) => Host.Callback(() => handler(this, new PointerRoutedEventArgs(e))));
            else callback = new global::System.Windows.Input.MouseEventHandler((s, e) => Host.Callback(() => handler(this, new PointerRoutedEventArgs(e))));
            peer.AddHandler(routed, callback);
            return Tokens.Add(ref table, callback);
        }
        private void RemovePointer(Dictionary<long, Delegate> table, EventRegistrationToken token, global::System.Windows.RoutedEvent routed)
        {
            var peer = InputPeer;
            var callback = Tokens.Remove(table, token);
            if (callback != null) peer.RemoveHandler(routed, callback);
        }

        // WPF owns tap routing and Handled; the small adapter below recognizes
        // desktop mouse press/release and raises one bubbling WPF event.
        public EventRegistrationToken add_Tapped(TappedEventHandler handler)
        {
            if (handler == null) throw new ArgumentNullException("handler");
            var peer = InputPeer;
            global::System.Windows.RoutedEventHandler callback = (s, e) => Host.Callback(() => handler(this, new TappedRoutedEventArgs(e)));
            TapEvents.Attach(peer);
            peer.AddHandler(TapEvents.Event, callback);
            return Tokens.Add(ref taps, callback);
        }
        public void remove_Tapped(EventRegistrationToken token)
        {
            var peer = InputPeer;
            var callback = Tokens.Remove(taps, token);
            if (callback == null) return;
            peer.RemoveHandler(TapEvents.Event, callback);
            TapEvents.Detach(peer);
        }
    }
}

namespace AppxPortable.WinRT.Xaml
{
    internal static class TapEvents
    {
        internal static readonly System.Windows.RoutedEvent Event = System.Windows.EventManager.RegisterRoutedEvent(
            "AppxPortableTapped", System.Windows.RoutingStrategy.Bubble, typeof(System.Windows.RoutedEventHandler), typeof(System.Windows.UIElement));
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<System.Windows.UIElement, Tracker> trackers =
            new System.Runtime.CompilerServices.ConditionalWeakTable<System.Windows.UIElement, Tracker>();
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<System.Windows.Input.MouseButtonEventArgs, Receipt> receipts =
            new System.Runtime.CompilerServices.ConditionalWeakTable<System.Windows.Input.MouseButtonEventArgs, Receipt>();
        private sealed class Receipt { internal bool Sent; }

        internal static void Attach(System.Windows.UIElement peer)
        {
            var tracker = trackers.GetValue(peer, p => new Tracker(p));
            if (++tracker.Count != 1) return;
            peer.AddHandler(System.Windows.UIElement.MouseLeftButtonDownEvent, tracker.Down, true);
            peer.AddHandler(System.Windows.UIElement.MouseLeftButtonUpEvent, tracker.Up, true);
        }
        internal static void Detach(System.Windows.UIElement peer)
        {
            Tracker tracker;
            if (!trackers.TryGetValue(peer, out tracker) || --tracker.Count != 0) return;
            peer.RemoveHandler(System.Windows.UIElement.MouseLeftButtonDownEvent, tracker.Down);
            peer.RemoveHandler(System.Windows.UIElement.MouseLeftButtonUpEvent, tracker.Up);
            trackers.Remove(peer);
        }
        private sealed class Tracker
        {
            internal int Count;
            internal readonly System.Windows.Input.MouseButtonEventHandler Down, Up;
            private readonly System.Windows.UIElement peer;
            private bool down;
            private System.Windows.Point start;
            internal Tracker(System.Windows.UIElement peer)
            {
                this.peer = peer;
                Down = Press; Up = Release;
            }
            private void Press(object sender, System.Windows.Input.MouseButtonEventArgs args)
            {
                down = args.ChangedButton == System.Windows.Input.MouseButton.Left;
                if (down) start = args.GetPosition(peer);
            }
            private void Release(object sender, System.Windows.Input.MouseButtonEventArgs args)
            {
                bool pending = down; down = false;
                if (!pending || args.ChangedButton != System.Windows.Input.MouseButton.Left) return;
                var receipt = receipts.GetValue(args, e => new Receipt());
                if (receipt.Sent) return;
                var end = args.GetPosition(peer);
                if (Math.Abs(end.X - start.X) > System.Windows.SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(end.Y - start.Y) > System.Windows.SystemParameters.MinimumVerticalDragDistance) return;
                // MouseLeftButtonUp is re-raised along its route. The nearest
                // subscribed element synthesizes it once; WPF then bubbles the tap.
                receipt.Sent = true;
                peer.RaiseEvent(new System.Windows.RoutedEventArgs(Event, args.OriginalSource));
            }
        }
    }
}

namespace Windows.UI.Xaml.Controls
{
    public sealed partial class SelectionChangedEventArgs
    {
        private readonly IList<object> added, removed;
        internal SelectionChangedEventArgs(global::System.Windows.Controls.SelectionChangedEventArgs args)
            : base(Values.FromWpf(args.OriginalSource, typeof(object)))
        {
            added = Copy(args.AddedItems); removed = Copy(args.RemovedItems);
        }
        private static IList<object> Copy(global::System.Collections.IList values)
        {
            var result = new List<object>();
            foreach (object value in values) result.Add(Values.FromWpf(value, typeof(object)));
            return result.AsReadOnly();
        }
        public IList<object> AddedItems { get { return added; } }
        public IList<object> RemovedItems { get { return removed; } }
    }
    public partial class ListBox
    {
        public void ScrollIntoView(object item)
        {
            ((global::System.Windows.Controls.ListBox)Values.Dependency(this)).ScrollIntoView(Values.ToWpf(item, typeof(object)));
        }
    }
}

namespace Windows.UI.Xaml.Controls.Primitives
{
    public partial class Selector
    {
        private Dictionary<long, Delegate> selectionChanged;
        public EventRegistrationToken add_SelectionChanged(global::Windows.UI.Xaml.Controls.SelectionChangedEventHandler handler)
        {
            CheckThread(); if (handler == null) throw new ArgumentNullException("handler");
            global::System.Windows.Controls.SelectionChangedEventHandler callback = (s, e) =>
                Host.Callback(() => handler(this, new global::Windows.UI.Xaml.Controls.SelectionChangedEventArgs(e)));
            ((global::System.Windows.Controls.Primitives.Selector)Values.Dependency(this)).SelectionChanged += callback;
            return Tokens.Add(ref selectionChanged, callback);
        }
        public void remove_SelectionChanged(EventRegistrationToken token)
        {
            CheckThread(); var callback = Tokens.Remove(selectionChanged, token) as global::System.Windows.Controls.SelectionChangedEventHandler;
            if (callback != null) ((global::System.Windows.Controls.Primitives.Selector)Values.Dependency(this)).SelectionChanged -= callback;
        }
    }
}

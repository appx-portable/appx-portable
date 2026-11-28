using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AppxPortable.WinRT.Xaml
{
    // Windows.UI.Xaml builds its theme transitions from aero.msstyles' Animations
    // storyboards, read through uxtheme's GetThemeAnimationProperty, which Wine lacks.
    // These are those storyboards' values; all use timing function 4,
    // cubic-bezier(0.1, 0.9, 0.2, 1):
    //   Entrance (32): translate from (FromHorizontalOffset, FromVerticalOffset) and
    //     opacity 0 to 1, both 667 ms; staggered siblings start 35 ms apart, at most 333 ms.
    //   Pane (15 in, 17 out): translate from, or to, its Edge by the element's size, 550 ms.
    //   Content (24, the incoming content): translate from (HorizontalOffset,
    //     VerticalOffset) over 500 ms; opacity 0 to 1 from 50 ms over 170 ms.
    // Windows plays them on an element's transition target, apart from its own Opacity and
    // RenderTransform: here a temporary translation and an opacity mask (UWP has none). An
    // element with its own RenderTransform only fades.
    internal static class ThemeTransitions
    {
        private const double EntranceMs = 667, PaneMs = 550, ContentMs = 500, ContentFadeStartMs = 50, ContentFadeMs = 170;
        private const double StaggerMs = 35, StaggerCapMs = 333;
        private static readonly KeySpline Curve = Frozen(new KeySpline(0.1, 0.9, 0.2, 1));

        private sealed class Running
        {
            internal TranslateTransform Translate;
            internal SolidColorBrush Mask;
            internal Transform Original;
            internal bool HitTestVisible = true, BlocksHitTest;
            internal Action Done;
            internal int Remaining;
        }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UIElement, Running> running =
            new System.Runtime.CompilerServices.ConditionalWeakTable<UIElement, Running>();
        // Siblings loaded in one layout pass, per stagger owner, until the dispatcher idles.
        private static readonly Dictionary<object, int> batch = new Dictionary<object, int>();
        private static bool batchResetQueued;

        private static KeySpline Frozen(KeySpline spline) { spline.Freeze(); return spline; }

        // Called when a transitions property is set: WPF raises Loaded only on elements
        // with an instance handler, so each owner is hooked where its transitions apply.
        internal static void Attach(DependencyObject owner, DependencyProperty property)
        {
            var element = owner as FrameworkElement;
            if (element == null) return;
            if (property == CompatibilityProperties.TransitionsProperty) Hook(element, OnOwnLoaded);
            else if (property == CompatibilityProperties.ChildrenTransitionsProperty && element is Panel)
            {
                Hook(element, OnPanelLoaded);
                element.LayoutUpdated -= PanelLayoutHandler(element);
                element.LayoutUpdated += PanelLayoutHandler(element);
            }
            else if (property == CompatibilityProperties.ItemContainerTransitionsProperty && element is ItemsControl)
            {
                var items = (ItemsControl)element;
                items.ItemContainerGenerator.StatusChanged -= GeneratorHandler(items);
                items.ItemContainerGenerator.StatusChanged += GeneratorHandler(items);
            }
            else if (property == CompatibilityProperties.ContentTransitionsProperty && (element is ContentPresenter || element is ContentControl))
            {
                var content = element is ContentPresenter ? ContentPresenter.ContentProperty : ContentControl.ContentProperty;
                var descriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(content, element.GetType());
                descriptor.RemoveValueChanged(element, OnContentChanged);
                descriptor.AddValueChanged(element, OnContentChanged);
                Hook(element, OnContentOwnerLoaded);
            }
        }

        // A popup child: PopupPeer hooks it whenever it becomes a popup's child.
        internal static void HookPopupChild(UIElement child)
        {
            var element = child as FrameworkElement;
            if (element != null) Hook(element, OnPopupChildLoaded);
        }

        private static void Hook(FrameworkElement element, RoutedEventHandler handler)
        {
            element.Loaded -= handler;
            element.Loaded += handler;
        }

        private static void Guard(Action action)
        {
            try { action(); }
            catch (Exception error) { Errors.Log(error); }
        }

        private static void OnOwnLoaded(object sender, RoutedEventArgs e)
        {
            var element = (FrameworkElement)sender;
            Guard(() => Play(element, CompatibilityProperties.GetTransitions(element), null, false));
        }

        private static void OnPopupChildLoaded(object sender, RoutedEventArgs e)
        {
            var element = (FrameworkElement)sender;
            Guard(() => LoadPopupChild(element));
        }

        // The Load trigger for an open popup's child.
        internal static void LoadPopupChild(FrameworkElement element)
        {
            var popup = PopupPeer.ParentOf(element);
            if (popup != null && popup.IsOpen) Play(element, CompatibilityProperties.GetChildTransitions(popup), null, false);
        }

        // Children present when the panel loads enter together, staggered.
        private static void OnPanelLoaded(object sender, RoutedEventArgs e)
        {
            var panel = (Panel)sender;
            Guard(() =>
            {
                var known = Known(panel);
                known.Clear();
                foreach (UIElement child in panel.Children)
                {
                    known.Add(child);
                    var element = child as FrameworkElement;
                    if (element != null) Play(element, CompatibilityProperties.GetChildrenTransitions(panel), panel, false);
                }
            });
        }

        // Children added to a loaded panel enter after their first layout.
        private static void OnPanelLayout(Panel panel)
        {
            if (!panel.IsLoaded) return;
            var known = Known(panel);
            if (known.Count == panel.Children.Count && AllKnown(panel, known)) return;
            Guard(() =>
            {
                var current = new HashSet<UIElement>();
                foreach (UIElement child in panel.Children)
                {
                    current.Add(child);
                    var element = child as FrameworkElement;
                    if (!known.Contains(child) && element != null) Play(element, CompatibilityProperties.GetChildrenTransitions(panel), panel, false);
                }
                known.Clear();
                known.UnionWith(current);
            });
        }

        private static bool AllKnown(Panel panel, HashSet<UIElement> known)
        {
            foreach (UIElement child in panel.Children) if (!known.Contains(child)) return false;
            return true;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Panel, HashSet<UIElement>> knownChildren =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Panel, HashSet<UIElement>>();
        private static HashSet<UIElement> Known(Panel panel) { return knownChildren.GetValue(panel, p => new HashSet<UIElement>()); }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement, EventHandler> layoutHandlers =
            new System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement, EventHandler>();
        private static EventHandler PanelLayoutHandler(FrameworkElement panel)
        {
            var weak = new WeakReference(panel);
            return layoutHandlers.GetValue(panel, p => (s, e) => { var target = weak.Target as Panel; if (target != null) OnPanelLayout(target); });
        }

        // Item containers enter as the generator produces them.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ItemsControl, EventHandler> generatorHandlers =
            new System.Runtime.CompilerServices.ConditionalWeakTable<ItemsControl, EventHandler>();
        private static EventHandler GeneratorHandler(ItemsControl items)
        {
            var weak = new WeakReference(items);
            return generatorHandlers.GetValue(items, i => (s, e) =>
            {
                var target = weak.Target as ItemsControl;
                if (target == null || target.ItemContainerGenerator.Status != System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated) return;
                Guard(() =>
                {
                    foreach (var item in target.Items)
                    {
                        var container = target.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
                        object seen;
                        if (container == null || entered.TryGetValue(container, out seen)) continue;
                        entered.Add(container, target);
                        if (container.IsLoaded) Play(container, CompatibilityProperties.GetItemContainerTransitions(target), target, false);
                        else Hook(container, OnContainerLoaded);
                    }
                });
            });
        }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement, object> entered =
            new System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement, object>();

        private static void OnContainerLoaded(object sender, RoutedEventArgs e)
        {
            var container = (FrameworkElement)sender;
            container.Loaded -= OnContainerLoaded;
            var items = ItemsControl.ItemsControlFromItemContainer(container);
            if (items != null) Guard(() => Play(container, CompatibilityProperties.GetItemContainerTransitions(items), items, false));
        }

        // Content transitions play for new content, and for the content when its host loads.
        private static void OnContentChanged(object sender, EventArgs e)
        {
            var owner = sender as FrameworkElement;
            if (owner != null && owner.IsLoaded) Guard(() => PlayContent(owner));
        }

        private static void OnContentOwnerLoaded(object sender, RoutedEventArgs e)
        {
            var owner = (FrameworkElement)sender;
            Guard(() => PlayContent(owner));
        }

        private static void PlayContent(FrameworkElement owner)
        {
            var transitions = CompatibilityProperties.GetContentTransitions(owner);
            var presenter = owner as ContentPresenter;
            var content = (presenter != null ? presenter.Content : ((ContentControl)owner).Content) as FrameworkElement;
            if (content == null) return;
            if (content.IsLoaded) Play(content, transitions, null, true);
            else
            {
                RoutedEventHandler once = null;
                once = (s, e) => { content.Loaded -= once; Guard(() => Play(content, transitions, null, true)); };
                content.Loaded += once;
            }
        }

        private static bool Play(FrameworkElement element, TransitionCollectionPeer transitions, object staggerOwner, bool isContent)
        {
            if (transitions == null) return false;
            foreach (var transition in transitions)
            {
                var entrance = transition as EntranceThemeTransitionPeer;
                if (entrance != null)
                {
                    double delay = entrance.IsStaggeringEnabled && staggerOwner != null ? Stagger(staggerOwner) : 0;
                    Begin(element, new Point(entrance.FromHorizontalOffset, entrance.FromVerticalOffset), new Point(), EntranceMs, delay, 0, EntranceMs, null, false);
                    return true;
                }
                var pane = transition as PaneThemeTransitionPeer;
                if (pane != null)
                {
                    Begin(element, EdgeOffset(element, pane.Edge), new Point(), PaneMs, 0, 0, 0, null, false);
                    return true;
                }
                var content = transition as ContentThemeTransitionPeer;
                if (content != null && isContent)
                {
                    Begin(element, new Point(content.HorizontalOffset, content.VerticalOffset), new Point(), ContentMs, 0, ContentFadeStartMs, ContentFadeMs, null, false);
                    return true;
                }
            }
            return false;
        }

        // The Unload trigger for an open popup's child: a pane slides back out to its
        // edge, then `remove` takes it out of the tree. False when nothing plays.
        internal static bool Unload(PopupPeer popup, UIElement child, Action remove)
        {
            var element = child as FrameworkElement;
            var transitions = CompatibilityProperties.GetChildTransitions(popup);
            if (element == null || transitions == null) return false;
            foreach (var transition in transitions)
            {
                var pane = transition as PaneThemeTransitionPeer;
                if (pane == null) continue;
                Stop(element);
                if (!Replaceable(element.RenderTransform)) return false;
                Begin(element, new Point(), EdgeOffset(element, pane.Edge), PaneMs, 0, 0, 0, remove, true);
                return true;
            }
            return false;
        }

        // Ends a running transition without its completion work, restoring the element.
        internal static void Stop(UIElement element)
        {
            Running run;
            if (!running.TryGetValue(element, out run)) return;
            running.Remove(element);
            Restore(element, run);
        }

        private static void Begin(FrameworkElement element, Point from, Point to, double translateMs, double delayMs,
                                  double fadeStartMs, double fadeMs, Action done, bool blockHitTest)
        {
            Stop(element);
            var run = new Running { Done = done };
            var animations = new List<KeyValuePair<Animatable, KeyValuePair<DependencyProperty, AnimationTimeline>>>();
            bool moves = from != to && Replaceable(element.RenderTransform);
            if (moves)
            {
                run.Original = element.RenderTransform;
                run.Translate = new TranslateTransform(from.X, from.Y);
                element.RenderTransform = run.Translate;
                if (from.X != to.X) animations.Add(Pair(run.Translate, TranslateTransform.XProperty, Animation(from.X, to.X, delayMs, translateMs)));
                if (from.Y != to.Y) animations.Add(Pair(run.Translate, TranslateTransform.YProperty, Animation(from.Y, to.Y, delayMs, translateMs)));
            }
            if (fadeMs > 0 && element.OpacityMask == null)
            {
                run.Mask = new SolidColorBrush(Colors.Black) { Opacity = 0 };
                element.OpacityMask = run.Mask;
                animations.Add(Pair(run.Mask, Brush.OpacityProperty, Animation(0, 1, delayMs + fadeStartMs, fadeMs)));
            }
            if (blockHitTest)
            {
                run.BlocksHitTest = true;
                run.HitTestVisible = element.IsHitTestVisible;
                element.IsHitTestVisible = false;
            }
            if (animations.Count == 0)
            {
                Restore(element, run);
                if (done != null) done();
                return;
            }
            running.Add(element, run);
            run.Remaining = animations.Count;
            foreach (var animation in animations)
            {
                animation.Value.Value.Completed += (sender, args) =>
                {
                    Running current;
                    if (!running.TryGetValue(element, out current) || !ReferenceEquals(current, run) || --run.Remaining > 0) return;
                    running.Remove(element);
                    Restore(element, run);
                    if (run.Done != null) Host.Callback(run.Done);
                };
                animation.Key.BeginAnimation(animation.Value.Key, animation.Value.Value);
            }
        }

        private static void Restore(UIElement element, Running run)
        {
            if (run.Translate != null)
            {
                run.Translate.BeginAnimation(TranslateTransform.XProperty, null);
                run.Translate.BeginAnimation(TranslateTransform.YProperty, null);
                if (ReferenceEquals(element.RenderTransform, run.Translate)) element.RenderTransform = run.Original;
            }
            if (run.Mask != null)
            {
                run.Mask.BeginAnimation(Brush.OpacityProperty, null);
                if (ReferenceEquals(element.OpacityMask, run.Mask)) element.OpacityMask = null;
            }
            if (run.BlocksHitTest) element.IsHitTestVisible = run.HitTestVisible;
        }

        private static KeyValuePair<Animatable, KeyValuePair<DependencyProperty, AnimationTimeline>> Pair(Animatable target, DependencyProperty property, AnimationTimeline animation)
        {
            return new KeyValuePair<Animatable, KeyValuePair<DependencyProperty, AnimationTimeline>>(target, new KeyValuePair<DependencyProperty, AnimationTimeline>(property, animation));
        }

        // Holds `from` until the start, then follows the theme curve to `to`.
        private static AnimationTimeline Animation(double from, double to, double startMs, double durationMs)
        {
            var animation = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd };
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            if (startMs > 0) animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(startMs))));
            animation.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(startMs + durationMs)), Curve));
            return animation;
        }

        // Only an element without its own RenderTransform is moved: replacing a game's
        // transform object would detach its bindings and animations for the duration.
        private static bool Replaceable(Transform transform)
        {
            return transform == null || ReferenceEquals(transform, Transform.Identity);
        }

        private static Point EdgeOffset(FrameworkElement element, global::Windows.UI.Xaml.Controls.Primitives.EdgeTransitionLocation edge)
        {
            double width = element.ActualWidth > 0 ? element.ActualWidth : element.RenderSize.Width;
            double height = element.ActualHeight > 0 ? element.ActualHeight : element.RenderSize.Height;
            switch (edge)
            {
                case global::Windows.UI.Xaml.Controls.Primitives.EdgeTransitionLocation.Left: return new Point(-width, 0);
                case global::Windows.UI.Xaml.Controls.Primitives.EdgeTransitionLocation.Top: return new Point(0, -height);
                case global::Windows.UI.Xaml.Controls.Primitives.EdgeTransitionLocation.Right: return new Point(width, 0);
                default: return new Point(0, height);
            }
        }

        private static double Stagger(object owner)
        {
            int index;
            batch.TryGetValue(owner, out index);
            batch[owner] = index + 1;
            if (!batchResetQueued)
            {
                batchResetQueued = true;
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { batch.Clear(); batchResetQueued = false; }));
            }
            return Math.Min(index * StaggerMs, StaggerCapMs);
        }
    }
}

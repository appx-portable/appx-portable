using System;
using System.Runtime.InteropServices;
using AppxPortable.WinRT;

namespace Windows.UI.Xaml.Controls
{
    public partial class Control
#if APPX_PORTABLE_INTEROP
        : AppxPortable.WinRT.Interop.INativeObject
#endif
    {
        private NativeObject nativeControl;

        internal static void RefreshKeyTarget()
        {
            var root = AppxPortable.WinRT.Xaml.Host.Content as Control;
            AppxPortable.WinRT.Xaml.NativeServices.SetKeyTarget(root == null ? null : root.nativeControl);
        }

#if APPX_PORTABLE_INTEROP
        SafeHandle AppxPortable.WinRT.Interop.INativeObject.AcquireInterface(Guid iid)
        {
            CheckThread();
            if (nativeControl == null)
            {
                if (!(this is Page)) throw Errors.Unimplemented("Native IControl for " + GetType().FullName);
                nativeControl = AppxPortable.WinRT.Xaml.NativeServices.Create("Windows.UI.Xaml.Controls.Page");
                NativeObject.Remember(this, nativeControl);
                RefreshKeyTarget();
            }
            return nativeControl.QueryInterface(iid);
        }

        internal static Control FromNative(NativeObject value)
        {
            return NativeObject.Wrap<Control>(value, NativeServiceIid, _ =>
            {
                throw Errors.Unimplemented("Control from an unassociated native object");
            });
        }
#endif
    }

    public partial class SwapChainPanel : INativeWrapper
#if APPX_PORTABLE_INTEROP
        , AppxPortable.WinRT.Interop.INativeObject
#endif
    {
        private NativeObject native;

        NativeObject INativeWrapper.Native
        {
            get
            {
                CheckThread();
                if (native == null)
                {
                    native = AppxPortable.WinRT.Xaml.NativeServices.Create("Windows.UI.Xaml.Controls.SwapChainPanel");
                    NativeObject.Remember(this, native);
                }
                return native;
            }
        }

#if APPX_PORTABLE_INTEROP
        SafeHandle AppxPortable.WinRT.Interop.INativeObject.AcquireInterface(Guid iid)
        {
            return NativeObject.Argument(this, iid);
        }

        internal static SwapChainPanel FromNative(NativeObject value)
        {
            return NativeObject.Wrap<SwapChainPanel>(value, NativeServiceIid, _ =>
            {
                throw Errors.Unimplemented("SwapChainPanel from an unassociated native object");
            });
        }
#endif
    }

    // The board's swap chain stays a shim service: the WPF peer holds the panel's
    // place in the tree, and native code that receives the panel (a game's
    // ISwapChainBackgroundPanelNative::SetSwapChain) gets the shim's own panel.
    public partial class SwapChainBackgroundPanel : INativeWrapper
#if APPX_PORTABLE_INTEROP
        , AppxPortable.WinRT.Interop.INativeObject
#endif
    {
        private NativeObject native;

        NativeObject INativeWrapper.Native
        {
            get
            {
                CheckThread();
                return native ?? (native = AppxPortable.WinRT.Xaml.NativeServices.Create("Windows.UI.Xaml.Controls.SwapChainBackgroundPanel"));
            }
        }

#if APPX_PORTABLE_INTEROP
        SafeHandle AppxPortable.WinRT.Interop.INativeObject.AcquireInterface(Guid iid) { return NativeObject.Argument(this, iid); }
#endif
    }
}

namespace AppxPortable.WinRT.Xaml
{
    internal static class NativeServices
    {
        [DllImport("appxportable.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern void ShimSetXamlKeyTarget(IntPtr value);
        private static NativeObject keyTarget;

        internal static void SetKeyTarget(NativeObject target)
        {
            if (ReferenceEquals(keyTarget, target)) return;
            ShimSetXamlKeyTarget(target == null ? IntPtr.Zero : target.DangerousGetHandle());
            keyTarget = target;
        }

        [DllImport("appxportable.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int ShimCreateXamlService(string name, out IntPtr value);

        // A standalone shim object (MediaElement, SwapChainBackgroundPanel) that
        // does the work behind a WPF-backed wrapper.
        internal static NativeObject Create(string className)
        {
            IntPtr value;
            int hr = ShimCreateXamlService(className, out value);
            return NativeObject.TakeResult(hr, value);
        }
    }
}

namespace AppxPortable.WinRT.Xaml
{
    // MediaElement plays through the shim's Media Foundation element; the WPF peer
    // holds its place in the tree and forwards each property, whether markup or
    // code set it, to that native element.
    public sealed class MediaElementPeer : NativeHostPeer
    {
        private static readonly Guid MediaElementIid = new Guid("a38ed2cf-13de-4299-ade2-ae18f74ed353");
        // IMediaElement slots (SDK order).
        private const int PutSource = 9, PutIsMuted = 11, PutAutoPlay = 14, PutVolume = 16, PutIsLooping = 37, PutAudioCategory = 46,
            StopSlot = 76, PlaySlot = 77, PauseSlot = 78;
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetPointer(IntPtr self, IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetFlag(IntPtr self, byte value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetNumber(IntPtr self, double value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetInt(IntPtr self, int value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Action0(IntPtr self);

        public static readonly System.Windows.DependencyProperty SourceProperty = Register("Source", typeof(Uri), null);
        public static readonly System.Windows.DependencyProperty AutoPlayProperty = Register("AutoPlay", typeof(bool), true);
        public static readonly System.Windows.DependencyProperty IsMutedProperty = Register("IsMuted", typeof(bool), false);
        public static readonly System.Windows.DependencyProperty IsLoopingProperty = Register("IsLooping", typeof(bool), false);
        // UWP's default volume is half.
        public static readonly System.Windows.DependencyProperty VolumeProperty = Register("Volume", typeof(double), 0.5);
        public static readonly System.Windows.DependencyProperty AudioCategoryProperty =
            Register("AudioCategory", typeof(global::Windows.UI.Xaml.Media.AudioCategory), global::Windows.UI.Xaml.Media.AudioCategory.Other);

        public Uri Source { get { return (Uri)GetValue(SourceProperty); } set { SetValue(SourceProperty, value); } }
        public bool AutoPlay { get { return (bool)GetValue(AutoPlayProperty); } set { SetValue(AutoPlayProperty, value); } }
        public bool IsMuted { get { return (bool)GetValue(IsMutedProperty); } set { SetValue(IsMutedProperty, value); } }
        public bool IsLooping { get { return (bool)GetValue(IsLoopingProperty); } set { SetValue(IsLoopingProperty, value); } }
        public double Volume { get { return (double)GetValue(VolumeProperty); } set { SetValue(VolumeProperty, value); } }
        public global::Windows.UI.Xaml.Media.AudioCategory AudioCategory
        {
            get { return (global::Windows.UI.Xaml.Media.AudioCategory)GetValue(AudioCategoryProperty); }
            set { SetValue(AudioCategoryProperty, value); }
        }

        private NativeObject native;
        private bool unloaded;
        private int? pendingCommand;

        public MediaElementPeer()
        {
            // Windows shuts down a removed MediaElement's engine, even when
            // the app retains the element. Entering the tree reloads Source.
            Unloaded += (sender, args) => Host.Callback(() =>
            {
                unloaded = true;
                pendingCommand = null;
                if (native != null) Invoke(StopSlot);
            });
            Loaded += (sender, args) => Host.Callback(() =>
            {
                if (!unloaded) return;
                unloaded = false;
                var command = pendingCommand;
                pendingCommand = null;
                if (native == null)
                {
                    if (Source == null && !command.HasValue) return;
                    var created = Native;
                }
                else SendProperties(true);
                // WPF raises Loaded later than Window.Content is assigned;
                // preserve a Play/Pause/Stop issued during that transition.
                if (command.HasValue) Invoke(command.Value);
            });
        }

        private static System.Windows.DependencyProperty Register(string name, Type type, object value)
        {
            return System.Windows.DependencyProperty.Register(name, type, typeof(MediaElementPeer),
                new System.Windows.PropertyMetadata(value, (d, e) => ((MediaElementPeer)d).Forward(e.Property, e.NewValue)));
        }

        // Created with the first value: the native element starts from the SDK
        // defaults, so every non-default value here is sent once.
        private NativeObject Native
        {
            get
            {
                if (native != null) return native;
                using (var instance = NativeServices.Create("Windows.UI.Xaml.Controls.MediaElement"))
                    native = instance.QueryInterface(MediaElementIid);
                SendProperties();
                return native;
            }
        }

        private void SendProperties(bool all = false)
        {
            foreach (var property in new[] { AutoPlayProperty, IsMutedProperty, IsLoopingProperty, VolumeProperty, AudioCategoryProperty, SourceProperty })
                if (all || ReadLocalValue(property) != System.Windows.DependencyProperty.UnsetValue || property == VolumeProperty)
                    Send(property, GetValue(property));
        }

        private void Forward(System.Windows.DependencyProperty property, object value)
        {
            if (unloaded)
            {
                if (property == SourceProperty) pendingCommand = null;
                return;  // replay current properties on reattachment
            }
            if (native == null) { var created = Native; return; }  // creation sends current values
            Send(property, value);
        }

        private void Send(System.Windows.DependencyProperty property, object value)
        {
            using (var call = new NativeObject.Access(native))
            {
                int hr;
                if (property == SourceProperty)
                {
                    using (var uri = value == null ? null : global::AppxPortable.WinRT.Projected.Windows_Foundation_Uri.ToNative((Uri)value))
                        hr = call.Method<SetPointer>(PutSource)(call.Self, uri == null ? IntPtr.Zero : uri.DangerousGetHandle());
                }
                else if (property == VolumeProperty) hr = call.Method<SetNumber>(PutVolume)(call.Self, (double)value);
                else if (property == AudioCategoryProperty) hr = call.Method<SetInt>(PutAudioCategory)(call.Self, (int)value);
                else
                {
                    int slot = property == AutoPlayProperty ? PutAutoPlay : property == IsMutedProperty ? PutIsMuted : PutIsLooping;
                    hr = call.Method<SetFlag>(slot)(call.Self, (bool)value ? (byte)1 : (byte)0);
                }
                Errors.Check(hr);
            }
        }

        private void Invoke(int slot)
        {
            var target = Native;
            using (var call = new NativeObject.Access(target)) Errors.Check(call.Method<Action0>(slot)(call.Self));
        }

        private void Command(int slot)
        {
            if (unloaded) pendingCommand = slot;
            else Invoke(slot);
        }

        internal void Play() { Command(PlaySlot); }
        internal void Pause() { Command(PauseSlot); }
        internal void Stop() { Command(StopSlot); }
    }
}

namespace Windows.UI.Xaml.Controls
{
    public sealed partial class MediaElement
    {
        private AppxPortable.WinRT.Xaml.MediaElementPeer Player { get { return (AppxPortable.WinRT.Xaml.MediaElementPeer)AppxPortable.WinRT.Xaml.Values.Dependency(this); } }
        public void Play() { Player.Play(); }
        public void Pause() { Player.Pause(); }
        public void Stop() { Player.Stop(); }
    }
}

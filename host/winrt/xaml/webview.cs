using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace AppxPortable.WinRT.Xaml
{
    // The native HTML view is a child of the real app window. WPF's off-screen
    // layered source cannot composite browser HWNDs into its bitmap.
    public sealed class WebViewPeer : FrameworkElement
    {
        [DllImport("appxportable.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern IntPtr ShimGetHostWindow();
        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);
        [DllImport("gdi32.dll", ExactSpelling = true)]
        private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
        [DllImport("gdi32.dll", ExactSpelling = true)]
        private static extern int CombineRgn(IntPtr target, IntPtr first, IntPtr second, int mode);
        [DllImport("gdi32.dll", ExactSpelling = true)]
        private static extern bool DeleteObject(IntPtr value);
        public static readonly DependencyProperty DefaultBackgroundColorProperty = DependencyProperty.Register("DefaultBackgroundColor", typeof(Color), typeof(WebViewPeer), new PropertyMetadata(Colors.White));
        public Color DefaultBackgroundColor { get { return (Color)GetValue(DefaultBackgroundColorProperty); } set { SetValue(DefaultBackgroundColorProperty, value); } }
        private Forms.WebBrowser browser;
        private string html;
        private Uri source;
        private Rect lastBounds = Rect.Empty;
        private Rect lastOcclusion = Rect.Empty;
        private bool visible;
        private static readonly bool useUiFontFallback = MissingUiFont();
        private static bool MissingUiFont()
        {
            using (var fonts = new System.Drawing.Text.InstalledFontCollection())
                foreach (var font in fonts.Families)
                    if (font.Name.Equals("Segoe UI", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }
        private static string CssWithFonts(string css)
        {
            // Preserve package font-face definitions and existing fallback lists.
            const string pattern = @"(?<face>@font-face\s*\{[^}]*\})|(?<prefix>\bfont-family\s*:\s*)(?<quote>['""]?)Segoe UI\k<quote>(?=\s*(?:[;}]|$))";
            return System.Text.RegularExpressions.Regex.Replace(css, pattern,
                match => match.Groups["face"].Success ? match.Value : match.Groups["prefix"].Value +
                    "system-ui, -apple-system, BlinkMacSystemFont, Segoe UI, Arial, sans-serif",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        }
        private static string HtmlWithFonts(string text)
        {
            if (!useUiFontFallback || text.IndexOf("Segoe UI", StringComparison.OrdinalIgnoreCase) < 0) return text;
            var flags = System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant;
            text = System.Text.RegularExpressions.Regex.Replace(text,
                @"(?<open><style\b[^>]*>)(?<css>[\s\S]*?)(?<close></style\s*>)",
                match => match.Groups["open"].Value + CssWithFonts(match.Groups["css"].Value) + match.Groups["close"].Value, flags);
            return System.Text.RegularExpressions.Regex.Replace(text,
                @"(?<open>\sstyle\s*=\s*)(?<quote>['""])(?<css>.*?)\k<quote>",
                match => match.Groups["open"].Value + match.Groups["quote"].Value + CssWithFonts(match.Groups["css"].Value) + match.Groups["quote"].Value, flags);
        }
        internal Forms.WebBrowser Browser { get { return browser; } }
        internal event Action<string> ScriptNotify;
        public WebViewPeer()
        {
            Loaded += (s, e) => { CreateBrowser(); Position(); };
            Unloaded += (s, e) => ReleaseBrowser();
            LayoutUpdated += (s, e) => Position();
            IsVisibleChanged += (s, e) => Position();
        }
        private void CreateBrowser()
        {
            if (browser != null || !IsLoaded) return;
            IntPtr parent = ShimGetHostWindow();
            if (parent == IntPtr.Zero) throw new InvalidOperationException("WebView needs the app's host window");
            browser = new Forms.WebBrowser { ScriptErrorsSuppressed = true, ObjectForScripting = new ScriptBridge(this) };
            SetParent(browser.Handle, parent);
            browser.DocumentCompleted += (s, e) =>
            {
                if (browser == null || browser.Document == null) return;
                var color = DefaultBackgroundColor;
                browser.Document.BackColor = System.Drawing.Color.FromArgb(color.R, color.G, color.B);
                // Native HTML takes clicks before the composited overlay can.
                browser.Document.MouseDown += (sender, args) => SettingsFlyoutPeer.DismissOutside(this);
            };
            if (html != null) browser.DocumentText = html;
            else if (source != null) browser.Navigate(Resolve(source));
        }
        private static Uri Resolve(Uri uri)
        {
            if (uri.Scheme != "ms-appx") return uri;
            var local = new UriBuilder(new Uri(XamlLoader.Resolve(uri), UriKind.Absolute));
            local.Query = uri.Query.TrimStart('?');
            local.Fragment = uri.Fragment.TrimStart('#');
            return local.Uri;
        }
        internal void NavigateToString(string value)
        {
            VerifyAccess();
            if (value == null) throw new ArgumentNullException("text");
            var previous = browser;
            html = HtmlWithFonts(value); source = null;
            CreateBrowser();
            if (browser != null && ReferenceEquals(previous, browser)) browser.DocumentText = html;
        }
        internal void Navigate(Uri value)
        {
            VerifyAccess();
            if (value == null) throw new ArgumentNullException("source");
            var previous = browser;
            source = value; html = null;
            CreateBrowser();
            if (browser != null && ReferenceEquals(previous, browser)) browser.Navigate(Resolve(value));
        }
        private void Position()
        {
            if (browser == null) return;
            bool show = IsLoaded && IsVisible && ActualWidth > 0 && ActualHeight > 0;
            Rect bounds = Rect.Empty;
            Rect occlusion = Rect.Empty;
            if (show)
            {
                var point = TranslatePoint(new Point(0, 0), Host.Root);
                var matrix = PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice;
                point = matrix.Transform(point);
                var size = matrix.Transform(new Vector(ActualWidth, ActualHeight));
                bounds = new Rect(point.X, point.Y, Math.Max(0, size.X), Math.Max(0, size.Y));
                occlusion = SettingsFlyoutPeer.Occlusion(this);
                if (!occlusion.IsEmpty) { occlusion.Transform(matrix); occlusion.Intersect(bounds); }
            }
            if (bounds == lastBounds && occlusion == lastOcclusion && show == visible) return;
            lastBounds = bounds; lastOcclusion = occlusion; visible = show;
            // ActiveX sizes its document from the control's managed Bounds.
            // Resizing only the HWND leaves the HTML viewport at its default size.
            if (show)
                browser.Bounds = new System.Drawing.Rectangle((int)Math.Round(bounds.X), (int)Math.Round(bounds.Y),
                    Math.Max(1, (int)Math.Round(bounds.Width)), Math.Max(1, (int)Math.Round(bounds.Height)));
            SetWindowPos(browser.Handle, IntPtr.Zero, show ? (int)Math.Round(bounds.X) : 0, show ? (int)Math.Round(bounds.Y) : 0,
                show ? Math.Max(1, (int)Math.Round(bounds.Width)) : 0, show ? Math.Max(1, (int)Math.Round(bounds.Height)) : 0,
                0x0004 | 0x0010 | (show ? 0x0040U : 0x0080U)); // NOZORDER | NOACTIVATE | SHOW/HIDE
            ClipForSettings(bounds, occlusion);
        }
        private void ClipForSettings(Rect bounds, Rect occlusion)
        {
            if (occlusion.IsEmpty) { SetWindowRgn(browser.Handle, IntPtr.Zero, true); return; }
            var region = CreateRectRgn(0, 0, (int)Math.Round(bounds.Width), (int)Math.Round(bounds.Height));
            var cutout = CreateRectRgn((int)Math.Floor(occlusion.Left - bounds.Left), (int)Math.Floor(occlusion.Top - bounds.Top),
                (int)Math.Ceiling(occlusion.Right - bounds.Left), (int)Math.Ceiling(occlusion.Bottom - bounds.Top));
            try
            {
                if (region == IntPtr.Zero || cutout == IntPtr.Zero || CombineRgn(region, region, cutout, 4) == 0 || SetWindowRgn(browser.Handle, region, true) == 0)
                    throw new InvalidOperationException("Cannot clip HTML behind SettingsFlyout");
                region = IntPtr.Zero; // SetWindowRgn owns the region after success.
            }
            finally
            {
                if (region != IntPtr.Zero) DeleteObject(region);
                if (cutout != IntPtr.Zero) DeleteObject(cutout);
            }
        }
        private void ReleaseBrowser()
        {
            var current = browser; browser = null;
            if (current != null) { current.ObjectForScripting = null; current.Dispose(); }
            lastBounds = Rect.Empty; lastOcclusion = Rect.Empty; visible = false;
        }
        [ComVisible(true), ClassInterface(ClassInterfaceType.AutoDispatch)]
        public sealed class ScriptBridge
        {
            private readonly WebViewPeer owner;
            internal ScriptBridge(WebViewPeer owner) { this.owner = owner; }
            public void notify(string value) { if (owner.ScriptNotify != null) Host.Callback(() => owner.ScriptNotify(value)); }
        }
    }
}

namespace Windows.UI.Xaml.Controls
{
    public sealed partial class NotifyEventArgs
    {
        private readonly string value;
        internal NotifyEventArgs(string value) { this.value = value; }
        public string Value { get { return value; } }
    }
    public sealed partial class WebView
    {
        private Dictionary<long, Delegate> notifications;
        private AppxPortable.WinRT.Xaml.WebViewPeer BrowserPeer { get { return (AppxPortable.WinRT.Xaml.WebViewPeer)AppxPortable.WinRT.Xaml.Values.Dependency(this); } }
        public void NavigateToString(string text) { BrowserPeer.NavigateToString(text); }
        public void Navigate(Uri source) { BrowserPeer.Navigate(source); }
        public EventRegistrationToken add_ScriptNotify(NotifyEventHandler handler)
        {
            CheckThread(); if (handler == null) throw new ArgumentNullException("handler");
            Action<string> callback = text => handler(this, new NotifyEventArgs(text));
            BrowserPeer.ScriptNotify += callback;
            return AppxPortable.WinRT.Xaml.Tokens.Add(ref notifications, callback);
        }
        public void remove_ScriptNotify(EventRegistrationToken token)
        {
            CheckThread(); var callback = AppxPortable.WinRT.Xaml.Tokens.Remove(notifications, token) as Action<string>;
            if (callback != null) BrowserPeer.ScriptNotify -= callback;
        }
    }
}

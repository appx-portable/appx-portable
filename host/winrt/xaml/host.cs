using System.Windows.Threading;

namespace AppxPortable.WinRT.Xaml
{
    // WPF backs Windows.UI.Xaml for managed games under Wine. Its objects live on the
    // shim's view thread, whose message loop also pumps WPF's dispatcher.
    internal static partial class Host
    {
        private static Dispatcher dispatcher;

        internal static Dispatcher Dispatcher { get { EnsureDispatcher(); return dispatcher; } }

        // Binds WPF to the calling (view) thread on first use; the shim's loop pumps it.
        internal static void EnsureDispatcher()
        {
            if (dispatcher == null) dispatcher = Dispatcher.CurrentDispatcher;
        }

        private static System.Windows.ResourceDictionary resources;

        internal static string RequestedTheme = "Dark";
        internal static void SetTheme(string value)
        {
            if (value != "Dark" && value != "Light") throw new System.ArgumentException("Unknown application theme: " + value);
            if (value == RequestedTheme) return;
            RequestedTheme = value;
            if (resources != null) resources.MergedDictionaries[0] = CreateTheme();
        }
        private static System.Windows.ResourceDictionary CreateTheme()
        {
            var document = new System.Xml.XmlDocument { XmlResolver = null };
            using (var stream = typeof(Host).Assembly.GetManifestResourceStream("AppxPortable.WinRT.Xaml.theme.xaml")) document.Load(stream);
            // Light variant of the runtime's own palette; retain accents,
            // image overlays, focus strokes and tooltips.
            if (RequestedTheme == "Light")
                foreach (System.Xml.XmlNode child in document.DocumentElement.ChildNodes)
                {
                    var brush = child as System.Xml.XmlElement;
                    if (brush == null || brush.LocalName != "SolidColorBrush") continue;
                    string key = brush.GetAttribute("Key", "http://schemas.microsoft.com/winfx/2006/xaml");
                    if (key.StartsWith("FocusVisual", System.StringComparison.Ordinal) || key.Contains("Overlay") || key == "ToolTipBackgroundThemeBrush" || key == "ToggleButtonCheckedForegroundThemeBrush") continue;
                    string color = brush.GetAttribute("Color");
                    switch (color)
                    {
                        case "#FF1D1D1D": color = "#FFFFFFFF"; break;
                        case "#FF3D3D3D": color = "#FFF0F0F0"; break;
                        case "#FFFFFFFF": color = "#FF000000"; break;
                        case "#FF000000": color = "#FFFFFFFF"; break;
                        case "#DEFFFFFF": color = "#DE000000"; break;
                        case "#99FFFFFF": color = "#99000000"; break;
                        case "#66FFFFFF": color = "#66000000"; break;
                        case "#21FFFFFF": color = "#21000000"; break;
                        case "#59FFFFFF": color = "#59000000"; break;
                    }
                    brush.SetAttribute("Color", color);
                }
            System.Windows.ResourceDictionary dictionary;
            using (var reader = new System.Xml.XmlNodeReader(document)) dictionary = (System.Windows.ResourceDictionary)System.Windows.Markup.XamlReader.Load(reader);
            // Implicit styles match an element's exact type: UWP-created viewers share the theme's.
            var viewer = (System.Windows.Style)dictionary[typeof(System.Windows.Controls.ScrollViewer)];
            dictionary[typeof(ScrollViewerPeer)] = new System.Windows.Style(typeof(ScrollViewerPeer), viewer);
            // WPF's CheckBox defaults to black text. UWP's dark-theme
            // ToggleSwitch header uses the application foreground brush.
            var toggle = new System.Windows.Style(typeof(ToggleSwitchPeer));
            toggle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Control.ForegroundProperty,
                dictionary["ApplicationForegroundThemeBrush"]));
            dictionary[typeof(ToggleSwitchPeer)] = toggle;
            return dictionary;
        }

        // WPF finds application resources through System.Windows.Application: its
        // dictionary merges the theme, then the app's Application.Resources.
        internal static void EnsureTheme()
        {
            if (resources != null) return;
            EnsureDispatcher();
            var application = System.Windows.Application.Current ??
                new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
            resources = new System.Windows.ResourceDictionary();
            resources.MergedDictionaries.Add(CreateTheme());
            application.Resources = resources;
        }

        internal static void SetResources(global::Windows.UI.Xaml.ResourceDictionary value)
        {
            EnsureTheme();
            while (resources.MergedDictionaries.Count > 1) resources.MergedDictionaries.RemoveAt(1);
            if (value != null) resources.MergedDictionaries.Add((System.Windows.ResourceDictionary)Peers.Of(value));
        }

        // Game code called from WPF: a failure is logged as the shim logs native
        // callbacks', then passed on to WPF's caller.
        internal static void Callback(System.Action call)
        {
            try { call(); }
            catch (System.Exception error)
            {
                Errors.Log(error);
                throw;
            }
        }

        // The window's content and the WPF tree the compositor draws.
        internal static global::Windows.UI.Xaml.UIElement Content { get; private set; }
        internal static System.Windows.UIElement Root { get { return Layers; } }

        internal static void SetContent(global::Windows.UI.Xaml.UIElement content)
        {
            Content = content;
            global::Windows.UI.Xaml.Controls.Control.RefreshKeyTarget();
            ShowContent((System.Windows.UIElement)Peers.Of(content));
            EnsurePresentation();
        }
    }
}

namespace Windows.UI.Xaml
{
    // The native Application keeps its bootstrap and lifetime; its resources are WPF.
    public partial class Application
    {
        private ResourceDictionary resources;
        private bool initialized;

        public ResourceDictionary Resources
        {
            get
            {
                Initialize();
                if (resources == null) AppxPortable.WinRT.Xaml.Host.SetResources(resources = new ResourceDictionary());
                return resources;
            }
            set { initialized = true; AppxPortable.WinRT.Xaml.Host.SetResources(resources = value); }
        }

        // Windows loads the package's App.xaml for the app (Windows 8 apps'
        // InitializeComponent does not), unless the app loaded its own source first.
        internal void Initialize()
        {
            if (initialized) return;
            initialized = true;
            var uri = new global::System.Uri("ms-appx:///App.xaml");
            if (AppxPortable.WinRT.Xaml.XamlLoader.HasMarkup(uri))
                LoadComponent(this, uri);
        }

        internal void Loaded() { initialized = true; }
    }

    // The native Window keeps bounds, activation and CoreWindow; its content is WPF.
    public sealed partial class Window
    {
        public UIElement Content
        {
            get { return AppxPortable.WinRT.Xaml.Host.Content; }
            set { AppxPortable.WinRT.Xaml.Host.SetContent(value); }
        }
    }
}

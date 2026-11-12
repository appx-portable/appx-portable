// EdgeHTML requires CoreWindow, which XAML Islands cannot supply. A WebView
// outer composes a system Grid and renders WebView2 beside the island HWND.
#include "com/factory.h"
#include "islands.h"
#include "webview.h"
#include "host/host.h"
#include "registry.h"
#include "package/package.h"
#include <WebView2.h>
#include <windows.data.json.h>
#include <windows.ui.xaml.media.h>
#include <windows.ui.xaml.controls.primitives.h>
#include <windows.storage.h>
#include <wrl/event.h>
#include <memory>
#include <functional>
#include <map>
#include <cmath>
using namespace Microsoft::WRL;
using namespace Microsoft::WRL::Wrappers;
namespace Xaml = ABI::Windows::UI::Xaml;
namespace Controls = Xaml::Controls;
namespace Foundation = ABI::Windows::Foundation;

namespace appx {
namespace {
std::wstring Text(HSTRING value) {
    UINT32 length;
    const auto* text = WindowsGetStringRawBuffer(value, &length);
    return {text, length};
}
HRESULT String(const std::wstring& text, HSTRING* value) {
    return value ? WindowsCreateString(text.data(), static_cast<UINT32>(text.size()), value) : E_POINTER;
}
HRESULT Uri(const wchar_t* text, Foundation::IUriRuntimeClass** value) {
    std::wstring address(text);
    if (address.starts_with(L"https://appx.invalid/"))
        address.replace(0, wcslen(L"https://appx.invalid/"), L"ms-appx-web:///");
    ComPtr<Foundation::IUriRuntimeClassFactory> factory;
    auto hr = OsGetActivationFactory(HStringReference(L"Windows.Foundation.Uri").Get(), IID_PPV_ARGS(&factory));
    return SUCCEEDED(hr) ? factory->CreateUri(HStringReference(address.c_str()).Get(), value) : hr;
}
std::wstring MapPackageUris(std::wstring text) {
    for (const auto* prefix : {L"ms-appx-web:///", L"ms-appx:///"}) {
        size_t position = 0;
        while ((position = text.find(prefix, position)) != std::wstring::npos) {
            text.replace(position, wcslen(prefix), L"https://appx.invalid/");
            position += wcslen(L"https://appx.invalid/");
        }
    }
    return text;
}
HRESULT ProfileFolder(std::filesystem::path& path) {
    ComPtr<IActivationFactory> factory;
    ComPtr<ABI::Windows::Storage::IApplicationDataStatics> statics;
    ComPtr<ABI::Windows::Storage::IApplicationData> data;
    ComPtr<ABI::Windows::Storage::IStorageFolder> folder;
    ComPtr<ABI::Windows::Storage::IStorageItem> item;
    HString name;
    auto hr = MakeStorageFactory(&factory);
    if (SUCCEEDED(hr)) hr = factory.As(&statics);
    if (SUCCEEDED(hr)) hr = statics->get_Current(&data);
    if (SUCCEEDED(hr)) hr = data->get_LocalFolder(&folder);
    if (SUCCEEDED(hr)) hr = folder.As(&item);
    if (SUCCEEDED(hr)) hr = item->get_Path(name.GetAddressOf());
    if (SUCCEEDED(hr)) path = std::filesystem::path(Text(name.Get())) / L"WebView2";
    return hr;
}
class LoadArgs final : public RuntimeClass<RuntimeClassFlags<WinRt>, Xaml::Navigation::INavigationEventArgs> {
    InspectableClass(L"Windows.UI.Xaml.Navigation.NavigationEventArgs", BaseTrust);
    ComPtr<Foundation::IUriRuntimeClass> uri_;

public:
    explicit LoadArgs(Foundation::IUriRuntimeClass* uri) : uri_(uri) {}
    HRESULT STDMETHODCALLTYPE get_Content(IInspectable** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_Parameter(IInspectable** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_SourcePageType(Xaml::Interop::TypeName* value) override {
        if (!value) return E_POINTER;
        *value = {};
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_NavigationMode(Xaml::Navigation::NavigationMode* value) override {
        if (!value) return E_POINTER;
        *value = Xaml::Navigation::NavigationMode_New;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_Uri(Foundation::IUriRuntimeClass** value) override { return uri_.CopyTo(value); }
    HRESULT STDMETHODCALLTYPE put_Uri(Foundation::IUriRuntimeClass* value) override {
        uri_ = value;
        return S_OK;
    }
};
ABI::Windows::Web::WebErrorStatus WebStatus(COREWEBVIEW2_WEB_ERROR_STATUS status) {
    using namespace ABI::Windows::Web;
    switch (status) {
    case COREWEBVIEW2_WEB_ERROR_STATUS_CERTIFICATE_COMMON_NAME_IS_INCORRECT:
        return WebErrorStatus_CertificateCommonNameIsIncorrect;
    case COREWEBVIEW2_WEB_ERROR_STATUS_CERTIFICATE_EXPIRED:
        return WebErrorStatus_CertificateExpired;
    case COREWEBVIEW2_WEB_ERROR_STATUS_CERTIFICATE_REVOKED:
        return WebErrorStatus_CertificateRevoked;
    case COREWEBVIEW2_WEB_ERROR_STATUS_CERTIFICATE_IS_INVALID:
        return WebErrorStatus_CertificateIsInvalid;
    case COREWEBVIEW2_WEB_ERROR_STATUS_SERVER_UNREACHABLE:
        return WebErrorStatus_ServerUnreachable;
    case COREWEBVIEW2_WEB_ERROR_STATUS_TIMEOUT:
        return WebErrorStatus_Timeout;
    case COREWEBVIEW2_WEB_ERROR_STATUS_CONNECTION_ABORTED:
        return WebErrorStatus_ConnectionAborted;
    case COREWEBVIEW2_WEB_ERROR_STATUS_CONNECTION_RESET:
        return WebErrorStatus_ConnectionReset;
    case COREWEBVIEW2_WEB_ERROR_STATUS_DISCONNECTED:
        return WebErrorStatus_Disconnected;
    case COREWEBVIEW2_WEB_ERROR_STATUS_CANNOT_CONNECT:
        return WebErrorStatus_CannotConnect;
    case COREWEBVIEW2_WEB_ERROR_STATUS_HOST_NAME_NOT_RESOLVED:
        return WebErrorStatus_HostNameNotResolved;
    case COREWEBVIEW2_WEB_ERROR_STATUS_OPERATION_CANCELED:
        return WebErrorStatus_OperationCanceled;
    default:
        return WebErrorStatus_Unknown;
    }
}
class FailureArgs final : public RuntimeClass<RuntimeClassFlags<WinRt>, Controls::IWebViewNavigationFailedEventArgs> {
    InspectableClass(L"Windows.UI.Xaml.Controls.WebViewNavigationFailedEventArgs", BaseTrust);
    ComPtr<Foundation::IUriRuntimeClass> uri_;
    ABI::Windows::Web::WebErrorStatus status_;

public:
    FailureArgs(Foundation::IUriRuntimeClass* uri, COREWEBVIEW2_WEB_ERROR_STATUS status)
        : uri_(uri), status_(WebStatus(status)) {}
    HRESULT STDMETHODCALLTYPE get_Uri(Foundation::IUriRuntimeClass** value) override { return uri_.CopyTo(value); }
    HRESULT STDMETHODCALLTYPE get_WebErrorStatus(ABI::Windows::Web::WebErrorStatus* value) override {
        if (!value) return E_POINTER;
        *value = status_;
        return S_OK;
    }
};
// ---- Windows 8.1 (IWebView2) event arguments ------------------------------------
// Their Uri is null for NavigateToString content, as on Windows.
HRESULT EventUri(const std::wstring& address, Foundation::IUriRuntimeClass** value) {
    *value = nullptr;
    if (address.empty() || address.starts_with(L"data:") || address.starts_with(L"about:blank")) return S_OK;
    return Uri(address.c_str(), value);
}
class StartingArgs final
    : public RuntimeClass<RuntimeClassFlags<WinRt>, Controls::IWebViewNavigationStartingEventArgs> {
    InspectableClass(L"Windows.UI.Xaml.Controls.WebViewNavigationStartingEventArgs", BaseTrust);
    ComPtr<Foundation::IUriRuntimeClass> uri_;

public:
    boolean cancel = false;
    explicit StartingArgs(Foundation::IUriRuntimeClass* uri) : uri_(uri) {}
    HRESULT STDMETHODCALLTYPE get_Uri(Foundation::IUriRuntimeClass** value) override { return uri_.CopyTo(value); }
    HRESULT STDMETHODCALLTYPE get_Cancel(boolean* value) override {
        if (!value) return E_POINTER;
        *value = cancel;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE put_Cancel(boolean value) override {
        cancel = value;
        return S_OK;
    }
};
class ContentLoadingArgs final
    : public RuntimeClass<RuntimeClassFlags<WinRt>, Controls::IWebViewContentLoadingEventArgs> {
    InspectableClass(L"Windows.UI.Xaml.Controls.WebViewContentLoadingEventArgs", BaseTrust);
    ComPtr<Foundation::IUriRuntimeClass> uri_;

public:
    explicit ContentLoadingArgs(Foundation::IUriRuntimeClass* uri) : uri_(uri) {}
    HRESULT STDMETHODCALLTYPE get_Uri(Foundation::IUriRuntimeClass** value) override { return uri_.CopyTo(value); }
};
class DomLoadedArgs final
    : public RuntimeClass<RuntimeClassFlags<WinRt>, Controls::IWebViewDOMContentLoadedEventArgs> {
    InspectableClass(L"Windows.UI.Xaml.Controls.WebViewDOMContentLoadedEventArgs", BaseTrust);
    ComPtr<Foundation::IUriRuntimeClass> uri_;

public:
    explicit DomLoadedArgs(Foundation::IUriRuntimeClass* uri) : uri_(uri) {}
    HRESULT STDMETHODCALLTYPE get_Uri(Foundation::IUriRuntimeClass** value) override { return uri_.CopyTo(value); }
};
class CompletedArgs final
    : public RuntimeClass<RuntimeClassFlags<WinRt>, Controls::IWebViewNavigationCompletedEventArgs> {
    InspectableClass(L"Windows.UI.Xaml.Controls.WebViewNavigationCompletedEventArgs", BaseTrust);
    ComPtr<Foundation::IUriRuntimeClass> uri_;
    boolean success_;
    ABI::Windows::Web::WebErrorStatus status_;

public:
    CompletedArgs(Foundation::IUriRuntimeClass* uri, bool success, COREWEBVIEW2_WEB_ERROR_STATUS status)
        : uri_(uri), success_(success), status_(success ? ABI::Windows::Web::WebErrorStatus_Unknown : WebStatus(status)) {}
    HRESULT STDMETHODCALLTYPE get_Uri(Foundation::IUriRuntimeClass** value) override { return uri_.CopyTo(value); }
    HRESULT STDMETHODCALLTYPE get_IsSuccess(boolean* value) override {
        if (!value) return E_POINTER;
        *value = success_;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_WebErrorStatus(ABI::Windows::Web::WebErrorStatus* value) override {
        if (!value) return E_POINTER;
        *value = status_;
        return S_OK;
    }
};
// Typed handlers for WebView events, sender first.
template <class Args>
using WebViewHandler = Foundation::ITypedEventHandler<Controls::WebView*, Args*>;
using StartingHandler = WebViewHandler<Controls::WebViewNavigationStartingEventArgs>;
using ContentLoadingHandler = WebViewHandler<Controls::WebViewContentLoadingEventArgs>;
using DomLoadedHandler = WebViewHandler<Controls::WebViewDOMContentLoadedEventArgs>;
using CompletedHandler = WebViewHandler<Controls::WebViewNavigationCompletedEventArgs>;
using ScriptDetectedHandler = WebViewHandler<Controls::WebViewLongRunningScriptDetectedEventArgs>;
using UnviewableHandler = WebViewHandler<Controls::WebViewUnviewableContentIdentifiedEventArgs>;
using WarningHandler = Foundation::ITypedEventHandler<Controls::WebView*, IInspectable*>;
// JSON quoting prevents string arguments from becoming code, including NULs.
std::wstring Quote(const std::wstring& text) {
    std::wstring result = L"\"";
    for (wchar_t c : text) {
        if (c == L'"' || c == L'\\') {
            result += L'\\';
            result += c;
        } else if (c < 32 || c == 0x2028 || c == 0x2029) {
            wchar_t escape[7];
            swprintf_s(escape, L"\\u%04x", static_cast<unsigned>(c));
            result += escape;
        } else
            result += c;
    }
    return result + L'"';
}
HRESULT ScriptResult(const wchar_t* json, HSTRING* value) {
    ComPtr<ABI::Windows::Data::Json::IJsonValueStatics> factory;
    ComPtr<ABI::Windows::Data::Json::IJsonValue> parsed;
    auto hr = OsGetActivationFactory(HStringReference(L"Windows.Data.Json.JsonValue").Get(), IID_PPV_ARGS(&factory));
    if (SUCCEEDED(hr)) hr = factory->Parse(HStringReference(json).Get(), &parsed);
    if (SUCCEEDED(hr)) {
        ABI::Windows::Data::Json::JsonValueType type;
        hr = parsed->get_ValueType(&type);
        if (SUCCEEDED(hr) && type == ABI::Windows::Data::Json::JsonValueType_String) return parsed->GetString(value);
    }
    return SUCCEEDED(hr) ? String(L"", value) : hr;
}
class NotifyArgs final
    : public RuntimeClass<RuntimeClassFlags<WinRt>, Controls::INotifyEventArgs, Controls::INotifyEventArgs2> {
    InspectableClass(L"Windows.UI.Xaml.Controls.NotifyEventArgs", BaseTrust);
    std::wstring text_;
    ComPtr<Foundation::IUriRuntimeClass> uri_;

public:
    NotifyArgs(std::wstring text, Foundation::IUriRuntimeClass* uri) : text_(std::move(text)), uri_(uri) {}
    HRESULT STDMETHODCALLTYPE get_Value(HSTRING* value) override { return String(text_, value); }
    HRESULT STDMETHODCALLTYPE get_CallingUri(Foundation::IUriRuntimeClass** value) override {
        return uri_.CopyTo(value);
    }
};

class WebView final : public Controls::IWebView, public Controls::IWebView2, public IIslandWebView {
    std::atomic<ULONG> refs_{1};
    // Only retain the nondelegating inner: ordinary interfaces retain this
    // outer and would form a reference cycle if stored as members.
    ComPtr<IInspectable> inner_;
    struct Lifetime {
        WebView* owner;
    };
    std::shared_ptr<Lifetime> lifetime_ = std::make_shared<Lifetime>(Lifetime{this});
    ComPtr<ICoreWebView2Controller> controller_;
    ComPtr<ICoreWebView2> browser_;
    // Own a host sibling whose z-order we can control without depending on
    // Chromium's private window classes or reparenting XAML's bridge.
    HWND window_ = nullptr;
    COLORREF windowBackground_ = CLR_INVALID;
    RECT clip_{};
    bool clipped_ = false;
    ComPtr<Foundation::IUriRuntimeClass> source_;
    EventSource<Controls::INotifyEventHandler> notify_;
    EventSource<Xaml::Navigation::ILoadCompletedEventHandler> loaded_;
    EventSource<Controls::IWebViewNavigationFailedEventHandler> failed_;
    // Windows 8.1 events. Frame and content-warning events are accepted but
    // never raised: WebView2 reports no equivalents the games depend on.
    EventSource<StartingHandler> navigationStarting_, frameStarting_;
    EventSource<ContentLoadingHandler> contentLoading_, frameContentLoading_;
    EventSource<DomLoadedHandler> domLoaded_, frameDomLoaded_;
    EventSource<CompletedHandler> completed_, frameCompleted_;
    EventSource<ScriptDetectedHandler> scriptDetected_;
    EventSource<WarningHandler> unsafeWarning_;
    EventSource<UnviewableHandler> unviewable_;
    // A navigation's address by WebView2 id, from its start: once it is
    // cancelled or fails, the browser's Source still names the previous page.
    std::map<UINT64, std::wstring> navigations_;
    EventRegistrationToken loadedToken_{}, unloadedToken_{}, layoutToken_{};
    EventRegistrationToken messageToken_{}, navigationToken_{}, startingToken_{}, contentToken_{}, domToken_{};
    bool attached_ = false, starting_ = false, ready_ = false, pendingHtml_ = false, pending_ = false;
    HRESULT browserError_ = S_OK;
    std::wstring navigation_;
    ABI::Windows::UI::Color background_{255, 255, 255, 255};

    static LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM wparam, LPARAM lparam) {
        auto* self = reinterpret_cast<WebView*>(GetWindowLongPtrW(window, GWLP_USERDATA));
        if (message == WM_NCCREATE) {
            self = static_cast<WebView*>(reinterpret_cast<CREATESTRUCTW*>(lparam)->lpCreateParams);
            SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(self));
        }
        if (message == WM_NCDESTROY && self) {
            self->window_ = nullptr;
            SetWindowLongPtrW(window, GWLP_USERDATA, 0);
        }
        return DefWindowProcW(window, message, wparam, lparam);
    }

    template <class T> HRESULT Inner(ComPtr<T>& out) { return inner_.As(&out); }
    // Game code may invoke synchronous JS from ScriptNotify. Queue it on XAML
    // so it runs after WebView2's callback, where nested pumping is forbidden.
    void Dispatch(std::function<void(WebView*)> work) {
        ComPtr<Xaml::IDependencyObject> object;
        ComPtr<ABI::Windows::UI::Core::ICoreDispatcher> dispatcher;
        if (FAILED(Inner(object)) || FAILED(object->get_Dispatcher(&dispatcher))) return;
        auto weak = std::weak_ptr<Lifetime>(lifetime_);
        auto callback =
            Callback<Implements<RuntimeClassFlags<ClassicCom>, ABI::Windows::UI::Core::IDispatchedHandler, FtmBase>>(
                [weak, work]() -> HRESULT {
                    auto alive = weak.lock();
                    if (alive && alive->owner) {
                        ComPtr<Controls::IWebView> keep(alive->owner);
                        work(alive->owner);
                    }
                    return S_OK;
                });
        ComPtr<Foundation::IAsyncAction> action;
        auto hr = dispatcher->RunAsync(ABI::Windows::UI::Core::CoreDispatcherPriority_Normal, callback.Get(), &action);
        if (FAILED(hr)) Log("FAIL", "WebView2 event dispatch failed hr=%08lx", hr);
    }
    // A window region clips the host and its browser children, for painting
    // and for WindowFromPoint input routing alike. Unchanged regions are not
    // reapplied: layout passes are frequent and a new region repaints.
    void Clip(const RECT& clip, LONG width, LONG height) {
        const bool whole = clip.left <= 0 && clip.top <= 0 && clip.right >= width && clip.bottom >= height;
        if (whole ? !clipped_ : clipped_ && EqualRect(&clip, &clip_)) return;
        HRGN region = whole ? nullptr : CreateRectRgn(clip.left, clip.top, clip.right, clip.bottom);
        if (!whole && !region) return;
        // On success the window owns the region.
        if (!SetWindowRgn(window_, region, TRUE)) {
            if (region) DeleteObject(region);
            return;
        }
        clipped_ = !whole;
        clip_ = clip;
    }
    // A removed WebView keeps its page and script state, as on Windows. Its
    // renderer is suspended until the element returns or the game calls in.
    void Suspend() {
        ComPtr<ICoreWebView2_3> browser;
        if (!browser_ || FAILED(browser_.As(&browser))) return;
        auto done = Callback<ICoreWebView2TrySuspendCompletedHandler>([](HRESULT hr, BOOL suspended) -> HRESULT {
            Log("TRACE", "WebView2 suspend hr=%08lx suspended=%d", hr, suspended);
            return S_OK;
        });
        const auto hr = browser->TrySuspend(done.Get());
        if (FAILED(hr)) Log("TRACE", "WebView2 suspend unavailable hr=%08lx", hr);
    }
    void Resume() {
        ComPtr<ICoreWebView2_3> browser;
        BOOL suspended = FALSE;
        if (browser_ && SUCCEEDED(browser_.As(&browser)) && SUCCEEDED(browser->get_IsSuspended(&suspended)) &&
            suspended)
            browser->Resume();
    }
    void UpdateBounds() {
        if (!controller_ || !window_) return;
        ComPtr<Xaml::IFrameworkElement> element;
        ComPtr<Xaml::IUIElement> ui;
        if (FAILED(Inner(element)) || FAILED(Inner(ui))) return;
        Xaml::Visibility visibility = Xaml::Visibility_Collapsed;
        double width = 0, height = 0;
        element->get_ActualWidth(&width);
        element->get_ActualHeight(&height);
        ui->get_Visibility(&visibility);
        ComPtr<Xaml::Media::IGeneralTransform> transform;
        Foundation::Point origin{};
        auto hr = ui->TransformToVisual(nullptr, &transform);
        if (SUCCEEDED(hr)) hr = transform->TransformPoint({}, &origin);
        double scale = 1;
        ComPtr<Xaml::IUIElement10> modern;
        ComPtr<Xaml::IXamlRoot> root;
        if (SUCCEEDED(inner_.As(&modern)) && SUCCEEDED(modern->get_XamlRoot(&root)) && root)
            root->get_RasterizationScale(&scale);
        bool visible =
            ready_ && attached_ && SUCCEEDED(hr) && visibility == Xaml::Visibility_Visible && width > 0 && height > 0;
        // An HWND does not inherit XAML's effective visibility. In particular,
        // collapsing a containing panel must hide the browser as well.
        ComPtr<Xaml::Media::IVisualTreeHelperStatics> tree;
        ComPtr<Xaml::IDependencyObject> ancestor;
        bool opaqueBackground = background_.A == 255;
        COLORREF background =
            opaqueBackground ? RGB(background_.R, background_.G, background_.B) : GetSysColor(COLOR_WINDOW);
        // An HWND is not clipped by XAML either: keep the part of the element
        // its ancestors show, such as a narrowed SettingsFlyout or a
        // ScrollViewer viewport. Canvas and Popup do not clip their children.
        double clipLeft = origin.X, clipTop = origin.Y, clipRight = origin.X + width, clipBottom = origin.Y + height;
        if (visible &&
            SUCCEEDED(OsGetActivationFactory(HStringReference(L"Windows.UI.Xaml.Media.VisualTreeHelper").Get(),
                                             IID_PPV_ARGS(&tree))) &&
            SUCCEEDED(Inner(ancestor))) {
            while (ancestor && visible) {
                ComPtr<Xaml::IUIElement> parentUi;
                if (SUCCEEDED(ancestor.As(&parentUi))) {
                    parentUi->get_Visibility(&visibility);
                    visible = visibility == Xaml::Visibility_Visible;
                }
                ComPtr<Controls::ICanvas> canvas;
                ComPtr<Controls::Primitives::IPopup> popup;
                ComPtr<Xaml::IFrameworkElement> frame;
                ComPtr<Xaml::Media::IGeneralTransform> toRoot;
                Foundation::Point corner{};
                double frameWidth = 0, frameHeight = 0;
                if (visible && parentUi && FAILED(ancestor.As(&canvas)) && FAILED(ancestor.As(&popup)) &&
                    SUCCEEDED(ancestor.As(&frame)) && SUCCEEDED(parentUi->TransformToVisual(nullptr, &toRoot)) &&
                    SUCCEEDED(toRoot->TransformPoint({}, &corner)) && SUCCEEDED(frame->get_ActualWidth(&frameWidth)) &&
                    SUCCEEDED(frame->get_ActualHeight(&frameHeight))) {
                    clipLeft = (std::max)(clipLeft, static_cast<double>(corner.X));
                    clipTop = (std::max)(clipTop, static_cast<double>(corner.Y));
                    clipRight = (std::min)(clipRight, corner.X + frameWidth);
                    clipBottom = (std::min)(clipBottom, corner.Y + frameHeight);
                }
                // A native HWND cannot expose the island's backdrop through
                // browser transparency. Resolve the nearest opaque XAML brush.
                if (!opaqueBackground) {
                    ComPtr<Controls::IPanel> panel;
                    ComPtr<Controls::IBorder> border;
                    ComPtr<Xaml::Media::IBrush> brush;
                    if (SUCCEEDED(ancestor.As(&panel)))
                        panel->get_Background(&brush);
                    else if (SUCCEEDED(ancestor.As(&border)))
                        border->get_Background(&brush);
                    ComPtr<Xaml::Media::ISolidColorBrush> solid;
                    double opacity = 0;
                    ABI::Windows::UI::Color color{};
                    if (brush && SUCCEEDED(brush->get_Opacity(&opacity)) && opacity == 1 &&
                        SUCCEEDED(brush.As(&solid)) && SUCCEEDED(solid->get_Color(&color)) && color.A == 255) {
                        background = RGB(color.R, color.G, color.B);
                        opaqueBackground = true;
                    }
                }
                ComPtr<Xaml::IDependencyObject> parent;
                if (FAILED(tree->GetParent(ancestor.Get(), &parent))) break;
                ancestor = std::move(parent);
            }
        }
        if (windowBackground_ != background) {
            windowBackground_ = background;
            ComPtr<ICoreWebView2Controller2> color;
            auto colorHr = controller_.As(&color);
            if (SUCCEEDED(colorHr))
                colorHr = color->put_DefaultBackgroundColor(
                    {255, GetRValue(background), GetGValue(background), GetBValue(background)});
            if (FAILED(colorHr)) Log("FAIL", "WebView background failed hr=%08lx", colorHr);
        }
        // In host pixels, relative to the element's own origin.
        const RECT clip{static_cast<LONG>(std::lround((clipLeft - origin.X) * scale)),
                        static_cast<LONG>(std::lround((clipTop - origin.Y) * scale)),
                        static_cast<LONG>(std::lround((clipRight - origin.X) * scale)),
                        static_cast<LONG>(std::lround((clipBottom - origin.Y) * scale))};
        if (clip.right <= clip.left || clip.bottom <= clip.top) visible = false;
        if (visible) {
            RECT bounds{static_cast<LONG>(std::lround(origin.X * scale)),
                        static_cast<LONG>(std::lround(origin.Y * scale)),
                        static_cast<LONG>(std::lround((origin.X + width) * scale)),
                        static_cast<LONG>(std::lround((origin.Y + height) * scale))};
            Clip(clip, bounds.right - bounds.left, bounds.bottom - bounds.top);
            Resume(); // after a suspension that completed while it was being re-added
            SetWindowPos(window_, HWND_TOP, bounds.left, bounds.top, bounds.right - bounds.left,
                         bounds.bottom - bounds.top, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            controller_->put_Bounds({0, 0, bounds.right - bounds.left, bounds.bottom - bounds.top});
        } else {
            ShowWindow(window_, SW_HIDE);
        }
        controller_->put_IsVisible(visible);
    }
    std::wstring NavigationAddress(UINT64 id) {
        if (auto found = navigations_.find(id); found != navigations_.end()) return found->second;
        LPWSTR address = nullptr;
        std::wstring text;
        if (browser_ && SUCCEEDED(browser_->get_Source(&address)) && address) text = address;
        CoTaskMemFree(address);
        return text;
    }
    HRESULT NavigatePending() {
        if (!ready_ || !pending_) return S_OK;
        pending_ = false;
        Resume();
        return pendingHtml_ ? browser_->NavigateToString(navigation_.c_str()) : browser_->Navigate(navigation_.c_str());
    }
    HRESULT Connect(ICoreWebView2Controller* controller) {
        controller_ = controller;
        auto hr = controller->get_CoreWebView2(&browser_);
        if (FAILED(hr)) return hr;
        // Legacy apps author their HTML for a light canvas. Chromium's OS
        // scheme can override even an explicitly light DefaultBackgroundColor.
        ComPtr<ICoreWebView2_13> modernBrowser;
        ComPtr<ICoreWebView2Profile> profile;
        if (SUCCEEDED(browser_.As(&modernBrowser)) && SUCCEEDED(modernBrowser->get_Profile(&profile))) {
            auto scheme = profile->put_PreferredColorScheme(COREWEBVIEW2_PREFERRED_COLOR_SCHEME_LIGHT);
            if (FAILED(scheme)) Log("FAIL", "WebView color scheme failed hr=%08lx", scheme);
        }
        hr = SetBackgroundColor(background_);
        if (FAILED(hr)) return hr;
        ComPtr<ICoreWebView2_3> mapping;
        hr = browser_.As(&mapping);
        if (SUCCEEDED(hr))
            hr = mapping->SetVirtualHostNameToFolderMapping(L"appx.invalid", CurrentPackage().directory.c_str(),
                                                            COREWEBVIEW2_HOST_RESOURCE_ACCESS_KIND_DENY_CORS);
        if (FAILED(hr)) return hr;
        auto weak = std::weak_ptr<Lifetime>(lifetime_);
        auto message = Callback<ICoreWebView2WebMessageReceivedEventHandler>(
            [weak](ICoreWebView2*, ICoreWebView2WebMessageReceivedEventArgs* args) -> HRESULT {
                auto alive = weak.lock();
                if (!alive || !alive->owner) return S_OK;
                LPWSTR text = nullptr, source = nullptr;
                auto hr = args->TryGetWebMessageAsString(&text);
                args->get_Source(&source);
                // Match legacy local-content trust. Remote pages do not gain
                // access to the application's ScriptNotify handlers.
                const std::wstring origin = source ? source : L"";
                const bool local = origin == L"about:blank" || origin.starts_with(L"about:blank#") ||
                                   origin.starts_with(L"https://appx.invalid/");
                if (SUCCEEDED(hr) && local) {
                    ComPtr<Foundation::IUriRuntimeClass> uri;
                    if (source) Uri(source, &uri);
                    auto event = Make<NotifyArgs>(std::wstring(text), uri.Get());
                    alive->owner->Dispatch([event](WebView* self) {
                        self->notify_.InvokeAll(self->Identity(), event.Get());
                    });
                }
                CoTaskMemFree(text);
                CoTaskMemFree(source);
                return S_OK;
            });
        hr = browser_->add_WebMessageReceived(message.Get(), &messageToken_);
        if (FAILED(hr)) return hr;
        // NavigationStarting runs inside WebView2's callback, not on a later
        // dispatcher turn, so a handler's Cancel can still stop the navigation.
        auto starting = Callback<ICoreWebView2NavigationStartingEventHandler>(
            [weak](ICoreWebView2*, ICoreWebView2NavigationStartingEventArgs* args) -> HRESULT {
                auto alive = weak.lock();
                if (!alive || !alive->owner) return S_OK;
                auto* self = alive->owner;
                ComPtr<Controls::IWebView> keep(self);
                UINT64 id = 0;
                LPWSTR address = nullptr;
                args->get_NavigationId(&id);
                args->get_Uri(&address);
                const std::wstring text = address ? address : L"";
                CoTaskMemFree(address);
                self->navigations_[id] = text;
                ComPtr<Foundation::IUriRuntimeClass> uri;
                EventUri(text, &uri);
                auto event = Make<StartingArgs>(uri.Get());
                self->navigationStarting_.InvokeAll(static_cast<Controls::IWebView*>(self), event.Get());
                if (event->cancel) {
                    Log("TRACE", "WebView navigation cancelled by the game");
                    args->put_Cancel(TRUE);
                }
                return S_OK;
            });
        hr = browser_->add_NavigationStarting(starting.Get(), &startingToken_);
        if (FAILED(hr)) return hr;
        auto content = Callback<ICoreWebView2ContentLoadingEventHandler>(
            [weak](ICoreWebView2*, ICoreWebView2ContentLoadingEventArgs* args) -> HRESULT {
                auto alive = weak.lock();
                if (!alive || !alive->owner) return S_OK;
                UINT64 id = 0;
                args->get_NavigationId(&id);
                ComPtr<Foundation::IUriRuntimeClass> uri;
                EventUri(alive->owner->NavigationAddress(id), &uri);
                auto event = Make<ContentLoadingArgs>(uri.Get());
                alive->owner->Dispatch([event](WebView* self) {
                    self->contentLoading_.InvokeAll(static_cast<Controls::IWebView*>(self), event.Get());
                });
                return S_OK;
            });
        hr = browser_->add_ContentLoading(content.Get(), &contentToken_);
        if (FAILED(hr)) return hr;
        ComPtr<ICoreWebView2_2> dom;
        if (SUCCEEDED(browser_.As(&dom))) {
            auto loaded = Callback<ICoreWebView2DOMContentLoadedEventHandler>(
                [weak](ICoreWebView2*, ICoreWebView2DOMContentLoadedEventArgs* args) -> HRESULT {
                    auto alive = weak.lock();
                    if (!alive || !alive->owner) return S_OK;
                    UINT64 id = 0;
                    args->get_NavigationId(&id);
                    ComPtr<Foundation::IUriRuntimeClass> uri;
                    EventUri(alive->owner->NavigationAddress(id), &uri);
                    auto event = Make<DomLoadedArgs>(uri.Get());
                    alive->owner->Dispatch([event](WebView* self) {
                        self->domLoaded_.InvokeAll(static_cast<Controls::IWebView*>(self), event.Get());
                    });
                    return S_OK;
                });
            hr = dom->add_DOMContentLoaded(loaded.Get(), &domToken_);
            if (FAILED(hr)) return hr;
        }
        auto completed = Callback<ICoreWebView2NavigationCompletedEventHandler>(
            [weak](ICoreWebView2* browser, ICoreWebView2NavigationCompletedEventArgs* args) -> HRESULT {
                auto alive = weak.lock();
                if (!alive || !alive->owner) return S_OK;
                BOOL success = FALSE;
                args->get_IsSuccess(&success);
                Log(success ? "TRACE" : "FAIL", "WebView2 navigation completed success=%d", success);
                COREWEBVIEW2_WEB_ERROR_STATUS status{};
                if (!success) args->get_WebErrorStatus(&status);
                UINT64 id = 0;
                args->get_NavigationId(&id);
                ComPtr<Foundation::IUriRuntimeClass> target;
                EventUri(alive->owner->NavigationAddress(id), &target);
                alive->owner->navigations_.erase(id);
                LPWSTR address = nullptr;
                ComPtr<Foundation::IUriRuntimeClass> uri;
                if (SUCCEEDED(browser->get_Source(&address)) && address) Uri(address, &uri);
                CoTaskMemFree(address);
                // Windows raises NavigationCompleted before LoadCompleted or
                // NavigationFailed.
                auto navigation = Make<CompletedArgs>(target.Get(), success != FALSE, status);
                if (success) {
                    auto event = Make<LoadArgs>(uri.Get());
                    alive->owner->Dispatch([navigation, event](WebView* self) {
                        self->completed_.InvokeAll(static_cast<Controls::IWebView*>(self), navigation.Get());
                        self->loaded_.InvokeAll(self->Identity(), event.Get());
                    });
                } else {
                    auto event = Make<FailureArgs>(uri.Get(), status);
                    alive->owner->Dispatch([navigation, event](WebView* self) {
                        self->completed_.InvokeAll(static_cast<Controls::IWebView*>(self), navigation.Get());
                        self->failed_.InvokeAll(self->Identity(), event.Get());
                    });
                }
                return S_OK;
            });
        hr = browser_->add_NavigationCompleted(completed.Get(), &navigationToken_);
        if (FAILED(hr)) return hr;
        auto script = Callback<ICoreWebView2AddScriptToExecuteOnDocumentCreatedCompletedHandler>(
            [weak](HRESULT status, LPCWSTR) -> HRESULT {
                auto alive = weak.lock();
                if (!alive || !alive->owner) return S_OK;
                alive->owner->browserError_ = status;
                if (SUCCEEDED(status)) {
                    alive->owner->ready_ = true;
                    alive->owner->UpdateBounds();
                    return alive->owner->NavigatePending();
                }
                return S_OK;
            });
        return browser_->AddScriptToExecuteOnDocumentCreated(
            L"window.external = window.external || {}; window.external.notify = function(s) { "
            L"window.chrome.webview.postMessage(String(s)); };",
            script.Get());
    }
    HRESULT Start() {
        if (starting_ || browser_ || !attached_) return browserError_;
        if (!HostWindow()) return E_PENDING;
        WNDCLASSW type{};
        type.lpfnWndProc = WindowProc;
        type.hInstance = GetModuleHandleW(nullptr);
        type.hbrBackground = GetSysColorBrush(COLOR_WINDOW);
        type.lpszClassName = L"AppxPortableWebView";
        if (!RegisterClassW(&type) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS)
            return HRESULT_FROM_WIN32(GetLastError());
        window_ = CreateWindowExW(0, type.lpszClassName, nullptr, WS_CHILD | WS_CLIPCHILDREN | WS_CLIPSIBLINGS, 0, 0, 1,
                                  1, HostWindow(), nullptr, type.hInstance, this);
        if (!window_) return HRESULT_FROM_WIN32(GetLastError());
        const HWND parent = window_;
        starting_ = true;
        auto weak = std::weak_ptr<Lifetime>(lifetime_);
        auto environment = Callback<ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler>(
            [weak, parent](HRESULT status, ICoreWebView2Environment* env) -> HRESULT {
                auto alive = weak.lock();
                if (!alive || !alive->owner) return S_OK;
                if (FAILED(status)) {
                    alive->owner->browserError_ = status;
                    Log("FAIL", "WebView2 environment failed hr=%08lx (Evergreen runtime required)", status);
                    return S_OK;
                }
                auto complete = Callback<ICoreWebView2CreateCoreWebView2ControllerCompletedHandler>(
                    [weak](HRESULT result, ICoreWebView2Controller* controller) -> HRESULT {
                        auto life = weak.lock();
                        if (!life || !life->owner) {
                            if (controller) controller->Close();
                            return S_OK;
                        }
                        if (SUCCEEDED(result)) result = life->owner->Connect(controller);
                        life->owner->browserError_ = result;
                        if (FAILED(result)) Log("FAIL", "WebView2 controller failed hr=%08lx", result);
                        return S_OK;
                    });
                auto hr = env->CreateCoreWebView2Controller(parent, complete.Get());
                if (FAILED(hr)) alive->owner->browserError_ = hr;
                return S_OK;
            });
        std::filesystem::path folder;
        auto hr = ProfileFolder(folder);
        if (SUCCEEDED(hr))
            hr = CreateCoreWebView2EnvironmentWithOptions(nullptr, folder.c_str(), nullptr, environment.Get());
        if (FAILED(hr)) {
            browserError_ = hr;
            Log("FAIL", "WebView2 startup failed hr=%08lx", hr);
        }
        return hr;
    }

public:
    // The object's identity: IWebView and IWebView2 both derive IInspectable.
    IInspectable* Identity() { return static_cast<Controls::IWebView*>(this); }
    ~WebView() {
        lifetime_->owner = nullptr;
        ComPtr<Xaml::IFrameworkElement> element;
        if (inner_ && SUCCEEDED(Inner(element))) {
            element->remove_Loaded(loadedToken_);
            element->remove_Unloaded(unloadedToken_);
            element->remove_LayoutUpdated(layoutToken_);
        }
        if (browser_) {
            browser_->remove_WebMessageReceived(messageToken_);
            browser_->remove_NavigationCompleted(navigationToken_);
            browser_->remove_NavigationStarting(startingToken_);
            browser_->remove_ContentLoading(contentToken_);
            ComPtr<ICoreWebView2_2> dom;
            if (SUCCEEDED(browser_.As(&dom))) dom->remove_DOMContentLoaded(domToken_);
        }
        if (controller_) controller_->Close();
        if (window_) DestroyWindow(window_);
    }
    HRESULT Initialize() {
        ComPtr<Controls::IGridFactory> factory;
        auto hr =
            OsGetActivationFactory(HStringReference(L"Windows.UI.Xaml.Controls.Grid").Get(), IID_PPV_ARGS(&factory));
        ComPtr<Controls::IGrid> grid;
        if (SUCCEEDED(hr)) hr = factory->CreateInstance(Identity(), &inner_, &grid);
        if (FAILED(hr)) return hr;
        ComPtr<Xaml::IFrameworkElement> element;
        hr = Inner(element);
        if (FAILED(hr)) return hr;
        auto weak = std::weak_ptr<Lifetime>(lifetime_);
        auto loaded = Callback<Xaml::IRoutedEventHandler>([weak](IInspectable*, Xaml::IRoutedEventArgs*) -> HRESULT {
            auto life = weak.lock();
            if (life && life->owner) {
                life->owner->attached_ = true;
                life->owner->Start();
                life->owner->Resume();
                life->owner->UpdateBounds();
            }
            return S_OK;
        });
        auto unloaded = Callback<Xaml::IRoutedEventHandler>([weak](IInspectable*, Xaml::IRoutedEventArgs*) -> HRESULT {
            auto life = weak.lock();
            if (life && life->owner) {
                life->owner->attached_ = false;
                // Hiding makes the browser invisible, which suspension requires.
                life->owner->UpdateBounds();
                life->owner->Suspend();
            }
            return S_OK;
        });
        auto layout =
            Callback<Foundation::IEventHandler<IInspectable*>>([weak](IInspectable*, IInspectable*) -> HRESULT {
                auto life = weak.lock();
                if (life && life->owner) life->owner->UpdateBounds();
                return S_OK;
            });
        hr = element->add_Loaded(loaded.Get(), &loadedToken_);
        if (SUCCEEDED(hr)) hr = element->add_Unloaded(unloaded.Get(), &unloadedToken_);
        if (SUCCEEDED(hr)) hr = element->add_LayoutUpdated(layout.Get(), &layoutToken_);
        return hr;
    }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        if (iid == __uuidof(IUnknown) || iid == __uuidof(IInspectable) || iid == __uuidof(Controls::IWebView)) {
            *value = static_cast<Controls::IWebView*>(this);
            AddRef();
            return S_OK;
        }
        if (iid == __uuidof(Controls::IWebView2)) {
            *value = static_cast<Controls::IWebView2*>(this);
            AddRef();
            return S_OK;
        }
        if (iid == __uuidof(IIslandWebView)) {
            *value = static_cast<IIslandWebView*>(this);
            AddRef();
            return S_OK;
        }
        return inner_ ? inner_->QueryInterface(iid, value) : E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++refs_; }
    HRESULT STDMETHODCALLTYPE GetBackgroundColor(ABI::Windows::UI::Color* value) override {
        if (!value) return E_POINTER;
        *value = background_;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE SetBackgroundColor(ABI::Windows::UI::Color value) override {
        background_ = value;
        windowBackground_ = CLR_INVALID;
        if (!controller_) return S_OK;
        UpdateBounds();
        return S_OK;
    }
    ULONG STDMETHODCALLTYPE Release() override {
        const auto n = --refs_;
        if (!n) {
            // Cleanup queries the inner for event removal. Its delegating
            // interfaces briefly retain the outer while it is being destroyed.
            refs_ = 1;
            delete this;
        }
        return n;
    }
    HRESULT STDMETHODCALLTYPE GetIids(ULONG* count, IID** ids) override {
        if (!count || !ids) return E_POINTER;
        *count = 0;
        *ids = static_cast<IID*>(CoTaskMemAlloc(2 * sizeof(IID)));
        if (!*ids) return E_OUTOFMEMORY;
        (*ids)[0] = __uuidof(Controls::IWebView);
        (*ids)[1] = __uuidof(Controls::IWebView2);
        *count = 2;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetRuntimeClassName(HSTRING* value) override {
        return String(L"Windows.UI.Xaml.Controls.WebView", value);
    }
    HRESULT STDMETHODCALLTYPE GetTrustLevel(TrustLevel* value) override {
        if (!value) return E_POINTER;
        *value = BaseTrust;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_Source(Foundation::IUriRuntimeClass** value) override {
        return source_.CopyTo(value);
    }
    HRESULT STDMETHODCALLTYPE put_Source(Foundation::IUriRuntimeClass* value) override { return Navigate(value); }
    HRESULT STDMETHODCALLTYPE get_AllowedScriptNotifyUris(__FIVector_1_Windows__CFoundation__CUri** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        return E_NOTIMPL;
    }
    HRESULT STDMETHODCALLTYPE put_AllowedScriptNotifyUris(__FIVector_1_Windows__CFoundation__CUri*) override {
        return E_NOTIMPL;
    }
    HRESULT STDMETHODCALLTYPE
    get_DataTransferPackage(ABI::Windows::ApplicationModel::DataTransfer::IDataPackage** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        return E_NOTIMPL;
    }
    HRESULT STDMETHODCALLTYPE add_LoadCompleted(Xaml::Navigation::ILoadCompletedEventHandler* handler,
                                                EventRegistrationToken* token) override {
        return loaded_.Add(handler, token);
    }
    HRESULT STDMETHODCALLTYPE remove_LoadCompleted(EventRegistrationToken token) override {
        return loaded_.Remove(token);
    }
    HRESULT STDMETHODCALLTYPE add_ScriptNotify(Controls::INotifyEventHandler* handler,
                                               EventRegistrationToken* token) override {
        return notify_.Add(handler, token);
    }
    HRESULT STDMETHODCALLTYPE remove_ScriptNotify(EventRegistrationToken token) override {
        return notify_.Remove(token);
    }
    HRESULT STDMETHODCALLTYPE add_NavigationFailed(Controls::IWebViewNavigationFailedEventHandler* handler,
                                                   EventRegistrationToken* token) override {
        return failed_.Add(handler, token);
    }
    HRESULT STDMETHODCALLTYPE remove_NavigationFailed(EventRegistrationToken token) override {
        return failed_.Remove(token);
    }
    HRESULT STDMETHODCALLTYPE Navigate(Foundation::IUriRuntimeClass* source) override {
        if (!source) return E_INVALIDARG;
        HString uri;
        auto hr = source->get_AbsoluteUri(uri.GetAddressOf());
        if (FAILED(hr)) return hr;
        source_ = source;
        navigation_ = MapPackageUris(Text(uri.Get()));
        pendingHtml_ = false;
        pending_ = true;
        return FAILED(browserError_) ? browserError_ : NavigatePending();
    }
    HRESULT STDMETHODCALLTYPE NavigateToString(HSTRING text) override {
        navigation_ = MapPackageUris(Text(text));
        pendingHtml_ = true;
        pending_ = true;
        source_.Reset();
        return FAILED(browserError_) ? browserError_ : NavigatePending();
    }
    HRESULT STDMETHODCALLTYPE InvokeScript(HSTRING name, UINT32 count, HSTRING* arguments, HSTRING* value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        if (count && !arguments) return E_POINTER;
        if (FAILED(browserError_)) return browserError_;
        if (!browser_) return E_PENDING;
        // A caller can remove the element while the nested loop dispatches UI.
        ComPtr<Controls::IWebView> keep(this);
        const auto script = Text(name);
        if (script.empty()) return E_INVALIDARG;
        // Resolve dotted function names without interpreting the name as code.
        std::wstring expression = L"(()=>{let f=window,o=window;for(const k of " + Quote(script) +
                                  L".split('.')){o=f;f=f[k];}return f.apply(o,[";
        for (UINT32 i = 0; i < count; ++i) {
            if (i) expression += L',';
            expression += Quote(Text(arguments[i]));
        }
        expression += L"]);})()";
        struct Result {
            bool done = false;
            HRESULT status = E_PENDING;
            std::wstring json;
        };
        auto result = std::make_shared<Result>();
        auto complete =
            Callback<ICoreWebView2ExecuteScriptCompletedHandler>([result](HRESULT status, LPCWSTR json) -> HRESULT {
                result->status = status;
                result->json = json ? json : L"null";
                result->done = true;
                return S_OK;
            });
        Resume();
        auto hr = browser_->ExecuteScript(expression.c_str(), complete.Get());
        if (FAILED(hr)) return hr;
        const auto deadline = GetTickCount64() + 10000;
        while (!result->done) {
            if (GetTickCount64() >= deadline) return HRESULT_FROM_WIN32(ERROR_TIMEOUT);
            MSG message;
            while (!result->done && PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
                if (message.message == WM_QUIT) {
                    PostQuitMessage(static_cast<int>(message.wParam));
                    return E_ABORT;
                }
                TranslateMessage(&message);
                DispatchMessageW(&message);
            }
            if (!result->done) MsgWaitForMultipleObjectsEx(0, nullptr, 25, QS_ALLINPUT, MWMO_INPUTAVAILABLE);
        }
        return SUCCEEDED(result->status) ? ScriptResult(result->json.c_str(), value) : result->status;
    }
    // ---- IWebView2 (Windows 8.1) ----
    HRESULT STDMETHODCALLTYPE get_CanGoBack(boolean* value) override {
        if (!value) return E_POINTER;
        BOOL can = FALSE;
        if (browser_) browser_->get_CanGoBack(&can);
        *value = can != FALSE;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_CanGoForward(boolean* value) override {
        if (!value) return E_POINTER;
        BOOL can = FALSE;
        if (browser_) browser_->get_CanGoForward(&can);
        *value = can != FALSE;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_DocumentTitle(HSTRING* value) override {
        if (!value) return E_POINTER;
        LPWSTR title = nullptr;
        std::wstring text;
        if (browser_ && SUCCEEDED(browser_->get_DocumentTitle(&title)) && title) text = title;
        CoTaskMemFree(title);
        return String(text, value);
    }
    HRESULT STDMETHODCALLTYPE add_NavigationStarting(StartingHandler* handler, EventRegistrationToken* token) override {
        return navigationStarting_.Add(handler, token);
    }
    HRESULT STDMETHODCALLTYPE remove_NavigationStarting(EventRegistrationToken token) override {
        return navigationStarting_.Remove(token);
    }
    HRESULT STDMETHODCALLTYPE add_ContentLoading(ContentLoadingHandler* handler, EventRegistrationToken* token) override {
        return contentLoading_.Add(handler, token);
    }
    HRESULT STDMETHODCALLTYPE remove_ContentLoading(EventRegistrationToken token) override {
        return contentLoading_.Remove(token);
    }
    HRESULT STDMETHODCALLTYPE add_DOMContentLoaded(DomLoadedHandler* handler, EventRegistrationToken* token) override {
        return domLoaded_.Add(handler, token);
    }
    HRESULT STDMETHODCALLTYPE remove_DOMContentLoaded(EventRegistrationToken token) override {
        return domLoaded_.Remove(token);
    }
    // Before the browser exists, navigation commands have no history to act on.
    HRESULT STDMETHODCALLTYPE GoForward() override { return browser_ ? browser_->GoForward() : S_OK; }
    HRESULT STDMETHODCALLTYPE GoBack() override { return browser_ ? browser_->GoBack() : S_OK; }
    HRESULT STDMETHODCALLTYPE Refresh() override { return browser_ ? browser_->Reload() : S_OK; }
    HRESULT STDMETHODCALLTYPE Stop() override {
        pending_ = false; // a navigation queued until the browser is ready
        return browser_ ? browser_->Stop() : S_OK;
    }
    HRESULT STDMETHODCALLTYPE CapturePreviewToStreamAsync(ABI::Windows::Storage::Streams::IRandomAccessStream*,
                                                          Foundation::IAsyncAction** operation) override {
        if (!operation) return E_POINTER;
        *operation = nullptr;
        return E_NOTIMPL;
    }
    HRESULT STDMETHODCALLTYPE InvokeScriptAsync(HSTRING, __FIIterable_1_HSTRING*,
                                                __FIAsyncOperation_1_HSTRING** operation) override {
        if (!operation) return E_POINTER;
        *operation = nullptr;
        return E_NOTIMPL;
    }
    HRESULT STDMETHODCALLTYPE CaptureSelectedContentToDataPackageAsync(
        __FIAsyncOperation_1_Windows__CApplicationModel__CDataTransfer__CDataPackage** operation) override {
        if (!operation) return E_POINTER;
        *operation = nullptr;
        return E_NOTIMPL;
    }
    HRESULT STDMETHODCALLTYPE NavigateToLocalStreamUri(Foundation::IUriRuntimeClass*,
                                                       ABI::Windows::Web::IUriToStreamResolver*) override {
        return E_NOTIMPL;
    }
    HRESULT STDMETHODCALLTYPE BuildLocalStreamUri(HSTRING, HSTRING, Foundation::IUriRuntimeClass** result) override {
        if (!result) return E_POINTER;
        *result = nullptr;
        return E_NOTIMPL;
    }
    HRESULT STDMETHODCALLTYPE get_DefaultBackgroundColor(ABI::Windows::UI::Color* value) override {
        return GetBackgroundColor(value);
    }
    HRESULT STDMETHODCALLTYPE put_DefaultBackgroundColor(ABI::Windows::UI::Color value) override {
        return SetBackgroundColor(value);
    }
    HRESULT STDMETHODCALLTYPE add_NavigationCompleted(CompletedHandler* handler, EventRegistrationToken* token) override {
        return completed_.Add(handler, token);
    }
    HRESULT STDMETHODCALLTYPE remove_NavigationCompleted(EventRegistrationToken token) override {
        return completed_.Remove(token);
    }
    HRESULT STDMETHODCALLTYPE add_FrameNavigationStarting(StartingHandler* handler,
                                                          EventRegistrationToken* token) override {
        return frameStarting_.Add(handler, token);
    }
    HRESULT STDMETHODCALLTYPE remove_FrameNavigationStarting(EventRegistrationToken token) override {
        return frameStarting_.Remove(token);
    }
    HRESULT STDMETHODCALLTYPE add_FrameContentLoading(ContentLoadingHandler* handler,
                                                      EventRegistrationToken* token) override {
        return frameContentLoading_.Add(handler, token);
    }
    HRESULT STDMETHODCALLTYPE remove_FrameContentLoading(EventRegistrationToken token) override {
        return frameContentLoading_.Remove(token);
    }
    HRESULT STDMETHODCALLTYPE add_FrameDOMContentLoaded(DomLoadedHandler* handler,
                                                        EventRegistrationToken* token) override {
        return frameDomLoaded_.Add(handler, token);
    }
    HRESULT STDMETHODCALLTYPE remove_FrameDOMContentLoaded(EventRegistrationToken token) override {
        return frameDomLoaded_.Remove(token);
    }
    HRESULT STDMETHODCALLTYPE add_FrameNavigationCompleted(CompletedHandler* handler,
                                                           EventRegistrationToken* token) override {
        return frameCompleted_.Add(handler, token);
    }
    HRESULT STDMETHODCALLTYPE remove_FrameNavigationCompleted(EventRegistrationToken token) override {
        return frameCompleted_.Remove(token);
    }
    HRESULT STDMETHODCALLTYPE add_LongRunningScriptDetected(ScriptDetectedHandler* handler,
                                                            EventRegistrationToken* token) override {
        return scriptDetected_.Add(handler, token);
    }
    HRESULT STDMETHODCALLTYPE remove_LongRunningScriptDetected(EventRegistrationToken token) override {
        return scriptDetected_.Remove(token);
    }
    HRESULT STDMETHODCALLTYPE add_UnsafeContentWarningDisplaying(WarningHandler* handler,
                                                                 EventRegistrationToken* token) override {
        return unsafeWarning_.Add(handler, token);
    }
    HRESULT STDMETHODCALLTYPE remove_UnsafeContentWarningDisplaying(EventRegistrationToken token) override {
        return unsafeWarning_.Remove(token);
    }
    HRESULT STDMETHODCALLTYPE add_UnviewableContentIdentified(UnviewableHandler* handler,
                                                              EventRegistrationToken* token) override {
        return unviewable_.Add(handler, token);
    }
    HRESULT STDMETHODCALLTYPE remove_UnviewableContentIdentified(EventRegistrationToken token) override {
        return unviewable_.Remove(token);
    }
    HRESULT STDMETHODCALLTYPE NavigateWithHttpRequestMessage(ABI::Windows::Web::Http::IHttpRequestMessage*) override {
        return E_NOTIMPL;
    }
    HRESULT STDMETHODCALLTYPE Focus(Xaml::FocusState, boolean* result) override {
        if (!result) return E_POINTER;
        *result = controller_ && SUCCEEDED(controller_->MoveFocus(COREWEBVIEW2_MOVE_FOCUS_REASON_PROGRAMMATIC));
        return S_OK;
    }
};

class WebViewFactory final : public Factory<Controls::IWebViewFactory4> {
    ComPtr<IActivationFactory> real_;

public:
    WebViewFactory() : Factory(L"Windows.UI.Xaml.Controls.WebView") {}
    HRESULT Initialize() {
        return OsGetActivationFactory(HStringReference(L"Windows.UI.Xaml.Controls.WebView").Get(),
                                      IID_PPV_ARGS(&real_));
    }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** out) noexcept override {
        if (!out) return E_POINTER;
        if (iid == __uuidof(Controls::IWebViewStatics) || iid == __uuidof(Controls::IWebViewStatics2) ||
            iid == __uuidof(Controls::IWebViewStatics3) || iid == __uuidof(Controls::IWebViewStatics4) ||
            iid == __uuidof(Controls::IWebViewStatics5))
            return real_->QueryInterface(iid, out);
        return Factory::QueryInterface(iid, out);
    }
    HRESULT STDMETHODCALLTYPE ActivateInstance(IInspectable** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        auto* web = new (std::nothrow) WebView;
        if (!web) return E_OUTOFMEMORY;
        auto hr = web->Initialize();
        if (SUCCEEDED(hr))
            *value = web->Identity();
        else
            web->Release();
        Log(SUCCEEDED(hr) ? "TRACE" : "FAIL", "WebView2 stand-in activation hr=%08lx", hr);
        return hr;
    }
    HRESULT STDMETHODCALLTYPE CreateInstanceWithExecutionMode(Controls::WebViewExecutionMode mode,
                                                              Controls::IWebView** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        if (mode < Controls::WebViewExecutionMode_SameThread || mode > Controls::WebViewExecutionMode_SeparateProcess)
            return E_INVALIDARG;
        ComPtr<IInspectable> object;
        auto hr = ActivateInstance(&object);
        return SUCCEEDED(hr) ? object.CopyTo(value) : hr;
    }
};
HRESULT MakeWebViewFactory(IActivationFactory** value) {
    if (!IslandsMode()) return CLASS_E_CLASSNOTAVAILABLE;
    auto factory = Make<WebViewFactory>();
    if (!factory) return E_OUTOFMEMORY;
    const auto hr = factory->Initialize();
    return SUCCEEDED(hr) ? factory.CopyTo(value) : hr;
}
const ShimClass webViewClass{L"Windows.UI.Xaml.Controls.WebView", MakeWebViewFactory, ShimPolicy::Replace, InIslands};
}
HRESULT CreateIslandWebView(IInspectable** value) {
    ComPtr<IActivationFactory> factory;
    auto hr = MakeWebViewFactory(&factory);
    return SUCCEEDED(hr) ? factory->ActivateInstance(value) : hr;
}
bool IsIslandWebViewWindow(HWND window) {
    if (!window || !IsChild(HostWindow(), window)) return false;
    for (; window && window != HostWindow(); window = GetParent(window)) {
        wchar_t name[64]{};
        GetClassNameW(window, name, ARRAYSIZE(name));
        if (wcscmp(name, L"AppxPortableWebView") == 0) return true;
    }
    return false;
}
bool RouteIslandWebViewInput(const MSG& message) {
    static HWND dragTarget = nullptr;
    static UINT32 dragPointer = 0;
    if (message.message == WM_POINTERCAPTURECHANGED && LOWORD(message.wParam) == dragPointer) {
        dragTarget = nullptr;
        dragPointer = 0;
    }
    const bool mousePointer =
        message.message == WM_POINTERDOWN || message.message == WM_POINTERUP || message.message == WM_POINTERUPDATE;
    const bool pointer = message.message == WM_POINTERWHEEL || message.message == WM_POINTERHWHEEL;
    if (!pointer && !mousePointer && message.message != WM_MOUSEWHEEL && message.message != WM_MOUSEHWHEEL)
        return false;
    if (IsIslandWebViewWindow(message.hwnd)) return false;
    // XAML's input site receives real pointer input even over a browser
    // sibling. Deliver it to the native child under the screen-space position.
    POINT position{static_cast<short>(LOWORD(message.lParam)), static_cast<short>(HIWORD(message.lParam))};
    HWND target = WindowFromPoint(position);
    POINTER_INFO info{};
    if (mousePointer) {
        if (!GetPointerInfo(LOWORD(message.wParam), &info) || info.pointerType != PT_MOUSE) return false;
        if (dragPointer == info.pointerId && IsIslandWebViewWindow(dragTarget)) target = dragTarget;
    }
    if (!IsIslandWebViewWindow(target)) return false;
    WPARAM buttons = LOWORD(message.wParam);
    if (pointer || mousePointer) {
        buttons = 0;
        if (GetKeyState(VK_SHIFT) < 0) buttons |= MK_SHIFT;
        if (GetKeyState(VK_CONTROL) < 0) buttons |= MK_CONTROL;
        if (GetKeyState(VK_LBUTTON) < 0) buttons |= MK_LBUTTON;
        if (GetKeyState(VK_RBUTTON) < 0) buttons |= MK_RBUTTON;
        if (GetKeyState(VK_MBUTTON) < 0) buttons |= MK_MBUTTON;
    }
    if (mousePointer) {
        UINT mouse = WM_MOUSEMOVE;
        switch (info.ButtonChangeType) {
        case POINTER_CHANGE_FIRSTBUTTON_DOWN:
            mouse = WM_LBUTTONDOWN;
            break;
        case POINTER_CHANGE_FIRSTBUTTON_UP:
            mouse = WM_LBUTTONUP;
            break;
        case POINTER_CHANGE_SECONDBUTTON_DOWN:
            mouse = WM_RBUTTONDOWN;
            break;
        case POINTER_CHANGE_SECONDBUTTON_UP:
            mouse = WM_RBUTTONUP;
            break;
        case POINTER_CHANGE_THIRDBUTTON_DOWN:
            mouse = WM_MBUTTONDOWN;
            break;
        case POINTER_CHANGE_THIRDBUTTON_UP:
            mouse = WM_MBUTTONUP;
            break;
        case POINTER_CHANGE_FOURTHBUTTON_DOWN:
        case POINTER_CHANGE_FIFTHBUTTON_DOWN:
            mouse = WM_XBUTTONDOWN;
            break;
        case POINTER_CHANGE_FOURTHBUTTON_UP:
        case POINTER_CHANGE_FIFTHBUTTON_UP:
            mouse = WM_XBUTTONUP;
            break;
        default:
            break;
        }
        if (info.pointerFlags & POINTER_FLAG_FIRSTBUTTON) buttons |= MK_LBUTTON;
        if (info.pointerFlags & POINTER_FLAG_SECONDBUTTON) buttons |= MK_RBUTTON;
        if (info.pointerFlags & POINTER_FLAG_THIRDBUTTON) buttons |= MK_MBUTTON;
        if (mouse == WM_XBUTTONDOWN || mouse == WM_XBUTTONUP)
            buttons = MAKEWPARAM(buttons, info.ButtonChangeType == POINTER_CHANGE_FOURTHBUTTON_DOWN ||
                                                  info.ButtonChangeType == POINTER_CHANGE_FOURTHBUTTON_UP
                                              ? XBUTTON1
                                              : XBUTTON2);
        if (message.message == WM_POINTERDOWN) {
            dragTarget = target;
            dragPointer = info.pointerId;
        } else if (message.message == WM_POINTERUP || (info.pointerFlags & POINTER_FLAG_CANCELED)) {
            dragTarget = nullptr;
            dragPointer = 0;
        }
        ScreenToClient(target, &position);
        return PostMessageW(target, mouse, buttons, MAKELPARAM(position.x, position.y)) != FALSE;
    }
    const UINT wheel =
        message.message == WM_POINTERHWHEEL || message.message == WM_MOUSEHWHEEL ? WM_MOUSEHWHEEL : WM_MOUSEWHEEL;
    return PostMessageW(target, wheel, MAKEWPARAM(buttons, HIWORD(message.wParam)), message.lParam) != FALSE;
}
}

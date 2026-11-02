// NotificationsExtensions: Microsoft's C# tile and toast XML helper library,
// which Windows 8 games ship as a WinRT component hosted by Windows'
// CLRHost.dll, which needs the package to run. Stood in for its tile image
// templates, which Asphalt 7: Heat builds its live tile from at launch, and
// TileWideImageAndText02, which ilomilo plus builds after its intro; the
// offline TileUpdater discards the notification. Other templates fail with
// E_NOTIMPL. Interfaces declared from NotificationsExtensions.winmd (same ABI
// for object parameters as IInspectable*).
#include "registry.h"
#include "com/factory.h"
#include "log.h"
#include <windows.data.xml.dom.h>
#include <windows.ui.notifications.h>
#include <wrl.h>
#include <string>
using namespace Microsoft::WRL;
using ABI::Windows::Data::Xml::Dom::IXmlDocument;
using ABI::Windows::UI::Notifications::ITileNotification;
namespace appx {
namespace {
MIDL_INTERFACE("2C0D137A-1FC8-5DB6-4C9A-580221EEF1F4")
INotificationContent : public IInspectable {
    virtual HRESULT STDMETHODCALLTYPE GetContent(HSTRING * value) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetXml(IXmlDocument * *value) = 0;
};
MIDL_INTERFACE("02E42CE2-2E61-5393-6E49-F9CB367C34D5")
INotificationContentImage : public IInspectable {
    virtual HRESULT STDMETHODCALLTYPE get_Src(HSTRING * value) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_Src(HSTRING value) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_Alt(HSTRING * value) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_Alt(HSTRING value) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_AddImageQuery(boolean * value) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_AddImageQuery(boolean value) = 0;
};
MIDL_INTERFACE("7B093628-752B-5D3A-42C0-ABE72D42D323")
INotificationContentText : public IInspectable {
    virtual HRESULT STDMETHODCALLTYPE get_Text(HSTRING * value) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_Text(HSTRING value) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_Lang(HSTRING * value) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_Lang(HSTRING value) = 0;
};
MIDL_INTERFACE("4F5A7235-6BB2-553D-4000-4A58562B7072")
ITileNotificationContent : public IInspectable {
    virtual HRESULT STDMETHODCALLTYPE get_StrictValidation(boolean * value) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_StrictValidation(boolean value) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_Lang(HSTRING * value) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_Lang(HSTRING value) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_BaseUri(HSTRING * value) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_BaseUri(HSTRING value) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_Branding(INT32 * value) = 0; // TileBranding: None, Logo, Name
    virtual HRESULT STDMETHODCALLTYPE put_Branding(INT32 value) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_AddImageQuery(boolean * value) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_AddImageQuery(boolean value) = 0;
    virtual HRESULT STDMETHODCALLTYPE CreateNotification(ITileNotification * *value) = 0;
};
MIDL_INTERFACE("2537E592-94FF-5DCC-6279-0366C54998C5")
ISquareTileNotificationContent : public IInspectable{};
MIDL_INTERFACE("B803960C-2950-54E8-49C7-4B0033742AE4")
IWideTileNotificationContent : public IInspectable {
    virtual HRESULT STDMETHODCALLTYPE get_SquareContent(ISquareTileNotificationContent * *value) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_SquareContent(ISquareTileNotificationContent * value) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_RequireSquareContent(boolean * value) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_RequireSquareContent(boolean value) = 0;
};
MIDL_INTERFACE("BCAA22F7-9A02-5FBB-6094-E865CF95A0AF")
ITileSquareImage : public IInspectable {
    virtual HRESULT STDMETHODCALLTYPE get_Image(INotificationContentImage * *value) = 0;
};
MIDL_INTERFACE("4DCA931B-2D24-5435-78F2-49F4C6849A0D")
ITileWideImage : public IInspectable {
    virtual HRESULT STDMETHODCALLTYPE get_Image(INotificationContentImage * *value) = 0;
};
MIDL_INTERFACE("E20B8C46-7868-5903-7B2D-C80B95BFEFF1")
ITileWideImageAndText02 : public IInspectable {
    virtual HRESULT STDMETHODCALLTYPE get_Image(INotificationContentImage * *value) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_TextCaption1(INotificationContentText * *value) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_TextCaption2(INotificationContentText * *value) = 0;
};
// Every template, in ITileContentFactoryStatic's slot order.
#define APPX_TILE_TEMPLATES(X)                                                                                         \
    X(SquareBlock)                                                                                                     \
    X(SquareImage)                                                                                                     \
    X(SquarePeekImageAndText01)                                                                                        \
    X(SquarePeekImageAndText02)                                                                                        \
    X(SquarePeekImageAndText03)                                                                                        \
    X(SquarePeekImageAndText04)                                                                                        \
    X(SquareText01)                                                                                                    \
    X(SquareText02)                                                                                                    \
    X(SquareText03)                                                                                                    \
    X(SquareText04)                                                                                                    \
    X(WideBlockAndText01)                                                                                              \
    X(WideBlockAndText02)                                                                                              \
    X(WideImage)                                                                                                       \
    X(WideImageAndText01)                                                                                              \
    X(WideImageAndText02)                                                                                              \
    X(WideImageCollection)                                                                                             \
    X(WidePeekImage01)                                                                                                 \
    X(WidePeekImage02)                                                                                                 \
    X(WidePeekImage03)                                                                                                 \
    X(WidePeekImage04)                                                                                                 \
    X(WidePeekImage05)                                                                                                 \
    X(WidePeekImage06)                                                                                                 \
    X(WidePeekImageAndText01)                                                                                          \
    X(WidePeekImageAndText02)                                                                                          \
    X(WidePeekImageCollection01)                                                                                       \
    X(WidePeekImageCollection02)                                                                                       \
    X(WidePeekImageCollection03)                                                                                       \
    X(WidePeekImageCollection04)                                                                                       \
    X(WidePeekImageCollection05)                                                                                       \
    X(WidePeekImageCollection06)                                                                                       \
    X(WideSmallImageAndText01)                                                                                         \
    X(WideSmallImageAndText02)                                                                                         \
    X(WideSmallImageAndText03)                                                                                         \
    X(WideSmallImageAndText04)                                                                                         \
    X(WideSmallImageAndText05)                                                                                         \
    X(WideText01)                                                                                                      \
    X(WideText02)                                                                                                      \
    X(WideText03)                                                                                                      \
    X(WideText04)                                                                                                      \
    X(WideText05)                                                                                                      \
    X(WideText06)                                                                                                      \
    X(WideText07)                                                                                                      \
    X(WideText08)                                                                                                      \
    X(WideText09)                                                                                                      \
    X(WideText10)                                                                                                      \
    X(WideText11)
#define APPX_DECLARE_TEMPLATE(name) virtual HRESULT STDMETHODCALLTYPE CreateTile##name(IInspectable** value) = 0;
MIDL_INTERFACE("D77B1B1E-207F-536B-7D0A-91E9CDDB8CD5")
ITileContentFactoryStatic : public IInspectable{APPX_TILE_TEMPLATES(APPX_DECLARE_TEMPLATE)};
#undef APPX_DECLARE_TEMPLATE
// Private: a wide tile reads the <binding> of the square tile it carries.
struct __declspec(uuid("bd02df64-3a6f-4625-9761-bc33d4ff0ab7")) ITileBinding : public IInspectable {
    virtual std::wstring STDMETHODCALLTYPE Binding() = 0;
};

std::wstring Text(HSTRING value) {
    UINT32 length{};
    const auto* raw = WindowsGetStringRawBuffer(value, &length);
    return std::wstring(raw, length);
}
HRESULT CopyText(const std::wstring& text, HSTRING* value) {
    if (!value) return E_POINTER;
    return WindowsCreateString(text.data(), static_cast<UINT32>(text.size()), value);
}
HRESULT CopyFlag(bool flag, boolean* value) {
    if (!value) return E_POINTER;
    *value = flag;
    return S_OK;
}
std::wstring Escape(const std::wstring& value) {
    std::wstring out;
    for (auto c : value) {
        if (c == L'&')
            out += L"&amp;";
        else if (c == L'<')
            out += L"&lt;";
        else if (c == L'"')
            out += L"&quot;";
        else
            out += c;
    }
    return out;
}
std::wstring Attribute(const wchar_t* name, const std::wstring& value) {
    return std::wstring(L" ") + name + L"=\"" + Escape(value) + L"\"";
}

class Image final : public RuntimeClass<RuntimeClassFlags<WinRt>, INotificationContentImage, FtmBase> {
    InspectableClass(L"NotificationsExtensions.NotificationContentImage", BaseTrust);
    std::wstring src_, alt_;
    bool query_ = false;

public:
    HRESULT STDMETHODCALLTYPE get_Src(HSTRING* value) override { return CopyText(src_, value); }
    HRESULT STDMETHODCALLTYPE put_Src(HSTRING value) override {
        src_ = Text(value);
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_Alt(HSTRING* value) override { return CopyText(alt_, value); }
    HRESULT STDMETHODCALLTYPE put_Alt(HSTRING value) override {
        alt_ = Text(value);
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_AddImageQuery(boolean* value) override { return CopyFlag(query_, value); }
    HRESULT STDMETHODCALLTYPE put_AddImageQuery(boolean value) override {
        query_ = value;
        return S_OK;
    }
    std::wstring Element() const {
        std::wstring out = L"<image id=\"1\"" + Attribute(L"src", src_);
        if (!alt_.empty()) out += Attribute(L"alt", alt_);
        if (query_) out += L" addImageQuery=\"true\"";
        return out + L"/>";
    }
};

class TextField final : public RuntimeClass<RuntimeClassFlags<WinRt>, INotificationContentText, FtmBase> {
    InspectableClass(L"NotificationsExtensions.NotificationContentText", BaseTrust);
    std::wstring text_, lang_;

public:
    HRESULT STDMETHODCALLTYPE get_Text(HSTRING* value) override { return CopyText(text_, value); }
    HRESULT STDMETHODCALLTYPE put_Text(HSTRING value) override {
        text_ = Text(value);
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_Lang(HSTRING* value) override { return CopyText(lang_, value); }
    HRESULT STDMETHODCALLTYPE put_Lang(HSTRING value) override {
        lang_ = Text(value);
        return S_OK;
    }
    std::wstring Element(int id) const {
        std::wstring out = L"<text id=\"" + std::to_wstring(id) + L"\"";
        if (!lang_.empty()) out += Attribute(L"lang", lang_);
        return out + L">" + Escape(text_) + L"</text>";
    }
};

// Windows' own notification, which the offline TileUpdater accepts and
// discards. Wine has no XmlDocument, so there Content is null.
class Notification final : public RuntimeClass<RuntimeClassFlags<WinRt>, ITileNotification, FtmBase> {
    InspectableClass(L"Windows.UI.Notifications.TileNotification", BaseTrust);
    ComPtr<IXmlDocument> content_;
    ComPtr<__FIReference_1_Windows__CFoundation__CDateTime> expiration_;
    Wrappers::HString tag_;

public:
    explicit Notification(IXmlDocument* content) : content_(content) {}
    HRESULT STDMETHODCALLTYPE get_Content(IXmlDocument** value) override {
        if (!value) return E_POINTER;
        return content_.CopyTo(value);
    }
    HRESULT STDMETHODCALLTYPE put_ExpirationTime(__FIReference_1_Windows__CFoundation__CDateTime* value) override {
        expiration_ = value;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_ExpirationTime(__FIReference_1_Windows__CFoundation__CDateTime** value) override {
        if (!value) return E_POINTER;
        return expiration_.CopyTo(value);
    }
    HRESULT STDMETHODCALLTYPE put_Tag(HSTRING value) override { return tag_.Set(value); }
    HRESULT STDMETHODCALLTYPE get_Tag(HSTRING* value) override {
        if (!value) return E_POINTER;
        return tag_.CopyTo(value);
    }
};

// A template led by one image: TileSquareImage, TileWideImage or
// TileWideImageAndText02. Wide ones also carry the square tile shown when the
// user makes the tile small.
template <class Template, class Shape>
class ImageTile : public RuntimeClass<RuntimeClassFlags<WinRt>, Template, ITileNotificationContent, Shape,
                                      INotificationContent, ITileBinding, FtmBase> {
    ComPtr<Image> image_ = Make<Image>();
    std::wstring lang_, baseUri_;
    INT32 branding_ = 1; // TileBranding.Logo, the tile's default
    bool strict_ = true, query_ = false;

protected:
    virtual const wchar_t* Name() const = 0;
    virtual std::wstring Bindings() { return Binding(); }
    virtual std::wstring Texts() { return {}; }

public:
    std::wstring STDMETHODCALLTYPE Binding() override {
        return std::wstring(L"<binding") + Attribute(L"template", Name()) + L">" + image_->Element() + Texts() +
               L"</binding>";
    }
    HRESULT STDMETHODCALLTYPE get_Image(INotificationContentImage** value) override {
        if (!value) return E_POINTER;
        return image_.CopyTo(value);
    }
    HRESULT STDMETHODCALLTYPE get_StrictValidation(boolean* value) override { return CopyFlag(strict_, value); }
    HRESULT STDMETHODCALLTYPE put_StrictValidation(boolean value) override {
        strict_ = value;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_Lang(HSTRING* value) override { return CopyText(lang_, value); }
    HRESULT STDMETHODCALLTYPE put_Lang(HSTRING value) override {
        lang_ = Text(value);
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_BaseUri(HSTRING* value) override { return CopyText(baseUri_, value); }
    HRESULT STDMETHODCALLTYPE put_BaseUri(HSTRING value) override {
        baseUri_ = Text(value);
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_Branding(INT32* value) override {
        if (!value) return E_POINTER;
        *value = branding_;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE put_Branding(INT32 value) override {
        if (value < 0 || value > 2) return E_INVALIDARG;
        branding_ = value;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_AddImageQuery(boolean* value) override { return CopyFlag(query_, value); }
    HRESULT STDMETHODCALLTYPE put_AddImageQuery(boolean value) override {
        query_ = value;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetContent(HSTRING* value) override {
        std::wstring visual = L"<visual version=\"1\"";
        if (!lang_.empty()) visual += Attribute(L"lang", lang_);
        if (!baseUri_.empty()) visual += Attribute(L"baseUri", baseUri_);
        if (branding_ != 1) visual += branding_ ? L" branding=\"name\"" : L" branding=\"none\"";
        if (query_) visual += L" addImageQuery=\"true\"";
        return CopyText(L"<tile>" + visual + L">" + Bindings() + L"</visual></tile>", value);
    }
    HRESULT STDMETHODCALLTYPE GetXml(IXmlDocument** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        Wrappers::HString xml;
        ComPtr<IInspectable> instance;
        ComPtr<ABI::Windows::Data::Xml::Dom::IXmlDocumentIO> io;
        auto hr = GetContent(xml.GetAddressOf());
        if (SUCCEEDED(hr))
            hr = OsActivateInstance(Wrappers::HStringReference(L"Windows.Data.Xml.Dom.XmlDocument").Get(), &instance);
        if (SUCCEEDED(hr)) hr = instance.As(&io);
        if (SUCCEEDED(hr)) hr = io->LoadXml(xml.Get());
        return SUCCEEDED(hr) ? instance.CopyTo(value) : hr;
    }
    HRESULT STDMETHODCALLTYPE CreateNotification(ITileNotification** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        ComPtr<IXmlDocument> content;
        const auto hr = GetXml(&content);
        if (FAILED(hr)) Log("TRACE", "NotificationsExtensions: tile without XML content hr=%08lx", hr);
        auto notification = Make<Notification>(content.Get());
        return notification ? notification.CopyTo(value) : E_OUTOFMEMORY;
    }
};
class SquareImageTile final : public ImageTile<ITileSquareImage, ISquareTileNotificationContent> {
    InspectableClass(L"NotificationsExtensions.TileContent.TileSquareImage", BaseTrust);

protected:
    const wchar_t* Name() const override { return L"TileSquareImage"; }
};
template <class Template> class WideTile : public ImageTile<Template, IWideTileNotificationContent> {
    ComPtr<ISquareTileNotificationContent> square_;
    bool requireSquare_ = true;

protected:
    std::wstring Bindings() override {
        ComPtr<ITileBinding> square;
        auto bindings = this->Binding();
        if (square_ && SUCCEEDED(square_.As(&square))) bindings += square->Binding();
        return bindings;
    }

public:
    HRESULT STDMETHODCALLTYPE get_SquareContent(ISquareTileNotificationContent** value) override {
        if (!value) return E_POINTER;
        return square_.CopyTo(value);
    }
    HRESULT STDMETHODCALLTYPE put_SquareContent(ISquareTileNotificationContent* value) override {
        square_ = value;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_RequireSquareContent(boolean* value) override {
        return CopyFlag(requireSquare_, value);
    }
    HRESULT STDMETHODCALLTYPE put_RequireSquareContent(boolean value) override {
        requireSquare_ = value;
        return S_OK;
    }
};
class WideImageTile final : public WideTile<ITileWideImage> {
    InspectableClass(L"NotificationsExtensions.TileContent.TileWideImage", BaseTrust);

protected:
    const wchar_t* Name() const override { return L"TileWideImage"; }
};
class WideImageAndText02Tile final : public WideTile<ITileWideImageAndText02> {
    InspectableClass(L"NotificationsExtensions.TileContent.TileWideImageAndText02", BaseTrust);
    ComPtr<TextField> caption1_ = Make<TextField>(), caption2_ = Make<TextField>();

protected:
    const wchar_t* Name() const override { return L"TileWideImageAndText02"; }
    std::wstring Texts() override { return caption1_->Element(1) + caption2_->Element(2); }

public:
    HRESULT STDMETHODCALLTYPE get_TextCaption1(INotificationContentText** value) override {
        if (!value) return E_POINTER;
        return caption1_.CopyTo(value);
    }
    HRESULT STDMETHODCALLTYPE get_TextCaption2(INotificationContentText** value) override {
        if (!value) return E_POINTER;
        return caption2_.CopyTo(value);
    }
};

class TileContentFactory final : public Factory<ITileContentFactoryStatic> {
    template <class T> static HRESULT Create(IInspectable** value) {
        auto tile = Make<T>();
        return tile ? tile.CopyTo(value) : E_OUTOFMEMORY;
    }
    static HRESULT Create(const wchar_t* name, IInspectable** value) {
        if (!value) return E_POINTER;
        *value = nullptr;
        if (wcscmp(name, L"SquareImage") == 0) return Create<SquareImageTile>(value);
        if (wcscmp(name, L"WideImage") == 0) return Create<WideImageTile>(value);
        if (wcscmp(name, L"WideImageAndText02") == 0) return Create<WideImageAndText02Tile>(value);
        Log("UNIMPL", "NotificationsExtensions TileContentFactory.CreateTile%ls", name);
        return E_NOTIMPL;
    }

public:
    TileContentFactory() : Factory(L"NotificationsExtensions.TileContent.TileContentFactory") {}
#define APPX_CREATE_TEMPLATE(name)                                                                                     \
    HRESULT STDMETHODCALLTYPE CreateTile##name(IInspectable** value) override { return Create(L#name, value); }
    APPX_TILE_TEMPLATES(APPX_CREATE_TEMPLATE)
#undef APPX_CREATE_TEMPLATE
};
}
HRESULT MakeTileContentFactory(IActivationFactory** value) {
    auto factory = Make<TileContentFactory>();
    return factory ? factory.CopyTo(value) : E_OUTOFMEMORY;
}
const ShimClass tileContentClass{L"NotificationsExtensions.TileContent.TileContentFactory", MakeTileContentFactory,
                                 ShimPolicy::Replace, InIslands};
}

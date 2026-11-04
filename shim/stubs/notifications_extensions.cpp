// NotificationsExtensions: Microsoft's C# tile and toast XML helper library,
// which Windows 8 games ship as a WinRT component hosted by Windows'
// CLRHost.dll, which needs the package to run. Stood in for its Windows 8 tile
// templates, all 45 of them: Asphalt 7: Heat builds its live tile from
// TileWideImage and TileSquareImage at launch, ilomilo plus from
// TileWideImageAndText02 and TileSquareText04. The offline TileUpdater
// discards the notification. Interfaces declared from
// NotificationsExtensions.winmd (same ABI for object parameters as
// IInspectable*).
#include "registry.h"
#include "com/factory.h"
#include "log.h"
#include <windows.data.xml.dom.h>
#include <windows.ui.notifications.h>
#include <wrl.h>
#include <string>
#include <vector>
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
// The interface the factory returns for each template: the getters of its
// fields, from slot 6. Taken from the Windows 8.1 NotificationsExtensions.winmd,
// whose TileSquare150x150Text04 is Windows 8's TileSquareText04 renamed, and so
// on. No template has more than ten fields. Windows 8's IIDs for these are only
// in the NotificationsExtensions.winmd games ship; the three known are in
// knownTemplates, and a tile logs any other interface a game asks it for.
// TileWidePeekImageCollection01-06 also carry TileWideImageCollection's five
// images, reached only through that interface's IID, which is not known.
struct __declspec(uuid("e3ace2bb-8796-4166-8f80-4d6a6e22fc82")) ITileTemplate : public IInspectable {
    virtual HRESULT STDMETHODCALLTYPE Field0(IInspectable** value) = 0;
    virtual HRESULT STDMETHODCALLTYPE Field1(IInspectable** value) = 0;
    virtual HRESULT STDMETHODCALLTYPE Field2(IInspectable** value) = 0;
    virtual HRESULT STDMETHODCALLTYPE Field3(IInspectable** value) = 0;
    virtual HRESULT STDMETHODCALLTYPE Field4(IInspectable** value) = 0;
    virtual HRESULT STDMETHODCALLTYPE Field5(IInspectable** value) = 0;
    virtual HRESULT STDMETHODCALLTYPE Field6(IInspectable** value) = 0;
    virtual HRESULT STDMETHODCALLTYPE Field7(IInspectable** value) = 0;
    virtual HRESULT STDMETHODCALLTYPE Field8(IInspectable** value) = 0;
    virtual HRESULT STDMETHODCALLTYPE Field9(IInspectable** value) = 0;
};
// Every template, in ITileContentFactoryStatic's slot order, with its fields
// in its interface's order: I an image, T a text.
#define APPX_TILE_TEMPLATES(X)                                                                                         \
    X(SquareBlock, "TT")                                                                                               \
    X(SquareImage, "I")                                                                                                \
    X(SquarePeekImageAndText01, "ITTTT")                                                                               \
    X(SquarePeekImageAndText02, "ITT")                                                                                 \
    X(SquarePeekImageAndText03, "ITTTT")                                                                               \
    X(SquarePeekImageAndText04, "IT")                                                                                  \
    X(SquareText01, "TTTT")                                                                                            \
    X(SquareText02, "TT")                                                                                              \
    X(SquareText03, "TTTT")                                                                                            \
    X(SquareText04, "T")                                                                                               \
    X(WideBlockAndText01, "TTTTTT")                                                                                    \
    X(WideBlockAndText02, "TTT")                                                                                       \
    X(WideImage, "I")                                                                                                  \
    X(WideImageAndText01, "IT")                                                                                        \
    X(WideImageAndText02, "ITT")                                                                                       \
    X(WideImageCollection, "IIIII")                                                                                    \
    X(WidePeekImage01, "ITT")                                                                                          \
    X(WidePeekImage02, "ITTTTT")                                                                                       \
    X(WidePeekImage03, "IT")                                                                                           \
    X(WidePeekImage04, "IT")                                                                                           \
    X(WidePeekImage05, "IITT")                                                                                         \
    X(WidePeekImage06, "IIT")                                                                                          \
    X(WidePeekImageAndText01, "IT")                                                                                    \
    X(WidePeekImageAndText02, "ITTTTT")                                                                                \
    X(WidePeekImageCollection01, "TT")                                                                                 \
    X(WidePeekImageCollection02, "TTTTT")                                                                              \
    X(WidePeekImageCollection03, "T")                                                                                  \
    X(WidePeekImageCollection04, "T")                                                                                  \
    X(WidePeekImageCollection05, "ITT")                                                                                \
    X(WidePeekImageCollection06, "IT")                                                                                 \
    X(WideSmallImageAndText01, "IT")                                                                                   \
    X(WideSmallImageAndText02, "ITTTTT")                                                                               \
    X(WideSmallImageAndText03, "IT")                                                                                   \
    X(WideSmallImageAndText04, "ITT")                                                                                  \
    X(WideSmallImageAndText05, "ITT")                                                                                  \
    X(WideText01, "TTTTT")                                                                                             \
    X(WideText02, "TTTTTTTTT")                                                                                         \
    X(WideText03, "T")                                                                                                 \
    X(WideText04, "T")                                                                                                 \
    X(WideText05, "TTTTT")                                                                                             \
    X(WideText06, "TTTTTTTTTT")                                                                                        \
    X(WideText07, "TTTTTTTTT")                                                                                         \
    X(WideText08, "TTTTTTTTTT")                                                                                        \
    X(WideText09, "TT")                                                                                                \
    X(WideText10, "TTTTTTTTT")                                                                                         \
    X(WideText11, "TTTTTTTTTT")
#define APPX_DECLARE_TEMPLATE(name, fields)                                                                            \
    virtual HRESULT STDMETHODCALLTYPE CreateTile##name(IInspectable** value) = 0;
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
    std::wstring Element(int id) const {
        std::wstring out = L"<image id=\"" + std::to_wstring(id) + L"\"" + Attribute(L"src", src_);
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

struct TemplateInfo {
    const wchar_t* name; // after "Tile"
    const char* fields;
};
#define APPX_TEMPLATE_INFO(name, fields) {L#name, fields},
constexpr TemplateInfo templates[] = {APPX_TILE_TEMPLATES(APPX_TEMPLATE_INFO)};
#undef APPX_TEMPLATE_INFO
#define APPX_TEMPLATE_INDEX(name, fields) Template##name,
enum TemplateIndex { APPX_TILE_TEMPLATES(APPX_TEMPLATE_INDEX) };
#undef APPX_TEMPLATE_INDEX
// Windows 8 IIDs known for template interfaces (Asphalt 7: Heat, ilomilo plus).
struct KnownTemplate {
    TemplateIndex index;
    GUID iid;
};
constexpr KnownTemplate knownTemplates[] = {
    {TemplateSquareImage, {0xbcaa22f7, 0x9a02, 0x5fbb, {0x60, 0x94, 0xe8, 0x65, 0xcf, 0x95, 0xa0, 0xaf}}},
    {TemplateWideImage, {0x4dca931b, 0x2d24, 0x5435, {0x78, 0xf2, 0x49, 0xf4, 0xc6, 0x84, 0x9a, 0x0d}}},
    {TemplateWideImageAndText02, {0xe20b8c46, 0x7868, 0x5903, {0x7b, 0x2d, 0xc8, 0x0b, 0x95, 0xbf, 0xef, 0xf1}}},
};

// A tile template: its fields, and the notification XML they make. Wide tiles
// also carry the square tile shown when the user makes the tile small.
template <class Shape>
class Tile : public RuntimeClass<RuntimeClassFlags<WinRt>, ITileTemplate, ITileNotificationContent, Shape,
                                 INotificationContent, ITileBinding, FtmBase> {
    using Base = RuntimeClass<RuntimeClassFlags<WinRt>, ITileTemplate, ITileNotificationContent, Shape,
                              INotificationContent, ITileBinding, FtmBase>;
    const TemplateIndex index_;
    std::vector<ComPtr<Image>> images_;
    std::vector<ComPtr<TextField>> texts_;
    std::vector<ComPtr<IInspectable>> fields_; // in the interface's order
    std::wstring lang_, baseUri_;
    INT32 branding_ = 1; // TileBranding.Logo, the tile's default
    bool strict_ = true, query_ = false;
    HRESULT Field(size_t index, IInspectable** value) {
        if (!value) return E_POINTER;
        *value = nullptr;
        return index < fields_.size() ? fields_[index].CopyTo(value) : E_NOTIMPL;
    }

protected:
    virtual std::wstring Bindings() { return Binding(); }

public:
    explicit Tile(TemplateIndex index) : index_(index) {
        for (const char* kind = templates[index].fields; *kind; ++kind) {
            if (*kind == 'I') {
                images_.push_back(Make<Image>());
                fields_.emplace_back(static_cast<INotificationContentImage*>(images_.back().Get()));
            } else {
                texts_.push_back(Make<TextField>());
                fields_.emplace_back(static_cast<INotificationContentText*>(texts_.back().Get()));
            }
        }
    }
    bool Ready() const {
        for (const auto& field : fields_)
            if (!field) return false;
        return true;
    }
    // Answers the template's interface by a known Windows 8 IID, and logs what
    // a game asks of a tile that this one lacks.
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** value) noexcept override {
        for (const auto& known : knownTemplates) {
            if (known.index != index_ || iid != known.iid || !value) continue;
            *value = static_cast<ITileTemplate*>(this);
            static_cast<ITileTemplate*>(this)->AddRef();
            return S_OK;
        }
        auto hr = Base::QueryInterface(iid, value);
        if (FAILED(hr))
            Log("FAIL", "NotificationsExtensions Tile%ls QI missing %08lx-%04x-%04x-%02x%02x-%02x%02x%02x%02x%02x%02x",
                templates[index_].name, iid.Data1, iid.Data2, iid.Data3, iid.Data4[0], iid.Data4[1], iid.Data4[2],
                iid.Data4[3], iid.Data4[4], iid.Data4[5], iid.Data4[6], iid.Data4[7]);
        return hr;
    }
    HRESULT STDMETHODCALLTYPE GetRuntimeClassName(HSTRING* value) noexcept override {
        return CopyText(std::wstring(L"NotificationsExtensions.TileContent.Tile") + templates[index_].name, value);
    }
    HRESULT STDMETHODCALLTYPE GetTrustLevel(TrustLevel* value) noexcept override {
        if (!value) return E_POINTER;
        *value = BaseTrust;
        return S_OK;
    }
#define APPX_FIELD(n)                                                                                                  \
    HRESULT STDMETHODCALLTYPE Field##n(IInspectable** value) override { return Field(n, value); }
    APPX_FIELD(0)
    APPX_FIELD(1)
    APPX_FIELD(2)
    APPX_FIELD(3)
    APPX_FIELD(4)
    APPX_FIELD(5)
    APPX_FIELD(6)
    APPX_FIELD(7)
    APPX_FIELD(8)
    APPX_FIELD(9)
#undef APPX_FIELD
    std::wstring STDMETHODCALLTYPE Binding() override {
        std::wstring out =
            std::wstring(L"<binding") + Attribute(L"template", std::wstring(L"Tile") + templates[index_].name) + L">";
        for (size_t i = 0; i < images_.size(); ++i) out += images_[i]->Element(static_cast<int>(i + 1));
        for (size_t i = 0; i < texts_.size(); ++i) out += texts_[i]->Element(static_cast<int>(i + 1));
        return out + L"</binding>";
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
class SquareTile final : public Tile<ISquareTileNotificationContent> {
public:
    explicit SquareTile(TemplateIndex index) : Tile(index) {}
};
class WideTile final : public Tile<IWideTileNotificationContent> {
    ComPtr<ISquareTileNotificationContent> square_;
    bool requireSquare_ = true;

protected:
    std::wstring Bindings() override {
        ComPtr<ITileBinding> square;
        auto bindings = Binding();
        if (square_ && SUCCEEDED(square_.As(&square))) bindings += square->Binding();
        return bindings;
    }

public:
    explicit WideTile(TemplateIndex index) : Tile(index) {}
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

class TileContentFactory final : public Factory<ITileContentFactoryStatic> {
    template <class T> static HRESULT Create(TemplateIndex index, IInspectable** value) {
        auto tile = Make<T>(index);
        if (!tile || !tile->Ready()) return E_OUTOFMEMORY;
        *value = static_cast<ITileTemplate*>(tile.Detach()); // the template's interface, as Windows returns
        return S_OK;
    }
    static HRESULT Create(TemplateIndex index, IInspectable** value) {
        if (!value) return E_POINTER;
        *value = nullptr;
        const wchar_t* name = templates[index].name;
        Log("TRACE", "NotificationsExtensions TileContentFactory.CreateTile%ls", name);
        return wcsncmp(name, L"Wide", 4) == 0 ? Create<WideTile>(index, value) : Create<SquareTile>(index, value);
    }

public:
    TileContentFactory() : Factory(L"NotificationsExtensions.TileContent.TileContentFactory") {}
#define APPX_CREATE_TEMPLATE(name, fields)                                                                             \
    HRESULT STDMETHODCALLTYPE CreateTile##name(IInspectable** value) override { return Create(Template##name, value); }
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

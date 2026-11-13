// Independent native ABI contract for the generated synthetic WinMD.
#include <windows.h>
#include <roapi.h>
#include <wrl.h>
#include <wrl/wrappers/corewrappers.h>
#include <vector>
using namespace Microsoft::WRL;
using Microsoft::WRL::Wrappers::HString;
MIDL_INTERFACE("723d9b38-bb73-463e-adc8-25b46f56121a") IChanged : IUnknown {
    virtual HRESULT STDMETHODCALLTYPE Invoke(INT32, BYTE, HSTRING, HSTRING*) = 0;
};
MIDL_INTERFACE("239635fc-88f0-4fd6-80b1-b21ac63c939a") INode : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE Read(INT32*) = 0;
};
MIDL_INTERFACE("45684efa-c695-4027-8fc4-51bf12a079ac") IExternalChanged : IUnknown {
    virtual HRESULT STDMETHODCALLTYPE Invoke(INode*) = 0;
};
class Node final : public RuntimeClass<RuntimeClassFlags<WinRt>, INode> {
    InspectableClass(L"Example.External.Node", BaseTrust);

public:
    HRESULT STDMETHODCALLTYPE Read(INT32* result) override {
        *result = 73;
        return S_OK;
    }
};
class NodeFactory final : public ActivationFactory<Implements<IActivationFactory>> {
public:
    HRESULT STDMETHODCALLTYPE ActivateInstance(IInspectable** value) noexcept override {
        return Make<Node>().CopyTo(value);
    }
};
MIDL_INTERFACE("2fc62e47-e1ce-471d-9098-014b39ca938e") IEngine : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE Echo(INT32, INT32*) = 0;
    virtual HRESULT STDMETHODCALLTYPE SetState(BYTE, DOUBLE, UINT32) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetState(DOUBLE*) = 0;
    virtual HRESULT STDMETHODCALLTYPE EchoText(HSTRING, HSTRING*) = 0;
    virtual HRESULT STDMETHODCALLTYPE SumBytes(UINT32, const BYTE*, UINT32*) = 0;
    virtual HRESULT STDMETHODCALLTYPE Add(IChanged*, INT64*) = 0;
    virtual HRESULT STDMETHODCALLTYPE Remove(INT64) = 0;
    virtual HRESULT STDMETHODCALLTYPE Raise(INT32, BYTE, HSTRING, HSTRING*) = 0;
    virtual HRESULT STDMETHODCALLTYPE Fail() = 0;
    virtual HRESULT STDMETHODCALLTYPE SetNode(INode*) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetNode(INode**) = 0;
    virtual HRESULT STDMETHODCALLTYPE NextMode(INT32, INT32*) = 0;
    virtual HRESULT STDMETHODCALLTYPE AddExternal(IExternalChanged*, INT64*) = 0;
    virtual HRESULT STDMETHODCALLTYPE RemoveExternal(INT64) = 0;
    virtual HRESULT STDMETHODCALLTYPE RaiseExternal() = 0;
};
class Engine final : public RuntimeClass<RuntimeClassFlags<WinRt>, IEngine> {
    InspectableClass(L"Example.Component.Engine", BaseTrust);
    ComPtr<INode> node_;
    ComPtr<IExternalChanged> external_;
    DOUBLE state_ = 0;
    INT64 token_ = 0;
    std::vector<std::pair<INT64, ComPtr<IChanged>>> handlers_;

public:
    HRESULT STDMETHODCALLTYPE Echo(INT32 value, INT32* result) override {
        *result = value;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE SetState(BYTE active, DOUBLE value, UINT32 count) override {
        if (active > 1) return E_INVALIDARG;
        state_ = active ? value + count : -value;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetState(DOUBLE* result) override {
        *result = state_;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE EchoText(HSTRING value, HSTRING* result) override {
        return WindowsDuplicateString(value, result);
    }
    HRESULT STDMETHODCALLTYPE SumBytes(UINT32 count, const BYTE* value, UINT32* result) override {
        if (count && !value) return E_POINTER;
        *result = 0;
        for (UINT32 i = 0; i < count; ++i) *result += value[i];
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE Add(IChanged* handler, INT64* token) override {
        if (!handler) return E_POINTER;
        *token = ++token_;
        handlers_.emplace_back(*token, handler);
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE Remove(INT64 token) override {
        for (auto i = handlers_.begin(); i != handlers_.end(); ++i)
            if (i->first == token) {
                handlers_.erase(i);
                return S_OK;
            }
        return E_INVALIDARG;
    }
    HRESULT STDMETHODCALLTYPE Raise(INT32 count, BYTE active, HSTRING name, HSTRING* result) override {
        *result = nullptr;
        auto snapshot = handlers_;
        for (auto& handler : snapshot) {
            HString current;
            auto hr = handler.second->Invoke(count, active, name, current.GetAddressOf());
            if (FAILED(hr)) {
                WindowsDeleteString(*result);
                *result = nullptr;
                return hr;
            }
            WindowsDeleteString(*result);
            *result = current.Detach();
        }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE Fail() override { return E_INVALIDARG; }
    HRESULT STDMETHODCALLTYPE SetNode(INode* node) override {
        // Validate the independently declared IID rather than accepting a layout cast.
        node_.Reset();
        return node ? node->QueryInterface(IID_PPV_ARGS(&node_)) : S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetNode(INode** result) override { return node_.CopyTo(result); }
    HRESULT STDMETHODCALLTYPE NextMode(INT32 mode, INT32* result) override {
        if (mode != 7) return E_INVALIDARG;
        *result = 19;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE AddExternal(IExternalChanged* handler, INT64* token) override {
        external_ = handler;
        *token = 9001;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE RemoveExternal(INT64 token) override {
        if (token != 9001) return E_INVALIDARG;
        external_.Reset();
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE RaiseExternal() override { return external_ ? external_->Invoke(node_.Get()) : S_OK; }
};
// Like a C++/CX engine creating a Platform::Exception in its constructor: the
MIDL_INTERFACE("8c1f2a90-6d3e-4b57-9e21-3a4c5d6e7f80") IGauge : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE get_Name(HSTRING*) = 0;
};
MIDL_INTERFACE("5b2e7c41-9a0d-4f3b-8c62-1d4e5f6a7b8c") IGaugeFactory : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE Create(HSTRING, IGauge**) = 0;
};
class Gauge final : public RuntimeClass<RuntimeClassFlags<WinRt>, IGauge> {
    InspectableClass(L"Example.Component.Gauge", BaseTrust);
    HString name_;

public:
    explicit Gauge(HSTRING name) { name_.Set(name); }
    HRESULT STDMETHODCALLTYPE get_Name(HSTRING* value) override { return name_.CopyTo(value); }
};
class GaugeFactory final : public ActivationFactory<Implements<IActivationFactory>, IGaugeFactory> {
public:
    HRESULT STDMETHODCALLTYPE ActivateInstance(IInspectable** value) noexcept override {
        *value = nullptr;
        return E_NOTIMPL;
    }
    HRESULT STDMETHODCALLTYPE Create(HSTRING name, IGauge** value) override { return Make<Gauge>(name).CopyTo(value); }
};
// IVector<HSTRING>, as a C++/CX Platform::Collections::Vector<String^> passes it.
MIDL_INTERFACE("98b9acc1-4b56-532e-ac73-03d5291cca90") IStringVector : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE GetAt(UINT32, HSTRING*) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_Size(UINT32*) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetView(IInspectable**) = 0;
    virtual HRESULT STDMETHODCALLTYPE IndexOf(HSTRING, UINT32*, BOOLEAN*) = 0;
    virtual HRESULT STDMETHODCALLTYPE SetAt(UINT32, HSTRING) = 0;
    virtual HRESULT STDMETHODCALLTYPE InsertAt(UINT32, HSTRING) = 0;
    virtual HRESULT STDMETHODCALLTYPE RemoveAt(UINT32) = 0;
    virtual HRESULT STDMETHODCALLTYPE Append(HSTRING) = 0;
    virtual HRESULT STDMETHODCALLTYPE RemoveAtEnd() = 0;
    virtual HRESULT STDMETHODCALLTYPE Clear() = 0;
    virtual HRESULT STDMETHODCALLTYPE GetMany(UINT32, UINT32, HSTRING*, UINT32*) = 0;
    virtual HRESULT STDMETHODCALLTYPE ReplaceAll(UINT32, HSTRING*) = 0;
};
class StringVector final : public RuntimeClass<RuntimeClassFlags<WinRt>, IStringVector> {
    InspectableClass(L"Windows.Foundation.Collections.IVector`1<String>", BaseTrust);
    std::vector<HString> items_;

public:
    StringVector(std::initializer_list<const wchar_t*> items) {
        for (auto item : items) {
            items_.emplace_back();
            items_.back().Set(item);
        }
    }
    HRESULT STDMETHODCALLTYPE GetAt(UINT32 index, HSTRING* value) override {
        if (index >= items_.size()) return E_BOUNDS;
        return items_[index].CopyTo(value);
    }
    HRESULT STDMETHODCALLTYPE get_Size(UINT32* value) override {
        *value = static_cast<UINT32>(items_.size());
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetView(IInspectable**) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE IndexOf(HSTRING, UINT32*, BOOLEAN*) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE SetAt(UINT32, HSTRING) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE InsertAt(UINT32, HSTRING) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE RemoveAt(UINT32) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE Append(HSTRING) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE RemoveAtEnd() override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE Clear() override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE GetMany(UINT32, UINT32, HSTRING*, UINT32*) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE ReplaceAll(UINT32, HSTRING*) override { return E_NOTIMPL; }
};
// Implemented in managed code by the consumer, like EngineWrapper.IArticleRenderer.
MIDL_INTERFACE("6a1d3f52-7c0e-4b8a-9f21-5e4d3c2b1a09") IRenderer : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE Begin(BYTE, INT32, HSTRING) = 0;
    virtual HRESULT STDMETHODCALLTYPE Words(IStringVector*) = 0;
    virtual HRESULT STDMETHODCALLTYPE Unsupported(IChanged*) = 0;
    virtual HRESULT STDMETHODCALLTYPE Scale(UINT32, DOUBLE, DOUBLE*) = 0;
    virtual HRESULT STDMETHODCALLTYPE Result(HSTRING*) = 0;
    virtual HRESULT STDMETHODCALLTYPE Sum(UINT32, const BYTE*, UINT32*) = 0;
};
MIDL_INTERFACE("0f3b8d27-4e61-4c9a-b5d2-7a8e9c1f2d36") IPrinter : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE Render(IRenderer*, HSTRING*) = 0;
    virtual HRESULT STDMETHODCALLTYPE Keep(IRenderer*) = 0;
    virtual HRESULT STDMETHODCALLTYPE RenderKept(HSTRING*) = 0;
};
// Windows.Foundation.IClosable, declared here like the other independent contracts.
MIDL_INTERFACE("30d5a829-7fa4-4026-83bb-d75bae4ea99e") IPrinterClosable : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE Close() = 0;
};
class Printer final : public RuntimeClass<RuntimeClassFlags<WinRt>, IPrinter, IPrinterClosable> {
    InspectableClass(L"Example.Component.Printer", BaseTrust);
    ComPtr<IRenderer> kept_;
    bool closed_ = false;

public:
    HRESULT STDMETHODCALLTYPE Close() override {
        closed_ = true;
        kept_.Reset();
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE Render(IRenderer* value, HSTRING* result) override {
        *result = nullptr;
        // Validate the independently declared IID rather than accepting a layout cast.
        ComPtr<IRenderer> renderer;
        if (!value) return E_POINTER;
        auto hr = value->QueryInterface(IID_PPV_ARGS(&renderer));
        if (FAILED(hr)) return hr;
        static const wchar_t label[] = L"đá\0\xD83D\xDC8E";
        HString text;
        hr = text.Set(label, ARRAYSIZE(label) - 1);
        if (SUCCEEDED(hr)) hr = renderer->Begin(1, -42, text.Get());
        if (SUCCEEDED(hr)) hr = renderer->Words(Make<StringVector>(std::initializer_list<const wchar_t*>{L"alpha", L"βeta"}).Get());
        if (FAILED(hr)) return hr;
        // An argument the projection cannot convert answers E_NOTIMPL without shifting later slots.
        if (renderer->Unsupported(nullptr) != E_NOTIMPL) return E_UNEXPECTED;
        DOUBLE scaled = 0;
        if (renderer->Scale(0, 1.5, &scaled) != E_INVALIDARG) return E_UNEXPECTED;
        hr = renderer->Scale(3, 1.5, &scaled);
        if (FAILED(hr)) return hr;
        if (scaled != 4.5) return E_UNEXPECTED;
        static const BYTE bytes[] = {1, 2, 250};
        UINT32 sum = 0;
        hr = renderer->Sum(ARRAYSIZE(bytes), bytes, &sum);
        if (FAILED(hr)) return hr;
        if (sum != 253) return E_UNEXPECTED;
        return renderer->Result(result);
    }
    HRESULT STDMETHODCALLTYPE Keep(IRenderer* value) override {
        kept_.Reset();
        return value ? value->QueryInterface(IID_PPV_ARGS(&kept_)) : S_OK;
    }
    HRESULT STDMETHODCALLTYPE RenderKept(HSTRING* result) override {
        *result = nullptr;
        if (closed_) return RO_E_CLOSED;
        if (!kept_) return E_ILLEGAL_METHOD_CALL;
        HString text;
        auto hr = text.Set(L"kept");
        if (SUCCEEDED(hr)) hr = kept_->Begin(0, 7, text.Get());
        return FAILED(hr) ? hr : kept_->Result(result);
    }
};
class PrinterFactory final : public ActivationFactory<Implements<IActivationFactory>> {
public:
    HRESULT STDMETHODCALLTYPE ActivateInstance(IInspectable** value) noexcept override {
        return Make<Printer>().CopyTo(value);
    }
};
// call goes through a dependency the component, not the game, loaded.
extern "C" HRESULT __cdecl ReportComponentFailure();
class EngineFactory final : public ActivationFactory<Implements<IActivationFactory>> {
public:
    HRESULT STDMETHODCALLTYPE ActivateInstance(IInspectable** value) noexcept override {
        auto hr = ReportComponentFailure();
        return FAILED(hr) ? hr : Make<Engine>().CopyTo(value);
    }
};
extern "C" HRESULT WINAPI DllGetActivationFactory(HSTRING name, IActivationFactory** factory) {
    *factory = nullptr;
    if (wcscmp(WindowsGetStringRawBuffer(name, nullptr), L"Example.External.Node") == 0)
        return Make<NodeFactory>().CopyTo(factory);
    if (wcscmp(WindowsGetStringRawBuffer(name, nullptr), L"Example.Component.Gauge") == 0)
        return Make<GaugeFactory>().CopyTo(factory);
    if (wcscmp(WindowsGetStringRawBuffer(name, nullptr), L"Example.Component.Printer") == 0)
        return Make<PrinterFactory>().CopyTo(factory);
    if (wcscmp(WindowsGetStringRawBuffer(name, nullptr), L"Example.Component.Engine") != 0)
        return CLASS_E_CLASSNOTAVAILABLE;
    return Make<EngineFactory>().CopyTo(factory);
}

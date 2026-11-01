// GA.GoogleAnalytics: a C# WinRT component Gameloft games (Asphalt 7: Heat)
// ship for usage reporting, hosted by Windows' CLRHost.dll, which needs the
// package to run. Stood in offline, like the Xbox LIVE services: every call
// succeeds and nothing is sent. Interface declared from GA.winmd; Nullable<Int64>
// parameters are IReference<Int64>.
#include "registry.h"
#include "com/factory.h"
#include "log.h"
#include <wrl.h>
#include <mutex>
#include <string>
using namespace Microsoft::WRL;
namespace appx {
namespace {
using OptionalInt64 = ABI::Windows::Foundation::IReference<INT64>;
MIDL_INTERFACE("5F781E60-C3CE-5C08-5A4C-CF5FAA09EE8E")
IGoogleAnalyticsStatic : public IInspectable {
    virtual HRESULT STDMETHODCALLTYPE get_TrackingID(HSTRING * value) = 0;
    virtual HRESULT STDMETHODCALLTYPE Init(HSTRING trackingId) = 0;
    virtual HRESULT STDMETHODCALLTYPE put_TrackingID(HSTRING value) = 0;
    virtual HRESULT STDMETHODCALLTYPE Run() = 0;
    virtual HRESULT STDMETHODCALLTYPE SendEvent(HSTRING category, HSTRING action, HSTRING label,
                                                OptionalInt64 * value) = 0;
    virtual HRESULT STDMETHODCALLTYPE SendView() = 0;
    virtual HRESULT STDMETHODCALLTYPE SendViewWithScreen(HSTRING screen) = 0;
    virtual HRESULT STDMETHODCALLTYPE SetAppScreen(HSTRING screen) = 0;
    virtual HRESULT STDMETHODCALLTYPE setCustomDimension(INT32 index, HSTRING value) = 0;
    virtual HRESULT STDMETHODCALLTYPE setCustomMetric(INT32 index, OptionalInt64 * value) = 0;
    virtual HRESULT STDMETHODCALLTYPE SetStartSession(boolean start) = 0;
    virtual HRESULT STDMETHODCALLTYPE SetVideoAdapter(HSTRING name, INT64 dedicated, INT64 shared) = 0;
    virtual HRESULT STDMETHODCALLTYPE SetVideoResolution(INT64 width, INT64 height) = 0;
};
class GoogleAnalyticsFactory final : public Factory<IGoogleAnalyticsStatic> {
    std::mutex mutex_;
    std::wstring trackingId_;

public:
    GoogleAnalyticsFactory() : Factory(L"GA.GoogleAnalytics") {}
    HRESULT STDMETHODCALLTYPE get_TrackingID(HSTRING* value) override {
        if (!value) return E_POINTER;
        std::lock_guard<std::mutex> lock(mutex_);
        return WindowsCreateString(trackingId_.data(), static_cast<UINT32>(trackingId_.size()), value);
    }
    HRESULT STDMETHODCALLTYPE Init(HSTRING trackingId) override {
        Log("TRACE", "GoogleAnalytics offline: hits not sent");
        return put_TrackingID(trackingId);
    }
    HRESULT STDMETHODCALLTYPE put_TrackingID(HSTRING value) override {
        std::lock_guard<std::mutex> lock(mutex_);
        UINT32 length = 0;
        const auto* raw = WindowsGetStringRawBuffer(value, &length);
        trackingId_.assign(raw, length);
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE Run() override { return S_OK; }
    HRESULT STDMETHODCALLTYPE SendEvent(HSTRING, HSTRING, HSTRING, OptionalInt64*) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE SendView() override { return S_OK; }
    HRESULT STDMETHODCALLTYPE SendViewWithScreen(HSTRING) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE SetAppScreen(HSTRING) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE setCustomDimension(INT32, HSTRING) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE setCustomMetric(INT32, OptionalInt64*) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE SetStartSession(boolean) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE SetVideoAdapter(HSTRING, INT64, INT64) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE SetVideoResolution(INT64, INT64) override { return S_OK; }
};
}
HRESULT MakeGoogleAnalyticsFactory(IActivationFactory** value) {
    static auto factory = Make<GoogleAnalyticsFactory>();
    return factory ? factory.CopyTo(value) : E_OUTOFMEMORY;
}
const ShimClass googleAnalyticsClass{L"GA.GoogleAnalytics", MakeGoogleAnalyticsFactory, ShimPolicy::Replace, InIslands};
}

// The game's network: offline unless [options] Network=1 in appx-portable.ini
// (or --network, APPX_PORTABLE_NETWORK=1). Offline, the game is told it has no
// internet connection and its Windows.Web.Http requests fail at once. Old
// online games otherwise wait on servers that no longer answer; Asphalt 8
// keeps its age screen's button spinning until Gameloft's servers send their
// settings. With Network=1 these classes are Windows' own.
#include "generated/http_filters.h"
#include "generated/http.h"
#include "generated/network.h"
#include "com/factory.h"
#include "registry.h"
#include "options.h"
#include <wrl/event.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Storage.Streams.h>
#include <winrt/Windows.Web.Http.h>
using namespace Microsoft::WRL;
using namespace ABI::Windows::Web::Http;
using namespace ABI::Windows::Web::Http::Filters;
namespace appx {
namespace {
using HttpOperation =
    __FIAsyncOperationWithProgress_2_Windows__CWeb__CHttp__CHttpResponseMessage_Windows__CWeb__CHttp__CHttpProgress;
template <typename Result>
winrt::Windows::Foundation::IAsyncOperationWithProgress<Result, winrt::Windows::Web::Http::HttpProgress>
OfflineHttpRequest() {
    // A coroutine captures this exception in the operation rather than throwing
    // while the game starts a request. Its completion handler receives Error.
    co_return[]()->Result { throw winrt::hresult_error(HRESULT_FROM_WIN32(ERROR_NETWORK_UNREACHABLE)); }
    ();
}
// Every request a client makes, for a response, a string, a buffer or a stream.
template <typename Result, typename Operation> HRESULT MakeOfflineHttpRequest(Operation** value) {
    if (!value) return E_POINTER;
    *value = nullptr;
    auto operation = OfflineHttpRequest<Result>();
    *value = reinterpret_cast<Operation*>(winrt::detach_abi(operation));
    Log("TRACE", "HttpClient request completed offline");
    return S_OK;
}
HRESULT MakeOfflineHttpRequest(HttpOperation** value) {
    return MakeOfflineHttpRequest<winrt::Windows::Web::Http::HttpResponseMessage>(value);
}
}
class Cache final : public RuntimeClass<RuntimeClassFlags<WinRt>, DefaultIHttpCacheControl, FtmBase> {
    InspectableClass(L"Windows.Web.Http.Filters.HttpCacheControl", BaseTrust);
    HttpCacheReadBehavior read_ = HttpCacheReadBehavior_Default;
    HttpCacheWriteBehavior write_ = HttpCacheWriteBehavior_Default;

public:
    HRESULT STDMETHODCALLTYPE get_ReadBehavior(HttpCacheReadBehavior* value) override {
        if (!value) return E_POINTER;
        *value = read_;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE put_ReadBehavior(HttpCacheReadBehavior value) override {
        read_ = value;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_WriteBehavior(HttpCacheWriteBehavior* value) override {
        if (!value) return E_POINTER;
        *value = write_;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE put_WriteBehavior(HttpCacheWriteBehavior value) override {
        write_ = value;
        return S_OK;
    }
};
class Filter final : public RuntimeClass<RuntimeClassFlags<WinRt>, DefaultIHttpBaseProtocolFilter, DefaultIHttpFilter,
                                         ABI::Windows::Foundation::IClosable, FtmBase> {
    InspectableClass(L"Windows.Web.Http.Filters.HttpBaseProtocolFilter", BaseTrust);
    ComPtr<Cache> cache_ = Make<Cache>();
    boolean autoRedirect_ = true, ui_ = false, decompress_ = true, proxy_ = false;
    UINT32 connections_ = 6;

public:
#define BOOLEAN_PROPERTY(Name, field)                                                                                  \
    HRESULT STDMETHODCALLTYPE get_##Name(boolean* value) override {                                                    \
        if (!value) return E_POINTER;                                                                                  \
        *value = field;                                                                                                \
        return S_OK;                                                                                                   \
    }                                                                                                                  \
    HRESULT STDMETHODCALLTYPE put_##Name(boolean value) override {                                                     \
        field = value;                                                                                                 \
        return S_OK;                                                                                                   \
    }
    BOOLEAN_PROPERTY(AllowAutoRedirect, autoRedirect_)
    BOOLEAN_PROPERTY(AllowUI, ui_)
    BOOLEAN_PROPERTY(AutomaticDecompression, decompress_)
    BOOLEAN_PROPERTY(UseProxy, proxy_)
#undef BOOLEAN_PROPERTY
    HRESULT STDMETHODCALLTYPE get_MaxConnectionsPerServer(UINT32* value) override {
        if (!value) return E_POINTER;
        *value = connections_;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE put_MaxConnectionsPerServer(UINT32 value) override {
        connections_ = value;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_CacheControl(IHttpCacheControl** value) override {
        if (!value) return E_POINTER;
        return cache_ ? cache_.CopyTo(value) : E_OUTOFMEMORY;
    }
    HRESULT STDMETHODCALLTYPE SendRequestAsync(
        IHttpRequestMessage*,
        __FIAsyncOperationWithProgress_2_Windows__CWeb__CHttp__CHttpResponseMessage_Windows__CWeb__CHttp__CHttpProgress**
            value) override {
        return MakeOfflineHttpRequest(value);
    }
    HRESULT STDMETHODCALLTYPE Close() override { return S_OK; }
};
class Client final
    : public RuntimeClass<RuntimeClassFlags<WinRt>, DefaultIHttpClient, ABI::Windows::Foundation::IClosable, FtmBase> {
    InspectableClass(L"Windows.Web.Http.HttpClient", BaseTrust);
    winrt::Windows::Web::Http::HttpRequestMessage headers_{nullptr};

public:
    HRESULT STDMETHODCALLTYPE GetAsync(ABI::Windows::Foundation::IUriRuntimeClass* uri,
                                       HttpOperation** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        return uri ? MakeOfflineHttpRequest(value) : E_INVALIDARG;
    }
    HRESULT STDMETHODCALLTYPE GetWithOptionAsync(ABI::Windows::Foundation::IUriRuntimeClass* uri, HttpCompletionOption,
                                                 HttpOperation** value) override {
        return GetAsync(uri, value);
    }
    HRESULT STDMETHODCALLTYPE DeleteAsync(ABI::Windows::Foundation::IUriRuntimeClass* uri,
                                          HttpOperation** value) override {
        return GetAsync(uri, value);
    }
    HRESULT STDMETHODCALLTYPE PostAsync(ABI::Windows::Foundation::IUriRuntimeClass* uri, IHttpContent*,
                                        HttpOperation** value) override {
        return GetAsync(uri, value);
    }
    HRESULT STDMETHODCALLTYPE PutAsync(ABI::Windows::Foundation::IUriRuntimeClass* uri, IHttpContent*,
                                       HttpOperation** value) override {
        return GetAsync(uri, value);
    }
    HRESULT STDMETHODCALLTYPE GetBufferAsync(
        ABI::Windows::Foundation::IUriRuntimeClass* uri,
        __FIAsyncOperationWithProgress_2_Windows__CStorage__CStreams__CIBuffer_Windows__CWeb__CHttp__CHttpProgress**
            value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        return uri ? MakeOfflineHttpRequest<winrt::Windows::Storage::Streams::IBuffer>(value) : E_INVALIDARG;
    }
    HRESULT STDMETHODCALLTYPE GetInputStreamAsync(
        ABI::Windows::Foundation::IUriRuntimeClass* uri,
        __FIAsyncOperationWithProgress_2_Windows__CStorage__CStreams__CIInputStream_Windows__CWeb__CHttp__CHttpProgress**
            value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        return uri ? MakeOfflineHttpRequest<winrt::Windows::Storage::Streams::IInputStream>(value) : E_INVALIDARG;
    }
    HRESULT STDMETHODCALLTYPE
    GetStringAsync(ABI::Windows::Foundation::IUriRuntimeClass* uri,
                   __FIAsyncOperationWithProgress_2_HSTRING_Windows__CWeb__CHttp__CHttpProgress** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        return uri ? MakeOfflineHttpRequest<winrt::hstring>(value) : E_INVALIDARG;
    }
    HRESULT STDMETHODCALLTYPE SendRequestAsync(IHttpRequestMessage* request, HttpOperation** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        return request ? MakeOfflineHttpRequest(value) : E_INVALIDARG;
    }
    HRESULT STDMETHODCALLTYPE SendRequestWithOptionAsync(IHttpRequestMessage* request, HttpCompletionOption,
                                                         HttpOperation** value) override {
        return SendRequestAsync(request, value);
    }
    // Games add their headers before each request. Windows' own collection,
    // from a request message nothing sends, keeps them.
    HRESULT STDMETHODCALLTYPE
    get_DefaultRequestHeaders(ABI::Windows::Web::Http::Headers::IHttpRequestHeaderCollection** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        try {
            if (!headers_) headers_ = winrt::Windows::Web::Http::HttpRequestMessage();
            *value = static_cast<ABI::Windows::Web::Http::Headers::IHttpRequestHeaderCollection*>(
                winrt::detach_abi(headers_.Headers()));
            return S_OK;
        } catch (...) {
            return winrt::to_hresult();
        }
    }
    HRESULT STDMETHODCALLTYPE Close() override { return S_OK; }
};
class HttpFactory final : public Factory<DefaultIHttpClientFactory> {
    bool filter_;

public:
    explicit HttpFactory(bool filter)
        : Factory(filter ? L"Windows.Web.Http.Filters.HttpBaseProtocolFilter" : L"Windows.Web.Http.HttpClient"),
          filter_(filter) {}
    HRESULT STDMETHODCALLTYPE ActivateInstance(IInspectable** value) override {
        if (!value) return E_POINTER;
        if (filter_) {
            auto filter = Make<Filter>();
            return filter ? filter.CopyTo(value) : E_OUTOFMEMORY;
        }
        auto client = Make<Client>();
        return client ? client.CopyTo(value) : E_OUTOFMEMORY;
    }
    HRESULT STDMETHODCALLTYPE Create(IHttpFilter*, IHttpClient** value) override {
        if (!value) return E_POINTER;
        auto client = Make<Client>();
        return client ? client.CopyTo(value) : E_OUTOFMEMORY;
    }
};
using namespace ABI::Windows::Networking::Connectivity;
class NetworkFactory final : public Factory<ABI::Windows::Networking::Connectivity::DefaultINetworkInformationStatics> {
    EventSource<ABI::Windows::Networking::Connectivity::INetworkStatusChangedEventHandler> changed_;
    // Windows' own class works outside a package. It answers the queries about
    // the local machine; connectivity stays the shim's offline answer.
    static HRESULT Os(ComPtr<ABI::Windows::Networking::Connectivity::INetworkInformationStatics>& statics) {
        return OsGetActivationFactory(
            Microsoft::WRL::Wrappers::HStringReference(L"Windows.Networking.Connectivity.NetworkInformation").Get(),
            IID_PPV_ARGS(&statics));
    }

public:
    NetworkFactory() : Factory(L"Windows.Networking.Connectivity.NetworkInformation") {}
    HRESULT STDMETHODCALLTYPE
    GetConnectionProfiles(__FIVectorView_1_Windows__CNetworking__CConnectivity__CConnectionProfile** value) override {
        ComPtr<ABI::Windows::Networking::Connectivity::INetworkInformationStatics> statics;
        auto hr = Os(statics);
        return SUCCEEDED(hr) ? statics->GetConnectionProfiles(value) : hr;
    }
    HRESULT STDMETHODCALLTYPE
    GetLanIdentifiers(__FIVectorView_1_Windows__CNetworking__CConnectivity__CLanIdentifier** value) override {
        ComPtr<ABI::Windows::Networking::Connectivity::INetworkInformationStatics> statics;
        auto hr = Os(statics);
        return SUCCEEDED(hr) ? statics->GetLanIdentifiers(value) : hr;
    }
    // Unity's .NET networking (Dns.GetHostName) asks for these on a worker
    // thread, and a failure there ends the game.
    HRESULT STDMETHODCALLTYPE GetHostNames(__FIVectorView_1_Windows__CNetworking__CHostName** value) override {
        ComPtr<ABI::Windows::Networking::Connectivity::INetworkInformationStatics> statics;
        auto hr = Os(statics);
        return SUCCEEDED(hr) ? statics->GetHostNames(value) : hr;
    }
    HRESULT STDMETHODCALLTYPE GetProxyConfigurationAsync(
        ABI::Windows::Foundation::IUriRuntimeClass* uri,
        __FIAsyncOperation_1_Windows__CNetworking__CConnectivity__CProxyConfiguration** value) override {
        ComPtr<ABI::Windows::Networking::Connectivity::INetworkInformationStatics> statics;
        auto hr = Os(statics);
        return SUCCEEDED(hr) ? statics->GetProxyConfigurationAsync(uri, value) : hr;
    }
    HRESULT STDMETHODCALLTYPE
    GetSortedEndpointPairs(__FIIterable_1_Windows__CNetworking__CEndpointPair* destinations,
                           ABI::Windows::Networking::HostNameSortOptions options,
                           __FIVectorView_1_Windows__CNetworking__CEndpointPair** value) override {
        ComPtr<ABI::Windows::Networking::Connectivity::INetworkInformationStatics> statics;
        auto hr = Os(statics);
        return SUCCEEDED(hr) ? statics->GetSortedEndpointPairs(destinations, options, value) : hr;
    }
    HRESULT STDMETHODCALLTYPE
    GetInternetConnectionProfile(ABI::Windows::Networking::Connectivity::IConnectionProfile** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        static const bool announced = [] {
            Log("INFO", "network: the game is told it is offline; Network=1 in appx-portable.ini connects it");
            return true;
        }();
        (void)announced;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE
    add_NetworkStatusChanged(ABI::Windows::Networking::Connectivity::INetworkStatusChangedEventHandler* handler,
                             EventRegistrationToken* token) override {
        return changed_.Add(handler, token);
    }
    HRESULT STDMETHODCALLTYPE remove_NetworkStatusChanged(EventRegistrationToken token) override {
        return changed_.Remove(token);
    }
};
// With Network=1 the shim declines these classes, and Windows' serve the game.
HRESULT MakeHttpFactory(bool filter, IActivationFactory** value) {
    if (GetOptions().network) return CLASS_E_CLASSNOTAVAILABLE;
    auto factory = Make<HttpFactory>(filter);
    return factory ? factory.CopyTo(value) : E_OUTOFMEMORY;
}
HRESULT MakeNetworkFactory(IActivationFactory** value) {
    if (GetOptions().network) return CLASS_E_CLASSNOTAVAILABLE;
    static auto factory = Make<NetworkFactory>();
    return factory ? factory.CopyTo(value) : E_OUTOFMEMORY;
}
const ShimClass httpFilterClass{L"Windows.Web.Http.Filters.HttpBaseProtocolFilter",
                                [](IActivationFactory** value) { return MakeHttpFactory(true, value); },
                                ShimPolicy::Replace, InIslands};
const ShimClass httpClientClass{L"Windows.Web.Http.HttpClient",
                                [](IActivationFactory** value) { return MakeHttpFactory(false, value); },
                                ShimPolicy::Replace, InIslands};
const ShimClass networkClass{L"Windows.Networking.Connectivity.NetworkInformation", MakeNetworkFactory,
                             ShimPolicy::Replace, InIslands};
}

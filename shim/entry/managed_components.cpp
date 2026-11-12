// C# Windows Runtime components (analytics, ads, social SDKs) are served by
// Windows' CLRHost.dll, which needs package identity. Instead, start the
// desktop .NET Framework CLR in the game's process and let it make the class's
// activation factory, through the bridge in appx-host.exe beside the game. The
// desktop CLR finds the component's .winmd beside the executable, as CLRHost
// would in the package, and the factory it returns is the one CLRHost would
// hand out, so every component works without a stand-in of its own.
#include "entry/managed_components.h"
#include "log.h"
#include "package/package.h"
#include <metahost.h>
#include <wrl/client.h>
#include <atomic>
#include <cwchar>
#include <mutex>
using Microsoft::WRL::ComPtr;
namespace appx {
namespace {
// From metahost.h/mscoree.h; defined here so nothing links mscoree.lib.
constexpr CLSID clsidMetaHost{0x9280188d, 0x0e8e, 0x4867, {0xb3, 0x0c, 0x7f, 0xa8, 0x38, 0x84, 0xe8, 0xde}};
constexpr CLSID clsidRuntimeHost{0x90f1a06e, 0x7712, 0x4762, {0x86, 0xb5, 0x7a, 0x5e, 0xba, 0x6b, 0xdb, 0x02}};
std::atomic<bool> hosted{false};

HRESULT StartClr(ComPtr<ICLRRuntimeHost>& result) {
    HMODULE mscoree = LoadLibraryExW(L"mscoree.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!mscoree) return HRESULT_FROM_WIN32(GetLastError());
    auto create = reinterpret_cast<decltype(&CLRCreateInstance)>(GetProcAddress(mscoree, "CLRCreateInstance"));
    if (!create) return HRESULT_FROM_WIN32(GetLastError());
    ComPtr<ICLRMetaHost> meta;
    HRESULT hr = create(clsidMetaHost, IID_PPV_ARGS(&meta));
    ComPtr<ICLRRuntimeInfo> runtime;
    if (SUCCEEDED(hr)) hr = meta->GetRuntime(L"v4.0.30319", IID_PPV_ARGS(&runtime));
    ComPtr<ICLRRuntimeHost> host;
    if (SUCCEEDED(hr)) hr = runtime->GetInterface(clsidRuntimeHost, IID_PPV_ARGS(&host));
    // Calls from clr.dll into Windows classes must reach the shim from here on.
    if (SUCCEEDED(hr)) hosted = true;
    // S_FALSE: already running, as in appx-host.exe.
    if (SUCCEEDED(hr)) hr = host->Start();
    if (SUCCEEDED(hr)) result = std::move(host);
    return hr;
}
// One start for the process; a failure is kept, so each class fails the same way.
HRESULT Runtime(ComPtr<ICLRRuntimeHost>& host) {
    static std::once_flag once;
    static ComPtr<ICLRRuntimeHost> runtime;
    static HRESULT started = E_UNEXPECTED;
    std::call_once(once, [] {
        started = StartClr(runtime);
        Log(SUCCEEDED(started) ? "INFO" : "FAIL", "desktop CLR for C# components hr=%08lx", started);
    });
    host = runtime;
    return started;
}
}
bool IsClrHostModule(const std::filesystem::path& module) noexcept {
    return _wcsicmp(module.filename().c_str(), L"CLRHost.dll") == 0;
}
bool ManagedComponentsHosted() noexcept { return hosted.load(); }
HRESULT ManagedComponentFactory(HSTRING name, IActivationFactory** factory) noexcept {
    if (!factory) return E_POINTER;
    *factory = nullptr;
    const wchar_t* id = WindowsGetStringRawBuffer(name, nullptr);
    try {
        const auto bridge = CurrentPackage().directory / L"appx-host.exe";
        if (GetFileAttributesW(bridge.c_str()) == INVALID_FILE_ATTRIBUTES) {
            Log("FAIL", "C# component %ls: appx-host.exe is not in the game folder", id);
            return REGDB_E_CLASSNOTREG;
        }
        ComPtr<ICLRRuntimeHost> host;
        HRESULT hr = Runtime(host);
        if (FAILED(hr)) return REGDB_E_CLASSNOTREG;
        // The bridge writes the AddRef'd factory through this address.
        IActivationFactory* made = nullptr;
        std::wstring request(32, L'\0');
        request.resize(swprintf(request.data(), request.size(), L"%llx|",
                                static_cast<unsigned long long>(reinterpret_cast<UINT_PTR>(&made))));
        request += id;
        DWORD result = 0;
        hr = host->ExecuteInDefaultAppDomain(bridge.c_str(), L"ManagedComponents", L"GetFactory", request.c_str(),
                                             &result);
        if (SUCCEEDED(hr)) hr = static_cast<HRESULT>(result);
        if (SUCCEEDED(hr) && !made) hr = E_UNEXPECTED;
        Log(SUCCEEDED(hr) ? "INFO" : "FAIL", "C# component %ls from the desktop CLR hr=%08lx", id, hr);
        if (FAILED(hr)) {
            if (made) made->Release();
            return hr;
        }
        *factory = made;
        return S_OK;
    } catch (...) {
        return E_OUTOFMEMORY;
    }
}
}

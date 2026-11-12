#include "xbf_loader.h"
#include "package/package.h"
#include "xaml/xbf_webview.h"
#include "log.h"
#include <psapi.h>
#include <cstring>
#include <string>
namespace appx {
namespace {
using MapCall = LPVOID(WINAPI*)(HANDLE, DWORD, DWORD, DWORD, SIZE_T);
MapCall realMap = &MapViewOfFile;
std::wstring PackageNtDirectory() {
    const auto& directory = CurrentPackage().directory;
    HANDLE handle =
        CreateFileW(directory.c_str(), FILE_READ_ATTRIBUTES, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                    nullptr, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, nullptr);
    if (handle == INVALID_HANDLE_VALUE) return {};
    std::wstring path(32768, L'\0');
    const auto length = GetFinalPathNameByHandleW(handle, path.data(), static_cast<DWORD>(path.size()), VOLUME_NAME_NT);
    CloseHandle(handle);
    if (!length || length >= path.size()) return {};
    path.resize(length);
    while (!path.empty() && path.back() == L'\\') path.pop_back();
    return path;
}
// The size of the package file with this extension that the view maps, or 0.
size_t PackageViewSize(const void* view, const wchar_t* extension) {
    static const auto directory = PackageNtDirectory();
    if (directory.empty()) return 0;
    std::wstring path(32768, L'\0');
    const auto length =
        GetMappedFileNameW(GetCurrentProcess(), const_cast<void*>(view), path.data(), static_cast<DWORD>(path.size()));
    if (!length || length >= path.size()) return 0;
    path.resize(length);
    if (path.size() <= directory.size() || _wcsnicmp(path.c_str(), directory.c_str(), directory.size()) != 0 ||
        path[directory.size()] != L'\\')
        return 0;
    const size_t suffix = wcslen(extension);
    if (path.size() < suffix || _wcsicmp(path.c_str() + path.size() - suffix, extension) != 0) return 0;
    WIN32_FILE_ATTRIBUTE_DATA attributes{};
    const auto absolute = L"\\\\?\\GLOBALROOT" + path;
    if (!GetFileAttributesExW(absolute.c_str(), GetFileExInfoStandard, &attributes)) return 0;
    return attributes.nFileSizeHigh ? 0 : attributes.nFileSizeLow;
}
// Windows 8.0 packages ship source XAML; Windows parses it from the mapped file.
bool SourceXamlView(const void* view, const MEMORY_BASIC_INFORMATION& region, SIZE_T count, size_t& size) {
    size = PackageViewSize(view, L".xaml");
    return size && size <= region.RegionSize && size <= 64 * 1024 * 1024 && (!count || count == size);
}
LPVOID WINAPI Map(HANDLE mapping, DWORD access, DWORD high, DWORD low, SIZE_T count) noexcept {
    void* original = realMap(mapping, access, high, low, count);
    const DWORD error = GetLastError();
    if (!original || high || low || access != FILE_MAP_READ) {
        SetLastError(error);
        return original;
    }
    try {
        MEMORY_BASIC_INFORMATION region{};
        UINT32 header[3]{};
        if (!VirtualQuery(original, &region, sizeof(region)) || region.RegionSize < sizeof(header) ||
            region.State != MEM_COMMIT || (region.Protect & (PAGE_NOACCESS | PAGE_GUARD))) {
            SetLastError(error);
            return original;
        }
        memcpy(header, original, sizeof(header));
        const UINT64 size = 12ull + header[1] + header[2];
        const bool xbf = header[0] == 0x00464258 && size <= region.RegionSize && size <= 64 * 1024 * 1024 &&
                         (!count || size == count) && PackageViewSize(original, L".xbf") == size;
        size_t sourceSize = 0;
        if (!xbf && !SourceXamlView(original, region, count, sourceSize)) {
            SetLastError(error);
            return original;
        }
        std::vector<BYTE> adapted;
        const auto* bytes = static_cast<const BYTE*>(original);
        const auto hr = xbf ? RemapXbfStandIns(bytes, static_cast<size_t>(size), adapted)
                            : RemapSourceXamlStandIns(bytes, sourceSize, adapted);
        if (hr != S_OK) {
            if (FAILED(hr)) Log("FAIL", "%s stand-in adaptation failed hr=%08lx", xbf ? "XBF" : "XAML", hr);
            SetLastError(error);
            return original;
        }
        HANDLE shadow = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0,
                                           static_cast<DWORD>(adapted.size()), nullptr);
        void* view = shadow ? realMap(shadow, FILE_MAP_WRITE, 0, 0, adapted.size()) : nullptr;
        if (shadow) CloseHandle(shadow);
        if (!view) {
            Log("FAIL", "XBF stand-in memory view failed error=%lu", GetLastError());
            SetLastError(error);
            return original;
        }
        memcpy(view, adapted.data(), adapted.size());
        DWORD protection;
        if (!VirtualProtect(view, adapted.size(), PAGE_READONLY, &protection)) {
            UnmapViewOfFile(view);
            SetLastError(error);
            return original;
        }
        UnmapViewOfFile(original);
        Log("TRACE", "%s stand-in metadata adapted in memory (%zu bytes)", xbf ? "XBF" : "XAML", adapted.size());
        SetLastError(error);
        return view;
    } catch (...) {
        Log("FAIL", "XBF stand-in memory adaptation allocation failed");
        SetLastError(error);
        return original;
    }
}
}
HRESULT InstallIslandXbfLoader() {
    static bool installed = false;
    if (installed) return S_OK;
    HMODULE module = GetModuleHandleW(L"Windows.UI.Xaml.dll");
    if (!module) return HRESULT_FROM_WIN32(ERROR_MOD_NOT_FOUND);
    auto* base = reinterpret_cast<BYTE*>(module);
    auto* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base);
    auto* nt = reinterpret_cast<IMAGE_NT_HEADERS*>(base + dos->e_lfanew);
    const auto directory = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if (!directory.VirtualAddress) return E_NOTIMPL;
    auto* imports = reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(base + directory.VirtualAddress);
    for (; imports->Name; ++imports) {
        if (!imports->OriginalFirstThunk) continue;
        auto* names = reinterpret_cast<IMAGE_THUNK_DATA*>(base + imports->OriginalFirstThunk);
        auto* slots = reinterpret_cast<IMAGE_THUNK_DATA*>(base + imports->FirstThunk);
        for (; names->u1.AddressOfData; ++names, ++slots) {
            if (IMAGE_SNAP_BY_ORDINAL(names->u1.Ordinal)) continue;
            auto* symbol = reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(base + names->u1.AddressOfData);
            if (strcmp(reinterpret_cast<char*>(symbol->Name), "MapViewOfFile") != 0) continue;
            DWORD old;
            if (!VirtualProtect(&slots->u1.Function, sizeof(void*), PAGE_READWRITE, &old))
                return HRESULT_FROM_WIN32(GetLastError());
            realMap = reinterpret_cast<MapCall>(slots->u1.Function);
            InterlockedExchangePointer(reinterpret_cast<void* volatile*>(&slots->u1.Function),
                                       reinterpret_cast<void*>(&Map));
            VirtualProtect(&slots->u1.Function, sizeof(void*), old, &old);
            installed = true;
            return S_OK;
        }
    }
    return E_NOTIMPL;
}
}

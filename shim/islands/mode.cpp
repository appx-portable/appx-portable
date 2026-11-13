#include "islands.h"
#include "options.h"
#include "package/package.h"
#include "log.h"
#include <fstream>
#include <atomic>
#include <vector>
static std::atomic<bool> managedCoreWindow{false};
static std::atomic<bool> nativeXaml{false};
// The managed launcher selects this before invoking an MTA game entry point.
// Keep CLR detection separate: CoreWindow games still need activation hooks.
extern "C" void __cdecl ShimSetManagedCoreWindowMode() noexcept { managedCoreWindow = true; }
namespace appx {
void SetNativeXamlMode() noexcept { nativeXaml = true; }
bool ManagedMode() noexcept {
    static const bool managed = [] {
        try {
            const auto& package = CurrentPackage();
            if (package.executable.empty()) return false;
            std::ifstream file(package.directory / package.executable, std::ios::binary);
            IMAGE_DOS_HEADER dos{};
            IMAGE_NT_HEADERS nt{};
            if (!file.read(reinterpret_cast<char*>(&dos), sizeof dos) || dos.e_magic != IMAGE_DOS_SIGNATURE ||
                dos.e_lfanew < 0)
                return false;
            file.seekg(dos.e_lfanew);
            if (!file.read(reinterpret_cast<char*>(&nt), sizeof nt) || nt.Signature != IMAGE_NT_SIGNATURE ||
                nt.OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR_MAGIC)
                return false;
            return nt.OptionalHeader.NumberOfRvaAndSizes > IMAGE_DIRECTORY_ENTRY_COM_DESCRIPTOR &&
                   nt.OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_COM_DESCRIPTOR].VirtualAddress != 0;
        } catch (...) {
            return false;
        }
    }();
    return managed;
}
// XAML Islands start only in a process whose own manifest declares a
// maxversiontested of Windows 10 1903 or later, which the installer writes
// beside a native XAML game. Ask the process default activation context, as
// XAML does, so a manifest Windows did not apply does not count.
static bool ManifestAllowsIslands() noexcept {
    SIZE_T needed = 0;
    QueryActCtxW(QUERY_ACTCTX_FLAG_NO_ADDREF, nullptr, nullptr, CompatibilityInformationInActivationContext, nullptr, 0,
                 &needed);
    if (!needed) return false;
    std::vector<BYTE> buffer(needed);
    if (!QueryActCtxW(QUERY_ACTCTX_FLAG_NO_ADDREF, nullptr, nullptr, CompatibilityInformationInActivationContext,
                      buffer.data(), buffer.size(), &needed))
        return false;
    const auto* info = reinterpret_cast<const ACTIVATION_CONTEXT_COMPATIBILITY_INFORMATION*>(buffer.data());
    constexpr ULONGLONG windows10Version1903 = 0x000A000047BA0000ull; // 10.0.18362.0
    for (DWORD i = 0; i < info->ElementCount; ++i) {
        const auto& element = info->Elements[i];
        if (element.Type == ACTCTX_COMPATIBILITY_ELEMENT_TYPE_MAXVERSIONTESTED &&
            element.MaxVersionTested >= windows10Version1903)
            return true;
    }
    return false;
}
// A native game with XAML pages runs Windows' own XAML in an island when its
// manifest allows it, unless [options] Xaml=portable keeps it on the portable
// backend. Installs from before this default have no manifest and keep it too.
static bool NativeSystemXaml() noexcept {
    static const bool system = [] {
        try {
            const auto& package = CurrentPackage();
            if (!package.loaded || ManagedMode() || GetOptions().portableXaml) return false;
            // Wine has no XAML Islands.
            if (GetProcAddress(GetModuleHandleW(L"ntdll.dll"), "wine_get_version")) return false;
            if (!PackageHasXaml(package.directory / package.executable)) return false;
            const bool allowed = ManifestAllowsIslands();
            appx::Log("INFO", "native XAML: %s",
                      allowed ? "Windows XAML in an island" : "portable backend (no maxversiontested in the manifest)");
            return allowed;
        } catch (...) {
            return false;
        }
    }();
    return system;
}
// appx-native-host.exe starts native C++/CX XAML games, which run on real
// Windows XAML in an island, as .NET Native games do; its CoreWindow games do not.
bool IslandsMode() noexcept {
    return nativeXaml.load() || GetOptions().hostedXaml || (ManagedMode() && !managedCoreWindow.load()) ||
           NativeSystemXaml();
}
}

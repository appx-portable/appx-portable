#include "package.h"
#include "log.h"
#include <bcrypt.h>
#include <shlwapi.h>
#include <xmllite.h>
#include <wrl/client.h>
#include <algorithm>
#include <functional>
#include <string_view>
using Microsoft::WRL::ComPtr;
namespace appx {
std::wstring PublisherId(const std::wstring& publisher) {
    // Classic CNG calls: one-shot BCryptHash and pseudo-handles need Windows 10.
    BYTE hash[32]{};
    BCRYPT_ALG_HANDLE algorithm = nullptr;
    BCRYPT_HASH_HANDLE handle = nullptr;
    bool hashed =
        BCRYPT_SUCCESS(BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0)) &&
        BCRYPT_SUCCESS(BCryptCreateHash(algorithm, &handle, nullptr, 0, nullptr, 0, 0)) &&
        BCRYPT_SUCCESS(BCryptHashData(handle, reinterpret_cast<PUCHAR>(const_cast<wchar_t*>(publisher.data())),
                                      static_cast<ULONG>(publisher.size() * sizeof(wchar_t)), 0)) &&
        BCRYPT_SUCCESS(BCryptFinishHash(handle, hash, sizeof(hash), 0));
    if (handle) BCryptDestroyHash(handle);
    if (algorithm) BCryptCloseAlgorithmProvider(algorithm, 0);
    if (!hashed) return {};
    static constexpr wchar_t alphabet[] = L"0123456789abcdefghjkmnpqrstvwxyz";
    unsigned long long bits = 0;
    for (int i = 0; i < 8; ++i) bits = bits << 8 | hash[i];
    // 64 bits make 12 full groups of five plus four bits, padded with a zero bit.
    std::wstring id;
    for (int i = 0; i < 12; ++i) id += alphabet[(bits >> (59 - 5 * i)) & 31];
    id += alphabet[(bits << 1) & 31];
    return id;
}
static std::wstring Attribute(IXmlReader* reader, const wchar_t* name) {
    if (FAILED(reader->MoveToAttributeByName(name, nullptr))) return {};
    const wchar_t* value = nullptr;
    reader->GetValue(&value, nullptr);
    std::wstring result = value ? value : L"";
    reader->MoveToElement();
    return result;
}
// Element text, for <DisplayName> and <Path>.
static std::wstring Text(IXmlReader* reader) {
    BOOL empty = reader->IsEmptyElement();
    std::wstring text;
    XmlNodeType type;
    while (!empty && reader->Read(&type) == S_OK && type != XmlNodeType_EndElement) {
        if (type != XmlNodeType_Text && type != XmlNodeType_CDATA) continue;
        const wchar_t* value = nullptr;
        reader->GetValue(&value, nullptr);
        if (value) text += value;
    }
    return text;
}
// A resource from the package's resources.pri, read in place as the shell reads
// an installed package's: a string's value, or a file candidate's full path.
// Empty when the package has no index or the index has no such resource.
static std::wstring LoadResource(const PackageInfo& info, const std::wstring& uri) {
    const auto index = info.directory / L"resources.pri";
    if (GetFileAttributesW(index.c_str()) == INVALID_FILE_ATTRIBUTES) return {};
    const auto source = L"@{" + index.wstring() + L"? " + uri + L"}";
    std::wstring value(32768, L'\0');
    if (FAILED(SHLoadIndirectString(source.c_str(), value.data(), static_cast<UINT>(value.size()), nullptr))) return {};
    value.resize(wcsnlen(value.c_str(), value.size()));
    return value;
}
// "ms-resource:AppName" names a string in the default Resources.resw map and
// "ms-resource:Other/AppName" may name one in Other.resw; "ms-resource:/path"
// and "ms-resource://package/path" are absolute. Other text is the name itself.
static std::wstring ResolveName(const PackageInfo& info, const std::wstring& name) {
    static constexpr std::wstring_view scheme = L"ms-resource:";
    if (name.rfind(scheme, 0) != 0) return name;
    const auto reference = name.substr(scheme.size());
    if (reference.rfind(L"//", 0) == 0) return LoadResource(info, name);
    const auto package = L"ms-resource://" + info.identityName;
    if (reference.rfind(L'/', 0) == 0) return LoadResource(info, package + reference);
    auto value = LoadResource(info, package + L"/resources/" + reference);
    if (value.empty() && reference.find(L'/') != std::wstring::npos)
        value = LoadResource(info, package + L"/" + reference);
    return value;
}
std::filesystem::path ResolvePackageFile(const PackageInfo& package, const std::wstring& relative) {
    auto name = relative;
    std::replace(name.begin(), name.end(), L'\\', L'/');
    const auto resolved = LoadResource(package, L"ms-resource://" + package.identityName + L"/Files/" + name);
    return resolved.empty() ? package.directory / relative : std::filesystem::path(resolved);
}
std::wstring ResolveDisplayName(const PackageInfo& package) {
    for (const auto* name : {&package.displayName, &package.tileName})
        if (!name->empty())
            if (auto value = ResolveName(package, *name); !value.empty()) return value;
    return {};
}
PackageInfo ReadPackage(const std::filesystem::path& directory) {
    PackageInfo info;
    info.directory = directory;
    ComPtr<IStream> stream;
    ComPtr<IXmlReader> reader;
    const auto manifest = directory / L"AppxManifest.xml";
    if (FAILED(
            SHCreateStreamOnFileEx(manifest.c_str(), STGM_READ | STGM_SHARE_DENY_WRITE, 0, FALSE, nullptr, &stream)) ||
        FAILED(CreateXmlReader(IID_PPV_ARGS(&reader), nullptr)) ||
        FAILED(reader->SetProperty(XmlReaderProperty_DtdProcessing, DtdProcessing_Prohibit)) ||
        FAILED(reader->SetInput(stream.Get())))
        return info;
    // Elements are matched by local name: Windows 8, 8.1 and 10 manifests use
    // different namespaces for the same elements.
    std::wstring serverModule;
    bool inProperties = false, inServer = false, firstApplication = true;
    XmlNodeType type;
    HRESULT hr;
    while ((hr = reader->Read(&type)) == S_OK) {
        const wchar_t* name = nullptr;
        if (type == XmlNodeType_EndElement) {
            reader->GetLocalName(&name, nullptr);
            if (!wcscmp(name, L"Properties")) inProperties = false;
            if (!wcscmp(name, L"InProcessServer")) inServer = false;
            continue;
        }
        if (type != XmlNodeType_Element) continue;
        reader->GetLocalName(&name, nullptr);
        if (!wcscmp(name, L"Identity")) {
            info.identityName = Attribute(reader.Get(), L"Name");
            info.publisher = Attribute(reader.Get(), L"Publisher");
            info.version = Attribute(reader.Get(), L"Version");
            info.architecture = Attribute(reader.Get(), L"ProcessorArchitecture");
            if (info.architecture.empty()) info.architecture = L"neutral";
        } else if (!wcscmp(name, L"TargetDeviceFamily")) {
            info.universal = true;
        } else if (!wcscmp(name, L"Properties")) {
            inProperties = !reader->IsEmptyElement();
        } else if (inProperties && !wcscmp(name, L"DisplayName")) {
            info.displayName = Text(reader.Get());
        } else if (!wcscmp(name, L"Application") && firstApplication) {
            firstApplication = false;
            info.applicationId = Attribute(reader.Get(), L"Id");
            info.executable = Attribute(reader.Get(), L"Executable");
            info.entryPoint = Attribute(reader.Get(), L"EntryPoint");
        } else if (!wcscmp(name, L"VisualElements") && info.smallLogo.empty()) {
            info.tileName = Attribute(reader.Get(), L"DisplayName");
            // Windows 8: SmallLogo; 8.1: Square30x30Logo; 10: Square44x44Logo.
            for (const auto* logo : {L"SmallLogo", L"Square30x30Logo", L"Square44x44Logo"})
                if (info.smallLogo.empty()) info.smallLogo = Attribute(reader.Get(), logo);
        } else if (!wcscmp(name, L"InProcessServer")) {
            inServer = !reader->IsEmptyElement();
            serverModule.clear();
        } else if (inServer && !wcscmp(name, L"Path")) {
            serverModule = Text(reader.Get());
        } else if (inServer && !wcscmp(name, L"ActivatableClass")) {
            auto id = Attribute(reader.Get(), L"ActivatableClassId");
            if (!id.empty() && !serverModule.empty()) info.classes.push_back({std::move(id), directory / serverModule});
        }
    }
    if (FAILED(hr)) Log("FAIL", "AppxManifest.xml parse error hr=%08lx; using defaults", hr);
    if (!info.publisher.empty()) {
        info.publisherId = PublisherId(info.publisher);
        info.familyName = info.identityName + L"_" + info.publisherId;
        info.fullName = info.identityName + L"_" + info.version + L"_" + info.architecture + L"__" + info.publisherId;
    }
    info.loaded = !info.identityName.empty();
    return info;
}
static std::filesystem::path ExecutablePath() {
    std::wstring path(MAX_PATH, L'\0');
    DWORD length = 0;
    while ((length = GetModuleFileNameW(nullptr, path.data(), static_cast<DWORD>(path.size()))) == path.size())
        path.resize(path.size() * 2);
    path.resize(length);
    return path;
}
const PackageInfo& CurrentPackage() noexcept {
    static const PackageInfo info = [] {
        PackageInfo result;
        try {
            result = ReadPackage(ExecutablePath().parent_path());
        } catch (...) {
            return result;
        }
        if (result.loaded)
            Log("INFO", "package %ls (%ls) app=%ls entry=%ls, %zu in-package classes", result.fullName.c_str(),
                result.displayName.c_str(), result.applicationId.c_str(), result.entryPoint.c_str(),
                result.classes.size());
        else
            Log("INFO", "no AppxManifest.xml beside the game; using defaults");
        return result;
    }();
    return info;
}
const std::wstring& PackageDisplayName() noexcept {
    static const std::wstring name = [] {
        try {
            if (auto value = ResolveDisplayName(CurrentPackage()); !value.empty()) return value;
            return ExecutablePath().stem().wstring();
        } catch (...) {
            return std::wstring(L"Game");
        }
    }();
    return name;
}
}
namespace appx {
namespace {
// The shim and its proxies sit beside the game and name XAML classes themselves.
bool ShimModule(const std::wstring& name) {
    for (const wchar_t* own :
         {L"appxportable.dll", L"DWrite.dll", L"XAudio2_8.dll", L"XAudio2_9.dll", L"d3dcompiler_47.dll"})
        if (_wcsicmp(name.c_str(), own) == 0) return true;
    return false;
}
// Whether an image names Windows.UI.Xaml.Application, adding the DLLs it
// imports from the game folder to pending. Mapped as a resource: nothing runs.
bool NamesXamlApplication(const std::filesystem::path& file, std::vector<std::wstring>& pending) {
    HMODULE handle = LoadLibraryExW(file.c_str(), nullptr, LOAD_LIBRARY_AS_IMAGE_RESOURCE);
    if (!handle) return false;
    struct Unload {
        HMODULE handle;
        ~Unload() { FreeLibrary(handle); }
    } unload{handle};
    const auto* base = reinterpret_cast<const BYTE*>(reinterpret_cast<ULONG_PTR>(handle) & ~ULONG_PTR{3});
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return false;
    const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS32*>(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE) return false;
    const bool pe64 = nt->OptionalHeader.Magic == IMAGE_NT_OPTIONAL_HDR64_MAGIC;
    const auto* nt64 = reinterpret_cast<const IMAGE_NT_HEADERS64*>(nt);
    const DWORD imageSize = pe64 ? nt64->OptionalHeader.SizeOfImage : nt->OptionalHeader.SizeOfImage;
    const IMAGE_DATA_DIRECTORY imports = pe64 ? nt64->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT]
                                              : nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    for (auto* entry = reinterpret_cast<const IMAGE_IMPORT_DESCRIPTOR*>(base + imports.VirtualAddress);
         imports.VirtualAddress && reinterpret_cast<const BYTE*>(entry + 1) <= base + imageSize && entry->Name;
         ++entry) {
        if (entry->Name >= imageSize) break;
        const auto* name = reinterpret_cast<const char*>(base + entry->Name);
        const auto length = strnlen(name, imageSize - entry->Name);
        pending.emplace_back(name, name + length);
    }
    constexpr std::wstring_view needle = L"Windows.UI.Xaml.Application";
    const auto* bytes = reinterpret_cast<const BYTE*>(needle.data());
    const std::boyer_moore_horspool_searcher search(bytes, bytes + needle.size() * sizeof(wchar_t));
    const auto* section = IMAGE_FIRST_SECTION(nt);
    for (WORD i = 0; i < nt->FileHeader.NumberOfSections; ++i, ++section) {
        const DWORD start = section->VirtualAddress;
        const DWORD size = (std::min)((std::max)(section->Misc.VirtualSize, section->SizeOfRawData),
                                      start < imageSize ? imageSize - start : 0);
        if (std::search(base + start, base + start + size, search) != base + start + size) return true;
    }
    return false;
}
// XAML built in code, as Asphalt 8's WCPToolkit.dll does, ships no XAML
// files; the game's code still names the XAML Application class. Unity loads
// its player without importing it, so its CoreWindow builds stay CoreWindow.
bool CodeStartsXaml(const std::filesystem::path& executable) {
    const auto directory = executable.parent_path();
    std::vector<std::wstring> pending{executable.filename().wstring()}, seen;
    while (!pending.empty()) {
        const auto name = std::move(pending.back());
        pending.pop_back();
        if (ShimModule(name) || std::any_of(seen.begin(), seen.end(), [&](const std::wstring& known) {
                return _wcsicmp(known.c_str(), name.c_str()) == 0;
            }))
            continue;
        seen.push_back(name);
        const auto file = directory / name;
        if (GetFileAttributesW(file.c_str()) == INVALID_FILE_ATTRIBUTES) continue;
        if (NamesXamlApplication(file, pending)) {
            Log("INFO", "XAML: %ls names Windows.UI.Xaml.Application", name.c_str());
            return true;
        }
    }
    return false;
}
}
bool PackageHasXaml(const std::filesystem::path& executable) {
    const auto directory = executable.parent_path();
    std::error_code error;
    std::filesystem::recursive_directory_iterator files(
        directory, std::filesystem::directory_options::skip_permission_denied, error),
        end;
    for (; !error && files != end; files.increment(error)) {
        const auto extension = files->path().extension();
        if (_wcsicmp(extension.c_str(), L".xbf") == 0 || _wcsicmp(extension.c_str(), L".xaml") == 0) return true;
    }
    HANDLE file = CreateFileW((directory / L"resources.pri").c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr,
                              OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file != INVALID_HANDLE_VALUE) {
        std::string data(GetFileSize(file, nullptr), '\0');
        DWORD read = 0;
        const bool loaded =
            !data.empty() && ReadFile(file, data.data(), static_cast<DWORD>(data.size()), &read, nullptr);
        CloseHandle(file);
        data.resize(loaded ? read : 0);
        if (data.find(".xbf") != std::string::npos || data.find(std::string(".\0x\0b\0f\0", 8)) != std::string::npos)
            return true;
    }
    return CodeStartsXaml(executable);
}
}

#include "xaml/xbf_webview.h"
#include "xbf_webview_fixture.h"
#include <algorithm>
#include <cstdio>
#include <fstream>
#include <string_view>
static int failures;
#define CHECK(x)                                                                                                       \
    do {                                                                                                               \
        if (!(x)) {                                                                                                    \
            printf("FAIL %d: %s\n", __LINE__, #x);                                                                     \
            ++failures;                                                                                                \
        }                                                                                                              \
    } while (0)
int wmain(int argc, wchar_t** argv) {
    auto original = fixture::WebView();
    if (argc == 3 && (wcscmp(argv[1], L"--write-fixture") == 0 || wcscmp(argv[1], L"--write-panel-fixture") == 0)) {
        const auto bytes = wcscmp(argv[1], L"--write-fixture") == 0 ? original : fixture::BackgroundPanelPage();
        std::ofstream output(argv[2], std::ios::binary);
        output.write(reinterpret_cast<const char*>(bytes.data()), bytes.size());
        return output ? 0 : 1;
    }
    if (argc == 3 && wcscmp(argv[1], L"--check") == 0) {
        std::ifstream input(argv[2], std::ios::binary);
        if (!input) return 1;
        original.assign(std::istreambuf_iterator<char>(input), {});
        if (original.size() < 132) return 1;
    }
    std::vector<BYTE> adapted;
    CHECK(appx::RemapXbfStandIns(original.data(), original.size(), adapted) == S_OK);
    CHECK(adapted.size() == original.size());
    UINT32 metadataSize = 0;
    memcpy(&metadataSize, original.data() + 4, 4);
    if (adapted.size() == original.size()) {
        CHECK(memcmp(original.data(), adapted.data(), 132) == 0);
        CHECK(memcmp(original.data() + 12 + metadataSize, adapted.data() + 12 + metadataSize,
                     original.size() - 12 - metadataSize) == 0);
        std::vector<BYTE> twice;
        CHECK(appx::RemapXbfStandIns(adapted.data(), adapted.size(), twice) == S_FALSE && twice.empty());
    }
    if (argc == 3) return failures ? 1 : 0;
    const auto saved = original;
    for (size_t size = 0; size < original.size(); ++size) {
        CHECK(appx::RemapXbfStandIns(original.data(), size, adapted) != S_OK);
        CHECK(adapted.empty());
    }
    CHECK(original == saved);
    auto literal = fixture::WebView(true);
    CHECK(appx::RemapXbfStandIns(literal.data(), literal.size(), adapted) == E_NOTIMPL && adapted.empty());
    auto custom = fixture::WebView(false, false);
    CHECK(appx::RemapXbfStandIns(custom.data(), custom.size(), adapted) == S_FALSE && adapted.empty());
    auto reused = original;
    UINT64 typesOffset = 0;
    memcpy(&typesOffset, reused.data() + 44, 8);
    const UINT32 webViewString = 4;
    memcpy(reused.data() + 12 + typesOffset + 4 + 2 * 12 + 8, &webViewString, 4);
    CHECK(appx::RemapXbfStandIns(reused.data(), reused.size(), adapted) == E_NOTIMPL && adapted.empty());
    auto unsupported = original;
    unsupported.back() = 0xff;
    CHECK(appx::RemapXbfStandIns(unsupported.data(), unsupported.size(), adapted) == E_NOTIMPL && adapted.empty());
    auto invalid = original;
    invalid[20] = 0xff;
    CHECK(FAILED(appx::RemapXbfStandIns(invalid.data(), invalid.size(), adapted)) && adapted.empty());
    auto unknown = original;
    unknown[12] = 2;
    CHECK(appx::RemapXbfStandIns(unknown.data(), unknown.size(), adapted) == S_FALSE && adapted.empty());
    CHECK(appx::RemapXbfStandIns(nullptr, 0, adapted) == E_POINTER);
    // A built-in SwapChainBackgroundPanel takes its same-length alias in place.
    const auto panel = fixture::BackgroundPanelPage();
    CHECK(appx::RemapXbfStandIns(panel.data(), panel.size(), adapted) == S_OK);
    CHECK(adapted.size() == panel.size());
    const std::wstring_view alias = L"PortableSwapChainBGPanel", builtIn = L"SwapChainBackgroundPanel";
    auto count = [](const std::vector<BYTE>& bytes, std::wstring_view text) {
        const auto* begin = reinterpret_cast<const BYTE*>(text.data());
        const auto* end = begin + text.size() * sizeof(wchar_t);
        int found = 0;
        for (auto at = bytes.begin(); (at = std::search(at, bytes.end(), begin, end)) != bytes.end(); ++at) ++found;
        return found;
    };
    // The type name changes; the x:Name DXSwapChainBackgroundPanel, which contains it, does not.
    CHECK(count(panel, builtIn) == 2 && count(adapted, builtIn) == 1 && count(adapted, alias) == 1);
    CHECK(count(adapted, L"DXSwapChainBackgroundPanel") == 1);
    std::vector<BYTE> again;
    CHECK(appx::RemapXbfStandIns(adapted.data(), adapted.size(), again) == S_FALSE && again.empty());
    printf("xbfwebviewtest: %d failure(s)\n", failures);
    return failures ? 1 : 0;
}

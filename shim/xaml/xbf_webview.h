#pragma once
#include <windows.h>
#include <vector>
namespace appx {
// Preserve the file size and node stream while substituting same-length,
// metadata-only names for built-in types islands cannot host (WebView and
// SwapChainBackgroundPanel). S_FALSE means nothing needs adaptation.
HRESULT RemapXbfStandIns(const BYTE* bytes, size_t size, std::vector<BYTE>& adapted);
// The same substitution for UTF-8 source XAML, as Windows 8.0 packages ship:
// unprefixed element and property-element tags of those types get the
// same-length alias. S_FALSE means nothing needs adaptation.
HRESULT RemapSourceXamlStandIns(const BYTE* bytes, size_t size, std::vector<BYTE>& adapted);
}

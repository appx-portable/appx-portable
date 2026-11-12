#pragma once
#include <windows.h>
#include <vector>
namespace appx {
// Preserve the file size and node stream while substituting same-length,
// metadata-only names for built-in types islands cannot host (WebView and
// SwapChainBackgroundPanel). S_FALSE means nothing needs adaptation.
HRESULT RemapXbfStandIns(const BYTE* bytes, size_t size, std::vector<BYTE>& adapted);
}

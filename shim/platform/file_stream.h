#pragma once
#include <windows.storage.streams.h>
#include <string>
namespace appx {
// A random-access stream over an existing path. Clones and positioned streams
// retain the requested access mode and have independent cursors.
HRESULT OpenFileStream(const std::wstring& path, const std::wstring& contentType,
                       ABI::Windows::Storage::Streams::IRandomAccessStreamWithContentType** value,
                       bool writable = false);
}

#pragma once
#include <windows.h>
#include <objidl.h>
#include <string>
#include <vector>
namespace appx {
struct XbfConnection {
    UINT32 id = 0;
    std::wstring type, name;
    bool root = false, collapsed = false;
};
// XBF 1.0 (Windows 8.1) and 2.0/2.1 connection metadata for the portable native
// XAML backend. This does not render a visual tree. Deferred/custom-runtime-data
// opcodes are rejected, and nothing is returned unless the entire stream validates.
HRESULT ReadXbfConnections(IStream* stream, std::vector<XbfConnection>& connections);
// Every object with a connection ID or a name, in document order: Windows 8
// pages bind named fields by FindName, with or without a connection.
HRESULT ReadXbfObjects(IStream* stream, std::vector<XbfConnection>& objects);
}

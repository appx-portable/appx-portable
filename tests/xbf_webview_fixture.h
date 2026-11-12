#pragma once
#include <windows.h>
#include <array>
#include <cstring>
#include <string>
#include <vector>
namespace fixture {
template <class T> inline void Add(std::vector<BYTE>& bytes, T value) {
    const auto offset = bytes.size();
    bytes.resize(offset + sizeof(value));
    memcpy(bytes.data() + offset, &value, sizeof(value));
}
inline void String(std::vector<BYTE>& bytes, const std::wstring& value) {
    Add<UINT32>(bytes, static_cast<UINT32>(value.size()));
    for (auto character : value) Add<UINT16>(bytes, character);
}
using Tables = std::vector<std::array<UINT32, 3>>[6];
// An XBF v1.0 file from its strings, metadata tables and node stream.
inline std::vector<BYTE> Assemble(const std::vector<std::wstring>& strings, const Tables& tables,
                                  const std::vector<BYTE>& nodes) {
    std::vector<BYTE> metadata(120);
    metadata[0] = 1;
    for (size_t table = 0; table < 6; ++table) {
        const UINT64 offset = metadata.size();
        memcpy(metadata.data() + 8 + table * 8, &offset, sizeof(offset));
        if (table == 0) {
            Add<UINT32>(metadata, static_cast<UINT32>(strings.size()));
            for (const auto& string : strings) String(metadata, string);
        } else {
            Add<UINT32>(metadata, static_cast<UINT32>(tables[table].size()));
            const auto width = table == 3 || table == 4 ? 3 : table == 5 ? 1 : 2;
            for (const auto& row : tables[table])
                for (int column = 0; column < width; ++column) Add<UINT32>(metadata, row[column]);
        }
    }
    std::vector<BYTE> result;
    Add<UINT32>(result, 0x00464258);
    Add<UINT32>(result, static_cast<UINT32>(metadata.size()));
    Add<UINT32>(result, static_cast<UINT32>(nodes.size()));
    result.insert(result.end(), metadata.begin(), metadata.end());
    result.insert(result.end(), nodes.begin(), nodes.end());
    return result;
}
// Node stream writers shared by the fixtures.
struct Nodes {
    std::vector<BYTE> bytes;
    void Node(BYTE kind, UINT32 id) {
        Add<BYTE>(bytes, kind);
        Add<UINT32>(bytes, id);
        Add<UINT32>(bytes, 0);
    }
    void Namespaces() {
        Node(7, 0);
        String(bytes, L"");
        Node(7, 1);
        String(bytes, L"x");
    }
    void Integer(UINT32 property, INT32 value) {
        Node(3, property);
        Add<BYTE>(bytes, 6);
        Add<BYTE>(bytes, 4);
        Add<INT32>(bytes, value);
        Add<BYTE>(bytes, 4);
    }
    void Text(UINT32 property, UINT32 string) {
        Node(3, property);
        Node(5, string);
        Add<BYTE>(bytes, 4);
    }
    void Number(UINT32 property, float value) {
        Node(3, property);
        Add<BYTE>(bytes, 6);
        Add<BYTE>(bytes, 3);
        Add<float>(bytes, value);
        Add<BYTE>(bytes, 4);
    }
};
inline std::vector<BYTE> WebView(bool sharedLiteral = false, bool native = true) {
    const std::vector<std::wstring> strings{L"System.Windows",
                                            L"Windows.UI.Xaml.Controls",
                                            L"Windows.UI.Xaml",
                                            L"UserControl",
                                            L"WebView",
                                            L"FrameworkElement",
                                            L"Content",
                                            L"Name",
                                            L"ConnectionId",
                                            L"DefaultBackgroundColor",
                                            L"Width",
                                            L"Height",
                                            L"Transparent",
                                            L"articleView",
                                            L"http://schemas.microsoft.com/winfx/2006/xaml/presentation",
                                            L"http://schemas.microsoft.com/winfx/2006/xaml"};
    const std::vector<std::array<UINT32, 3>> tables[6] = {
        {},
        {{1, 0, 0}},
        {{0, 1, 0}, {0, 2, 0}},
        {{0, 0, 3}, {0, static_cast<UINT32>(native ? 0 : 1), 4}, {0, 1, 5}},
        {{2, 0, 8}, {2, 0, 7}, {0, 0, 6}, {0, 1, 9}, {0, 2, 10}, {0, 2, 11}},
        {{14, 0, 0}, {15, 0, 0}}};
    Nodes nodes;
    nodes.Namespaces();
    nodes.Node(1, 0);
    nodes.Integer(0, 1); // Existing UserControl root, connector ID 1.
    nodes.Node(3, 2);
    nodes.Node(1, 1);
    nodes.Integer(0, 2);                   // Content WebView, connector ID 2.
    nodes.Text(1, sharedLiteral ? 4 : 13); // Same string used as literal must prevent remapping.
    nodes.Number(4, 500);
    nodes.Number(5, 300);
    nodes.Text(3, 12);
    Add<BYTE>(nodes.bytes, 2);
    Add<BYTE>(nodes.bytes, 4);
    Add<BYTE>(nodes.bytes, 2);
    return Assemble(strings, tables, nodes.bytes);
}
// A Page whose content is a built-in SwapChainBackgroundPanel, as Unity's
// Windows 8.1 template declares it (Skiing Fred 1.1.0.11's MainPage).
inline std::vector<BYTE> BackgroundPanelPage() {
    const std::vector<std::wstring> strings{L"System.Windows",
                                            L"Windows.UI.Xaml.Controls",
                                            L"Page",
                                            L"SwapChainBackgroundPanel",
                                            L"Content",
                                            L"Name",
                                            L"ConnectionId",
                                            L"DXSwapChainBackgroundPanel",
                                            L"http://schemas.microsoft.com/winfx/2006/xaml/presentation",
                                            L"http://schemas.microsoft.com/winfx/2006/xaml"};
    const Tables tables = {{},
                           {{1, 0, 0}},
                           {{0, 1, 0}},
                           {{0, 0, 2}, {0, 0, 3}},
                           {{2, 0, 6}, {2, 0, 5}, {0, 0, 4}},
                           {{8, 0, 0}, {9, 0, 0}}};
    Nodes nodes;
    nodes.Namespaces();
    nodes.Node(1, 0);
    nodes.Integer(0, 1); // Page root, connector ID 1.
    nodes.Node(3, 2);
    nodes.Node(1, 1);
    nodes.Integer(0, 2); // Content panel, connector ID 2.
    nodes.Text(1, 7);
    Add<BYTE>(nodes.bytes, 2);
    Add<BYTE>(nodes.bytes, 4);
    Add<BYTE>(nodes.bytes, 2);
    return Assemble(strings, tables, nodes.bytes);
}
}

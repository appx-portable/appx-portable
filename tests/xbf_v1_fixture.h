#pragma once
// A Windows 8.1 (XBF 1.0) page shaped like Minion Rush's DirectXPage: a page
// (connection 1) whose SwapChainBackgroundPanel holds a collapsed TextBox
// (connection 2, AgeTextBox) and an Image named only (cursor).
#include <windows.h>
#include <cstring>
#include <string>
#include <vector>
namespace xbf_v1 {
enum class Defect { None, UnknownNode, UnknownValue, BadType };
template <class T> void Add(std::vector<BYTE>& bytes, T value) {
    const auto start = bytes.size();
    bytes.resize(start + sizeof(value));
    memcpy(bytes.data() + start, &value, sizeof(value));
}
inline void String(std::vector<BYTE>& bytes, const std::wstring& value) {
    Add<UINT32>(bytes, static_cast<UINT32>(value.size()));
    for (auto character : value) Add<UINT16>(bytes, character);
}
inline std::vector<BYTE> Fixture(Defect defect = Defect::None) {
    const std::vector<std::wstring> strings = {L"Page",
                                               L"SwapChainBackgroundPanel",
                                               L"UIElementCollection",
                                               L"TextBox",
                                               L"Image",
                                               L"ConnectionId",
                                               L"Name",
                                               L"Children",
                                               L"__implicit_items",
                                               L"Visibility",
                                               L"Collapsed",
                                               L"Margin",
                                               L"Height",
                                               L"IsEnabled",
                                               L"presentation"};
    std::vector<BYTE> metadata(120), nodes;
    metadata[0] = 1; // Major version 1, minor 0.
    for (size_t table = 0; table < 6; ++table) {
        const UINT64 offset = metadata.size();
        memcpy(metadata.data() + 8 + table * 8, &offset, 8);
        switch (table) {
        case 0:
            Add<UINT32>(metadata, static_cast<UINT32>(strings.size()));
            for (const auto& value : strings) String(metadata, value);
            break;
        case 3: // Types: Page .. Image.
            Add<UINT32>(metadata, 5);
            for (UINT32 i = 0; i < 5; ++i) {
                Add<UINT32>(metadata, 0);
                Add<UINT32>(metadata, 0);
                Add<UINT32>(metadata, i);
            }
            break;
        case 4: // Properties: ConnectionId .. IsEnabled, without Collapsed.
            Add<UINT32>(metadata, 8);
            for (UINT32 name : {5u, 6u, 7u, 8u, 9u, 11u, 12u, 13u}) {
                Add<UINT32>(metadata, 0);
                Add<UINT32>(metadata, 0);
                Add<UINT32>(metadata, name);
            }
            break;
        case 5:
            Add<UINT32>(metadata, 1);
            Add<UINT32>(metadata, 14);
            break;
        default:
            Add<UINT32>(metadata, 0);
        }
    }
    auto object = [&](UINT32 type, UINT32 flags = 0) {
        Add<BYTE>(nodes, 0x01);
        Add<UINT32>(nodes, type);
        Add<UINT32>(nodes, flags);
    };
    auto property = [&](UINT32 id) {
        Add<BYTE>(nodes, 0x03);
        Add<UINT32>(nodes, id);
        Add<UINT32>(nodes, 0);
    };
    auto number = [&](UINT32 id, INT32 value) {
        property(id);
        Add<BYTE>(nodes, 0x06);
        Add<BYTE>(nodes, 4);
        Add<INT32>(nodes, value);
        Add<BYTE>(nodes, 0x04);
    };
    auto name = [&](const std::wstring& value) {
        property(1);
        Add<BYTE>(nodes, 0x06);
        Add<BYTE>(nodes, 5);
        String(nodes, value);
        Add<BYTE>(nodes, 0x04);
    };
    auto line = [&] {
        Add<BYTE>(nodes, 0x0a);
        Add<INT16>(nodes, 1);
        Add<INT16>(nodes, 5);
    };
    Add<BYTE>(nodes, 0x07); // xmlns="...presentation"
    Add<UINT32>(nodes, 0);
    Add<UINT32>(nodes, 0);
    String(nodes, L"");
    line();
    object(0); // Page
    number(0, 1);
    property(2);
    object(defect == Defect::BadType ? 9 : 1); // SwapChainBackgroundPanel
    name(L"SwapChainPanel");
    property(2);
    object(2, 1); // UIElementCollection
    property(3);
    line();
    object(3); // TextBox
    number(0, 2);
    name(L"AgeTextBox");
    property(4);
    Add<BYTE>(nodes, 0x05); // Visibility="Collapsed" from the string table.
    Add<UINT32>(nodes, 10);
    Add<UINT32>(nodes, 0);
    Add<BYTE>(nodes, 0x04);
    property(5);
    Add<BYTE>(nodes, 0x06);
    Add<BYTE>(nodes, 7); // Thickness.
    for (float side : {622.0f, 161.0f, 0.0f, 0.0f}) Add<float>(nodes, side);
    Add<BYTE>(nodes, 0x04);
    property(7);
    Add<BYTE>(nodes, 0x06);
    Add<BYTE>(nodes, defect == Defect::UnknownValue ? 0x7f : 1); // False.
    Add<BYTE>(nodes, 0x04);
    Add<BYTE>(nodes, 0x02);
    if (defect == Defect::UnknownNode) Add<BYTE>(nodes, 0x7f);
    object(4); // Image
    name(L"cursor");
    property(6);
    Add<BYTE>(nodes, 0x06);
    Add<BYTE>(nodes, 8);
    Add<float>(nodes, 40.0f);
    Add<BYTE>(nodes, 0x04);
    Add<BYTE>(nodes, 0x02);
    for (int scope = 0; scope < 3; ++scope) {
        Add<BYTE>(nodes, 0x04);
        Add<BYTE>(nodes, 0x02);
    }
    std::vector<BYTE> bytes;
    Add<UINT32>(bytes, 0x00464258);
    Add<UINT32>(bytes, static_cast<UINT32>(metadata.size()));
    Add<UINT32>(bytes, static_cast<UINT32>(nodes.size()));
    bytes.insert(bytes.end(), metadata.begin(), metadata.end());
    bytes.insert(bytes.end(), nodes.begin(), nodes.end());
    return bytes;
}
}

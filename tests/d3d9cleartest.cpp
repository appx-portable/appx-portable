// WPF clears dirty viewports away from the render target's origin when a pane
// moves. The clear must reach those pixels and preserve pixels outside it.
#include <windows.h>
#include <d3d9.h>
#include <cstdio>
#include <wrl/client.h>
using Microsoft::WRL::ComPtr;
namespace {
constexpr UINT size = 32;
bool Check(HRESULT hr, const char* step) {
    if (SUCCEEDED(hr)) return true;
    printf("d3d9 clear: %s hr=%08lx\n", step, hr);
    return false;
}
bool ClearViewport(IDirect3DDevice9* device, IDirect3DSurface9* target, IDirect3DSurface9* readback, UINT x, UINT y,
                   const RECT* rect) {
    D3DVIEWPORT9 full{0, 0, size, size, 0, 1};
    D3DVIEWPORT9 viewport{x, y, 16, 12, 0, 1};
    if (!Check(device->SetViewport(&full), "full viewport") ||
        !Check(device->Clear(0, nullptr, D3DCLEAR_TARGET, 0xff0000ff, 1, 0), "blue background") ||
        !Check(device->SetViewport(&viewport), "dirty viewport"))
        return false;
    D3DRECT box{};
    if (rect) box = D3DRECT{rect->left, rect->top, rect->right, rect->bottom};
    if (!Check(device->Clear(rect ? 1 : 0, rect ? &box : nullptr, D3DCLEAR_TARGET, 0, 1, 0), "transparent clear") ||
        !Check(device->GetRenderTargetData(target, readback), "readback"))
        return false;
    D3DLOCKED_RECT locked{};
    if (!Check(readback->LockRect(&locked, nullptr, D3DLOCK_READONLY), "lock")) return false;
    unsigned errors = 0;
    for (UINT row = 0; row < size; ++row)
        for (UINT col = 0; col < size; ++col) {
            const bool inViewport = col >= x && col < x + 16 && row >= y && row < y + 12;
            const bool inRect =
                !rect || (col >= static_cast<UINT>(rect->left) && col < static_cast<UINT>(rect->right) &&
                          row >= static_cast<UINT>(rect->top) && row < static_cast<UINT>(rect->bottom));
            const DWORD expected = inViewport && inRect ? 0 : 0xff0000ff;
            const DWORD pixel =
                reinterpret_cast<const DWORD*>(static_cast<const BYTE*>(locked.pBits) + row * locked.Pitch)[col];
            if (pixel != expected) ++errors;
        }
    readback->UnlockRect();
    printf("d3d9 clear: viewport %u,%u explicit=%d wrong=%u\n", x, y, rect != nullptr, errors);
    return !errors;
}
}
int main() {
    HWND window =
        CreateWindowW(L"STATIC", L"clear viewport", WS_POPUP, 0, 0, size, size, nullptr, nullptr, nullptr, nullptr);
    ComPtr<IDirect3D9> d3d;
    d3d.Attach(Direct3DCreate9(D3D_SDK_VERSION));
    if (!window || !d3d) return 1;
    D3DPRESENT_PARAMETERS pp{};
    pp.Windowed = TRUE;
    pp.SwapEffect = D3DSWAPEFFECT_DISCARD;
    pp.hDeviceWindow = window;
    pp.BackBufferWidth = pp.BackBufferHeight = size;
    pp.BackBufferFormat = D3DFMT_A8R8G8B8;
    ComPtr<IDirect3DDevice9> device;
    ComPtr<IDirect3DSurface9> target, readback;
    if (!Check(d3d->CreateDevice(0, D3DDEVTYPE_HAL, window, D3DCREATE_SOFTWARE_VERTEXPROCESSING, &pp, &device),
               "device") ||
        !Check(device->CreateRenderTarget(size, size, D3DFMT_A8R8G8B8, D3DMULTISAMPLE_NONE, 0, FALSE, &target, nullptr),
               "target") ||
        !Check(device->CreateOffscreenPlainSurface(size, size, D3DFMT_A8R8G8B8, D3DPOOL_SYSTEMMEM, &readback, nullptr),
               "readback surface") ||
        !Check(device->SetRenderTarget(0, target.Get()), "bind target"))
        return 1;
    bool passed = true;
    const RECT rect{10, 6, 31, 31}; // clipped to the offset viewport
    passed = ClearViewport(device.Get(), target.Get(), readback.Get(), 0, 0, nullptr) && passed;
    passed = ClearViewport(device.Get(), target.Get(), readback.Get(), 8, 0, nullptr) && passed;
    passed = ClearViewport(device.Get(), target.Get(), readback.Get(), 0, 4, nullptr) && passed;
    passed = ClearViewport(device.Get(), target.Get(), readback.Get(), 8, 4, nullptr) && passed;
    passed = ClearViewport(device.Get(), target.Get(), readback.Get(), 8, 4, &rect) && passed;
    return passed ? 0 : 1;
}

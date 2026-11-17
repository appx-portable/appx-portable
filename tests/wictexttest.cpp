// Direct2D's independent WIC bitmap target, as used for Cocos2D font atlases.
#include <windows.h>
#include <d2d1.h>
#include <dwrite.h>
#include <wincodec.h>
#include <wrl.h>
#include <cstdio>
using Microsoft::WRL::ComPtr;
static bool Check(HRESULT hr, const char* name) {
    if (SUCCEEDED(hr)) return true;
    printf("wictexttest: %s hr=%08lx\n", name, hr);
    return false;
}
int main() {
    if (!Check(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED), "COM")) return 1;
    ComPtr<IWICImagingFactory> imaging;
    ComPtr<IWICBitmap> bitmap;
    ComPtr<ID2D1Factory> factory;
    ComPtr<IDWriteFactory> write;
    using CreateWrite = HRESULT(WINAPI*)(DWRITE_FACTORY_TYPE, REFIID, IUnknown**);
    auto createWrite = reinterpret_cast<CreateWrite>(GetProcAddress(LoadLibraryW(L"dwrite.dll"), "DWriteCreateFactory"));
    if (!createWrite) return 1;
    if (!Check(CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&imaging)), "WIC") ||
        !Check(imaging->CreateBitmap(160, 48, GUID_WICPixelFormat32bppPBGRA, WICBitmapCacheOnLoad, &bitmap), "bitmap") ||
        !Check(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, factory.GetAddressOf()), "D2D") ||
        !Check(createWrite(DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory), reinterpret_cast<IUnknown**>(write.GetAddressOf())), "DWrite")) return 1;
    const auto properties = D2D1::RenderTargetProperties(D2D1_RENDER_TARGET_TYPE_DEFAULT,
        D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED));
    ComPtr<ID2D1RenderTarget> target;
    if (!Check(factory->CreateWicBitmapRenderTarget(bitmap.Get(), properties, &target), "WIC render target")) return 1;
    ComPtr<ID2D1SolidColorBrush> brush;
    ComPtr<IDWriteTextFormat> format;
    if (!Check(target->CreateSolidColorBrush(D2D1::ColorF(D2D1::ColorF::White), &brush), "brush") ||
        !Check(write->CreateTextFormat(L"Arial", nullptr, DWRITE_FONT_WEIGHT_NORMAL, DWRITE_FONT_STYLE_NORMAL,
            DWRITE_FONT_STRETCH_NORMAL, 24, L"en-US", &format), "font")) return 1;
    target->BeginDraw();
    target->Clear(D2D1::ColorF(0, 0, 0, 0));
    target->DrawText(L"Wine text", 9, format.Get(), D2D1::RectF(0, 0, 160, 48), brush.Get());
    if (!Check(target->EndDraw(), "draw")) return 1;
    ComPtr<IWICBitmapLock> locked;
    WICRect rect{0, 0, 160, 48};
    if (!Check(bitmap->Lock(&rect, WICBitmapLockRead, &locked), "lock")) return 1;
    UINT stride{}, length{}; BYTE* pixels{};
    if (!Check(locked->GetStride(&stride), "stride") || !Check(locked->GetDataPointer(&length, &pixels), "pixels")) return 1;
    unsigned coverage = 0;
    for (UINT y = 0; y < 48; ++y) for (UINT x = 0; x < 160; ++x) if (pixels[y * stride + x * 4 + 3]) ++coverage;
    if (coverage < 20) { printf("wictexttest: empty text bitmap coverage=%u\n", coverage); return 1; }
    printf("PASS: WIC render target draws text into bitmap coverage=%u\n", coverage);
    return 0;
}

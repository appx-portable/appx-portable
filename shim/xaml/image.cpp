// IImage's ABI still contains the deprecated PlayToSource getter.
#ifdef __clang__
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
#else
#pragma warning(disable : 4996)
#endif
#include "element.h"
#include "com/factory.h"
#include "registry.h"
#include <windows.ui.xaml.controls.h>
#include <windows.ui.xaml.media.imaging.h>
using namespace Microsoft::WRL;
using namespace ABI::Windows::UI::Xaml;
using namespace ABI::Windows::UI::Xaml::Controls;
namespace appx {
// Overlay image state for native DirectX views, without an OS XAML core.
// The desktop host does not render XAML image overlays yet.
class Image final : public Element<Image, IImage> {
    ComPtr<Media::IImageSource> source_;
    Media::Stretch stretch_ = Media::Stretch_Uniform;
    Thickness nineGrid_{};
    EventSource<IExceptionRoutedEventHandler> failed_;
    EventSource<IRoutedEventHandler> opened_;

public:
    static constexpr const wchar_t* Name = L"Windows.UI.Xaml.Controls.Image";
    explicit Image(IInspectable* outer) : Element(outer) {}
    HRESULT STDMETHODCALLTYPE get_Source(Media::IImageSource** value) override {
        if (!value) return E_POINTER;
        return source_.CopyTo(value);
    }
    HRESULT STDMETHODCALLTYPE put_Source(Media::IImageSource* value) override {
        source_ = value;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_Stretch(Media::Stretch* value) override {
        if (!value) return E_POINTER;
        *value = stretch_;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE put_Stretch(Media::Stretch value) override {
        if (value < Media::Stretch_None || value > Media::Stretch_UniformToFill) return E_INVALIDARG;
        stretch_ = value;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_NineGrid(Thickness* value) override {
        if (!value) return E_POINTER;
        *value = nineGrid_;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE put_NineGrid(Thickness value) override {
        nineGrid_ = value;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_PlayToSource(ABI::Windows::Media::PlayTo::IPlayToSource** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE add_ImageFailed(IExceptionRoutedEventHandler* handler,
                                              EventRegistrationToken* token) override {
        return failed_.Add(handler, token);
    }
    HRESULT STDMETHODCALLTYPE remove_ImageFailed(EventRegistrationToken token) override {
        return failed_.Remove(token);
    }
    HRESULT STDMETHODCALLTYPE add_ImageOpened(IRoutedEventHandler* handler, EventRegistrationToken* token) override {
        return opened_.Add(handler, token);
    }
    HRESULT STDMETHODCALLTYPE remove_ImageOpened(EventRegistrationToken token) override {
        return opened_.Remove(token);
    }
};
HRESULT MakeImage(IInspectable** value) {
    if (!value) return E_POINTER;
    *value = nullptr;
    ComPtr<IImage> image;
    const auto hr = Compose<Image>(nullptr, nullptr, image.GetAddressOf());
    return FAILED(hr) ? hr : image.CopyTo(value);
}
class ImageFactory final : public Factory<> {
public:
    ImageFactory() : Factory(Image::Name) {}
    HRESULT STDMETHODCALLTYPE ActivateInstance(IInspectable** value) override { return MakeImage(value); }
};
HRESULT MakeImageFactory(IActivationFactory** value) {
    auto factory = Make<ImageFactory>();
    return factory ? factory.CopyTo(value) : E_OUTOFMEMORY;
}
const ShimClass imageClass{Image::Name, MakeImageFactory};
// A BitmapImage records its source for the Image it is given to. Windows' own
// needs the OS XAML core and fails on the native host's thread with
// RPC_E_WRONG_THREAD; Minion Rush builds one for the Xbox avatar every frame of
// its achievements screen. Nothing decodes here: the desktop host does not
// draw XAML images, so the size stays 0 and ImageOpened/ImageFailed never fire.
class BitmapImage final : public Composable<BitmapImage, Media::Imaging::IBitmapImage, Media::Imaging::IBitmapSource,
                                            Media::IImageSource, DefaultIDependencyObject> {
    ComPtr<ABI::Windows::Foundation::IUriRuntimeClass> uri_;
    Media::Imaging::BitmapCreateOptions options_ = Media::Imaging::BitmapCreateOptions_None;
    INT32 decodeWidth_ = 0, decodeHeight_ = 0;
    EventSource<Media::Imaging::IDownloadProgressEventHandler> progress_;
    EventSource<IRoutedEventHandler> opened_;
    EventSource<IExceptionRoutedEventHandler> failed_;
    static HRESULT Get(INT32 field, INT32* value) {
        if (!value) return E_POINTER;
        *value = field;
        return S_OK;
    }
    static HRESULT Put(INT32& field, INT32 value) {
        if (value < 0) return E_INVALIDARG;
        field = value;
        return S_OK;
    }

public:
    static constexpr const wchar_t* Name = L"Windows.UI.Xaml.Media.Imaging.BitmapImage";
    explicit BitmapImage(IInspectable* outer) : Composable(outer) {}
    HRESULT STDMETHODCALLTYPE get_CreateOptions(Media::Imaging::BitmapCreateOptions* value) override {
        if (!value) return E_POINTER;
        *value = options_;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE put_CreateOptions(Media::Imaging::BitmapCreateOptions value) override {
        options_ = value;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_UriSource(ABI::Windows::Foundation::IUriRuntimeClass** value) override {
        if (!value) return E_POINTER;
        return uri_.CopyTo(value);
    }
    HRESULT STDMETHODCALLTYPE put_UriSource(ABI::Windows::Foundation::IUriRuntimeClass* value) override {
        uri_ = value;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_DecodePixelWidth(INT32* value) override { return Get(decodeWidth_, value); }
    HRESULT STDMETHODCALLTYPE put_DecodePixelWidth(INT32 value) override { return Put(decodeWidth_, value); }
    HRESULT STDMETHODCALLTYPE get_DecodePixelHeight(INT32* value) override { return Get(decodeHeight_, value); }
    HRESULT STDMETHODCALLTYPE put_DecodePixelHeight(INT32 value) override { return Put(decodeHeight_, value); }
    HRESULT STDMETHODCALLTYPE add_DownloadProgress(Media::Imaging::IDownloadProgressEventHandler* handler,
                                                   EventRegistrationToken* token) override {
        return progress_.Add(handler, token);
    }
    HRESULT STDMETHODCALLTYPE remove_DownloadProgress(EventRegistrationToken token) override {
        return progress_.Remove(token);
    }
    HRESULT STDMETHODCALLTYPE add_ImageOpened(IRoutedEventHandler* handler, EventRegistrationToken* token) override {
        return opened_.Add(handler, token);
    }
    HRESULT STDMETHODCALLTYPE remove_ImageOpened(EventRegistrationToken token) override {
        return opened_.Remove(token);
    }
    HRESULT STDMETHODCALLTYPE add_ImageFailed(IExceptionRoutedEventHandler* handler,
                                              EventRegistrationToken* token) override {
        return failed_.Add(handler, token);
    }
    HRESULT STDMETHODCALLTYPE remove_ImageFailed(EventRegistrationToken token) override {
        return failed_.Remove(token);
    }
    HRESULT STDMETHODCALLTYPE get_PixelWidth(INT32* value) override { return Get(0, value); }
    HRESULT STDMETHODCALLTYPE get_PixelHeight(INT32* value) override { return Get(0, value); }
    HRESULT STDMETHODCALLTYPE SetSource(ABI::Windows::Storage::Streams::IRandomAccessStream* stream) override {
        return stream ? S_OK : E_INVALIDARG;
    }
    HRESULT STDMETHODCALLTYPE SetSourceAsync(ABI::Windows::Storage::Streams::IRandomAccessStream*,
                                             ABI::Windows::Foundation::IAsyncAction**) override {
        return E_NOTIMPL;
    }
};
class BitmapImageFactory final : public Factory<Media::Imaging::IBitmapImageFactory> {
public:
    BitmapImageFactory() : Factory(BitmapImage::Name) {}
    HRESULT STDMETHODCALLTYPE ActivateInstance(IInspectable** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        ComPtr<Media::Imaging::IBitmapImage> bitmap;
        const auto hr = Compose<BitmapImage>(nullptr, nullptr, bitmap.GetAddressOf());
        return FAILED(hr) ? hr : bitmap.CopyTo(value);
    }
    HRESULT STDMETHODCALLTYPE CreateInstanceWithUriSource(ABI::Windows::Foundation::IUriRuntimeClass* uri,
                                                          Media::Imaging::IBitmapImage** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        ComPtr<Media::Imaging::IBitmapImage> bitmap;
        auto hr = Compose<BitmapImage>(nullptr, nullptr, bitmap.GetAddressOf());
        if (SUCCEEDED(hr)) hr = bitmap->put_UriSource(uri);
        return FAILED(hr) ? hr : bitmap.CopyTo(value);
    }
};
HRESULT MakeBitmapImageFactory(IActivationFactory** value) {
    auto factory = Make<BitmapImageFactory>();
    return factory ? factory.CopyTo(value) : E_OUTOFMEMORY;
}
const ShimClass bitmapImageClass{BitmapImage::Name, MakeBitmapImageFactory};
}

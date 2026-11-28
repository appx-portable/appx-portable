// Completion delivery for the shim's finished async operations. A handler the
// UI thread registers runs on a later UI turn; one registered on another thread
// runs there, as Windows' free-threaded operations complete, even while the UI
// thread waits for it. Practical English Usage's dictionary engine starts that
// way: OnLaunched blocks on an event a thread-pool continuation sets.
#include <windows.h>
#include <roapi.h>
#include <windows.storage.h>
#undef GetCurrentTime
#include <windows.ui.xaml.h>
#include <wrl.h>
#include <wrl/wrappers/corewrappers.h>
#include <cstdio>
#include <thread>
using namespace Microsoft::WRL;
using namespace Microsoft::WRL::Wrappers;
using namespace ABI::Windows::Foundation;
using namespace ABI::Windows::Storage;
using FileOperation = __FIAsyncOperation_1_Windows__CStorage__CStorageFile;
using FileCompleted = __FIAsyncOperationCompletedHandler_1_Windows__CStorage__CStorageFile;

int main() {
    RoInitialize(RO_INIT_MULTITHREADED);
    auto* dll = LoadLibraryW(L"appxportable.dll");
    if (!dll) return 1;
    auto get = reinterpret_cast<HRESULT(WINAPI*)(HSTRING, IActivationFactory**)>(GetProcAddress(dll, "ShimGetFactory"));
    if (!get) return 2;
    wchar_t self[MAX_PATH];
    if (!GetModuleFileNameW(nullptr, self, MAX_PATH)) return 3;
    ComPtr<IActivationFactory> fileFactory, applicationFactory;
    ComPtr<IStorageFileStatics> files;
    ComPtr<ABI::Windows::UI::Xaml::IApplicationStatics> applicationStatics;
    ComPtr<ABI::Windows::UI::Xaml::IApplicationFactory> compose;
    if (FAILED(get(HStringReference(L"Windows.Storage.StorageFile").Get(), &fileFactory)) ||
        FAILED(fileFactory.As(&files)) ||
        FAILED(get(HStringReference(L"Windows.UI.Xaml.Application").Get(), &applicationFactory)) ||
        FAILED(applicationFactory.As(&applicationStatics)) || FAILED(applicationFactory.As(&compose)))
        return 4;
    int failure = 0;
    bool started = false;
    ComPtr<ABI::Windows::UI::Xaml::IApplication> application;
    auto initialize = Callback<ABI::Windows::UI::Xaml::IApplicationInitializationCallback>(
        [&](ABI::Windows::UI::Xaml::IApplicationInitializationCallbackParams*) -> HRESULT {
            started = true;
            auto hr = compose->CreateInstance(nullptr, nullptr, &application);
            if (FAILED(hr)) return hr;
            const DWORD ui = GetCurrentThreadId();
            // Off the UI thread: the handler runs while the UI thread waits.
            HANDLE done = CreateEventW(nullptr, TRUE, FALSE, nullptr);
            if (!done) return HRESULT_FROM_WIN32(GetLastError());
            DWORD handlerThread = 0;
            std::thread worker([&] {
                RoInitialize(RO_INIT_MULTITHREADED);
                ComPtr<FileOperation> operation;
                if (FAILED(files->GetFileFromPathAsync(HStringReference(self).Get(), &operation)) ||
                    FAILED(operation->put_Completed(Callback<FileCompleted>([&](FileOperation* finished, AsyncStatus status) -> HRESULT {
                        ComPtr<IStorageFile> file;
                        if (status != AsyncStatus::Completed || FAILED(finished->GetResults(&file)) || !file) failure = 11;
                        handlerThread = GetCurrentThreadId();
                        SetEvent(done);
                        return S_OK;
                    }).Get())))
                    failure = 12;
                RoUninitialize();
            });
            const bool delivered = WaitForSingleObject(done, 5000) == WAIT_OBJECT_0;
            worker.join();
            CloseHandle(done);
            if (!delivered) failure = 13;
            else if (handlerThread == ui) failure = 14;
            // On the UI thread: the handler runs on a later UI turn, after put_Completed returns.
            ComPtr<FileOperation> operation;
            bool registered = false;
            hr = files->GetFileFromPathAsync(HStringReference(self).Get(), &operation);
            if (SUCCEEDED(hr))
                hr = operation->put_Completed(Callback<FileCompleted>([&, ui](FileOperation*, AsyncStatus) -> HRESULT {
                    if (!registered || GetCurrentThreadId() != ui) failure = 15;
                    PostQuitMessage(0);
                    return S_OK;
                }).Get());
            registered = true;
            return hr;
        });
    if (FAILED(applicationStatics->Start(initialize.Get())) || !started) return failure ? failure : 5;
    if (failure) {
        printf("completiontest: failure %d\n", failure);
        return failure;
    }
    puts("completiontest: 0 failure(s)");
    return 0;
}

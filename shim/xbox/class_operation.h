#pragma once
#include "marketplace_abi.h"
#include "com/async_id.h"
#include "host/host.h"
#include <wrl.h>
#include <mutex>
namespace appx::xbox {
using namespace Microsoft::WRL;
using namespace ABI::Windows::Foundation;
// A completed IAsyncOperation<Xbox runtime class>, retaining a stable result
// and dispatching completion on the same host queue as CurrentApp purchases.
template <class T>
class ClassOperation final : public RuntimeClass<RuntimeClassFlags<WinRt>, OperationAbi<T>, IAsyncInfo, FtmBase> {
    using RuntimeClassT = RuntimeClass<RuntimeClassFlags<WinRt>, OperationAbi<T>, IAsyncInfo, FtmBase>;
    InspectableClass(L"Windows.Foundation.IAsyncOperation`1", BaseTrust);
    ComPtr<T> result_;
    ComPtr<IUnknown> handler_;
    std::mutex mutex_;
    bool assigned_ = false, closed_ = false;
    const UINT32 id_ = NextAsyncId();

public:
    explicit ClassOperation(ComPtr<T> result) : result_(std::move(result)) {}
    HRESULT STDMETHODCALLTYPE put_Completed(IUnknown* handler) override {
        ComPtr<IUnknown> callback(handler);
        {
            std::lock_guard<std::mutex> lock(mutex_);
            if (closed_) return RO_E_CLOSED;
            if (assigned_) return E_ILLEGAL_DELEGATE_ASSIGNMENT;
            assigned_ = true;
            handler_ = callback;
        }
        if (callback) {
            ComPtr<ClassOperation> self(this);
            auto invoke = [self, callback] {
                using Invoke = HRESULT(STDMETHODCALLTYPE*)(IUnknown*, IInspectable*, AsyncStatus);
                reinterpret_cast<Invoke>((*reinterpret_cast<void***>(callback.Get()))[3])(
                    callback.Get(), static_cast<OperationAbi<T>*>(self.Get()), AsyncStatus::Completed);
            };
            if (FAILED(PostHostWork(0, invoke))) invoke();
        }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_Completed(IUnknown** out) override {
        if (!out) return E_POINTER;
        *out = nullptr;
        std::lock_guard<std::mutex> lock(mutex_);
        return closed_ ? RO_E_CLOSED : handler_.CopyTo(out);
    }
    HRESULT STDMETHODCALLTYPE GetResults(T** out) override {
        if (!out) return E_POINTER;
        *out = nullptr;
        std::lock_guard<std::mutex> lock(mutex_);
        return closed_ ? RO_E_CLOSED : result_.CopyTo(out);
    }
    HRESULT STDMETHODCALLTYPE get_Id(UINT32* out) override { return Info(id_, out); }
    HRESULT STDMETHODCALLTYPE get_Status(AsyncStatus* out) override { return Info(AsyncStatus::Completed, out); }
    HRESULT STDMETHODCALLTYPE get_ErrorCode(HRESULT* out) override { return Info(S_OK, out); }
    template <class V> HRESULT Info(V value, V* out) {
        if (!out) return E_POINTER;
        std::lock_guard<std::mutex> lock(mutex_);
        if (closed_) return RO_E_CLOSED;
        *out = value;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE Cancel() override {
        std::lock_guard<std::mutex> lock(mutex_);
        return closed_ ? RO_E_CLOSED : S_OK;
    }
    HRESULT STDMETHODCALLTYPE Close() override {
        ComPtr<IUnknown> releasing;
        {
            std::lock_guard<std::mutex> lock(mutex_);
            closed_ = true;
            releasing.Swap(handler_);
        }
        return S_OK;
    }
};
template <class T> HRESULT Success(ComPtr<T> result, IInspectable** out) {
    if (!out) return E_POINTER;
    *out = nullptr;
    if (!result) return E_OUTOFMEMORY;
    return Make<ClassOperation<T>>(std::move(result)).CopyTo(out);
}
}

// Windows.Devices.Enumeration.Pnp.PnpObject, deferring to Windows. Wine has
// no PnpObject class, and C++/CX callers throw when its factory is missing.
// Unity's Store players list devices through it while starting their main
// thread (driver details for SystemInfo, input devices through a watcher), so
// that thread ended before the first frame in Hitman GO and Lara Croft GO.
// Without the OS class, queries find no objects and watchers complete empty.
#include "generated/device_enumeration_pnp.h"
#include "com/completed_operation.h"
#include "com/factory.h"
#include "registry.h"
#include <wrl.h>
#include <wrl/event.h>
#include <wrl/wrappers/corewrappers.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.Devices.Enumeration.Pnp.h>
#include <atomic>
#include <thread>
using namespace Microsoft::WRL;
using namespace ABI::Windows::Devices::Enumeration;
using namespace ABI::Windows::Devices::Enumeration::Pnp;
using ABI::Windows::Foundation::ITypedEventHandler;
namespace appx {
namespace {
using ObjectListOperation = __FIAsyncOperation_1_Windows__CDevices__CEnumeration__CPnp__CPnpObjectCollection;
using ObjectList = __FIVectorView_1_Windows__CDevices__CEnumeration__CPnp__CPnpObject;
using ObjectOperation = __FIAsyncOperation_1_Windows__CDevices__CEnumeration__CPnp__CPnpObject;
// Reports EnumerationCompleted with no objects, from its own thread as
// Windows' watcher does.
class Watcher final : public RuntimeClass<RuntimeClassFlags<WinRt>, DefaultIPnpObjectWatcher, FtmBase> {
    InspectableClass(L"Windows.Devices.Enumeration.Pnp.PnpObjectWatcher", BaseTrust);
    using ObjectHandler = ITypedEventHandler<PnpObjectWatcher*, PnpObject*>;
    using UpdateHandler = ITypedEventHandler<PnpObjectWatcher*, PnpObjectUpdate*>;
    using StateHandler = ITypedEventHandler<PnpObjectWatcher*, IInspectable*>;
    EventSource<ObjectHandler> added_;
    EventSource<UpdateHandler> updated_, removed_;
    EventSource<StateHandler> enumerated_, stopped_;
    std::atomic<DeviceWatcherStatus> status_{DeviceWatcherStatus_Created};

public:
    HRESULT STDMETHODCALLTYPE add_Added(ObjectHandler* handler, EventRegistrationToken* token) override {
        return added_.Add(handler, token);
    }
    HRESULT STDMETHODCALLTYPE remove_Added(EventRegistrationToken token) override { return added_.Remove(token); }
    HRESULT STDMETHODCALLTYPE add_Updated(UpdateHandler* handler, EventRegistrationToken* token) override {
        return updated_.Add(handler, token);
    }
    HRESULT STDMETHODCALLTYPE remove_Updated(EventRegistrationToken token) override { return updated_.Remove(token); }
    HRESULT STDMETHODCALLTYPE add_Removed(UpdateHandler* handler, EventRegistrationToken* token) override {
        return removed_.Add(handler, token);
    }
    HRESULT STDMETHODCALLTYPE remove_Removed(EventRegistrationToken token) override { return removed_.Remove(token); }
    HRESULT STDMETHODCALLTYPE add_EnumerationCompleted(StateHandler* handler, EventRegistrationToken* token) override {
        return enumerated_.Add(handler, token);
    }
    HRESULT STDMETHODCALLTYPE remove_EnumerationCompleted(EventRegistrationToken token) override {
        return enumerated_.Remove(token);
    }
    HRESULT STDMETHODCALLTYPE add_Stopped(StateHandler* handler, EventRegistrationToken* token) override {
        return stopped_.Add(handler, token);
    }
    HRESULT STDMETHODCALLTYPE remove_Stopped(EventRegistrationToken token) override { return stopped_.Remove(token); }
    HRESULT STDMETHODCALLTYPE get_Status(DeviceWatcherStatus* status) override {
        if (!status) return E_POINTER;
        *status = status_;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE Start() override {
        auto expected = status_.load();
        if (expected != DeviceWatcherStatus_Created && expected != DeviceWatcherStatus_Stopped &&
            expected != DeviceWatcherStatus_Aborted)
            return E_ILLEGAL_METHOD_CALL;
        status_ = DeviceWatcherStatus_Started;
        Log("TRACE", "PnpObjectWatcher: no objects to report");
        ComPtr<Watcher> self(this);
        try {
            std::thread([self] {
                auto started = DeviceWatcherStatus_Started;
                if (self->status_.compare_exchange_strong(started, DeviceWatcherStatus_EnumerationCompleted))
                    self->enumerated_.InvokeAll(self.Get(), nullptr);
            }).detach();
        } catch (...) {
            status_ = DeviceWatcherStatus_Aborted;
            return E_OUTOFMEMORY;
        }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE Stop() override {
        auto status = status_.load();
        if (status != DeviceWatcherStatus_Started && status != DeviceWatcherStatus_EnumerationCompleted)
            return E_ILLEGAL_METHOD_CALL;
        status_ = DeviceWatcherStatus_Stopped;
        stopped_.InvokeAll(this, nullptr);
        return S_OK;
    }
};
class PnpObjectFactory final : public Factory<DefaultIPnpObjectStatics> {
    ComPtr<IPnpObjectStatics> os_;

    static HRESULT Empty(ObjectListOperation** operation) {
        if (!operation) return E_POINTER;
        *operation = nullptr;
        Log("TRACE", "PnpObject: no objects without the Windows class");
        ComPtr<ObjectList> objects;
        try {
            auto empty = winrt::single_threaded_vector<winrt::Windows::Devices::Enumeration::Pnp::PnpObject>();
            objects.Attach(reinterpret_cast<ObjectList*>(winrt::detach_abi(empty.GetView())));
        } catch (...) {
            return winrt::to_hresult();
        }
        auto completed = Make<CompletedOperation<PnpObjectCollection*, ObjectList*>>(
            [objects](ObjectList** value) { return objects.CopyTo(value); }, true);
        return completed ? completed.CopyTo(operation) : E_OUTOFMEMORY;
    }
    static HRESULT Watch(IPnpObjectWatcher** watcher) {
        if (!watcher) return E_POINTER;
        auto made = Make<Watcher>();
        return made ? made.CopyTo(watcher) : E_OUTOFMEMORY;
    }

public:
    PnpObjectFactory() : Factory(L"Windows.Devices.Enumeration.Pnp.PnpObject") {
        OsGetActivationFactory(Wrappers::HStringReference(L"Windows.Devices.Enumeration.Pnp.PnpObject").Get(),
                               IID_PPV_ARGS(&os_));
    }
    HRESULT STDMETHODCALLTYPE CreateFromIdAsync(PnpObjectType type, HSTRING id, __FIIterable_1_HSTRING* properties,
                                                ObjectOperation** operation) override {
        if (os_) return os_->CreateFromIdAsync(type, id, properties, operation);
        if (!operation) return E_POINTER;
        *operation = nullptr;
        return HRESULT_FROM_WIN32(ERROR_NOT_FOUND); // no object has this ID
    }
    HRESULT STDMETHODCALLTYPE FindAllAsync(PnpObjectType type, __FIIterable_1_HSTRING* properties,
                                           ObjectListOperation** operation) override {
        return os_ ? os_->FindAllAsync(type, properties, operation) : Empty(operation);
    }
    HRESULT STDMETHODCALLTYPE FindAllAsyncAqsFilter(PnpObjectType type, __FIIterable_1_HSTRING* properties,
                                                    HSTRING filter, ObjectListOperation** operation) override {
        return os_ ? os_->FindAllAsyncAqsFilter(type, properties, filter, operation) : Empty(operation);
    }
    HRESULT STDMETHODCALLTYPE CreateWatcher(PnpObjectType type, __FIIterable_1_HSTRING* properties,
                                            IPnpObjectWatcher** watcher) override {
        return os_ ? os_->CreateWatcher(type, properties, watcher) : Watch(watcher);
    }
    HRESULT STDMETHODCALLTYPE CreateWatcherAqsFilter(PnpObjectType type, __FIIterable_1_HSTRING* properties,
                                                     HSTRING filter, IPnpObjectWatcher** watcher) override {
        return os_ ? os_->CreateWatcherAqsFilter(type, properties, filter, watcher) : Watch(watcher);
    }
};
HRESULT MakePnpObjectFactory(IActivationFactory** value) {
    static auto factory = Make<PnpObjectFactory>();
    return factory ? factory.CopyTo(value) : E_OUTOFMEMORY;
}
}
const ShimClass pnpObjectClass{L"Windows.Devices.Enumeration.Pnp.PnpObject", MakePnpObjectFactory};
}

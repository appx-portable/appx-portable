// Portable apps cannot schedule OS background tasks. Keep registration offline
// and enumeration empty so notification setup/cancellation can complete.
// Asphalt 7 performs this work while pausing and resuming on window resize.
#include "registry.h"
#include "com/factory.h"
#include <windows.applicationmodel.background.h>
#include <wrl.h>
#include <wrl/event.h>
using namespace Microsoft::WRL;
using namespace ABI::Windows::ApplicationModel::Background;
namespace appx {
namespace {
using TaskMap = __FIMapView_2_GUID_Windows__CApplicationModel__CBackground__CIBackgroundTaskRegistration;
using TaskPair = __FIKeyValuePair_2_GUID_Windows__CApplicationModel__CBackground__CIBackgroundTaskRegistration;
using TaskIterable =
    __FIIterable_1___FIKeyValuePair_2_GUID_Windows__CApplicationModel__CBackground__CIBackgroundTaskRegistration;
using TaskIterator =
    __FIIterator_1___FIKeyValuePair_2_GUID_Windows__CApplicationModel__CBackground__CIBackgroundTaskRegistration;
class EmptyTaskIterator final : public RuntimeClass<RuntimeClassFlags<WinRt>, TaskIterator, FtmBase> {
    InspectableClass(L"Windows.Foundation.Collections.IIterator`1", BaseTrust);

public:
    HRESULT STDMETHODCALLTYPE get_Current(TaskPair** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        return E_BOUNDS;
    }
    HRESULT STDMETHODCALLTYPE get_HasCurrent(boolean* value) override {
        if (!value) return E_POINTER;
        *value = false;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE MoveNext(boolean* value) override { return get_HasCurrent(value); }
    HRESULT STDMETHODCALLTYPE GetMany(UINT32 capacity, TaskPair** values, UINT32* actual) override {
        if (!actual) return E_POINTER;
        *actual = 0;
        if (capacity && !values) return E_POINTER;
        return S_OK;
    }
};
class EmptyTaskMap final : public RuntimeClass<RuntimeClassFlags<WinRt>, TaskMap, TaskIterable, FtmBase> {
    InspectableClass(L"Windows.Foundation.Collections.IMapView`2", BaseTrust);

public:
    HRESULT STDMETHODCALLTYPE Lookup(GUID, IBackgroundTaskRegistration** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        return E_BOUNDS;
    }
    HRESULT STDMETHODCALLTYPE get_Size(UINT32* value) override {
        if (!value) return E_POINTER;
        *value = 0;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE HasKey(GUID, boolean* value) override {
        if (!value) return E_POINTER;
        *value = false;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE Split(TaskMap** first, TaskMap** second) override {
        if (first) *first = nullptr;
        if (second) *second = nullptr;
        return first && second ? S_OK : E_POINTER;
    }
    HRESULT STDMETHODCALLTYPE First(TaskIterator** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        auto iterator = Make<EmptyTaskIterator>();
        return iterator ? iterator.CopyTo(value) : E_OUTOFMEMORY;
    }
};

class BackgroundTaskFactory final : public Factory<IBackgroundTaskRegistrationStatics> {
public:
    BackgroundTaskFactory() : Factory(L"Windows.ApplicationModel.Background.BackgroundTaskRegistration") {}
    HRESULT STDMETHODCALLTYPE get_AllTasks(TaskMap** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        auto tasks = Make<EmptyTaskMap>();
        return tasks ? tasks.CopyTo(value) : E_OUTOFMEMORY;
    }
};
// Registration succeeds locally, but never schedules an OS background job.
// Games register notification tasks while pausing during a window resize.
class BackgroundRegistration final
    : public RuntimeClass<RuntimeClassFlags<WinRt>, IBackgroundTaskRegistration, FtmBase> {
    InspectableClass(L"Windows.ApplicationModel.Background.BackgroundTaskRegistration", BaseTrust);
    Wrappers::HString name_;
    GUID id_{};
    EventSource<IBackgroundTaskProgressEventHandler> progress_;
    EventSource<IBackgroundTaskCompletedEventHandler> completed_;

public:
    HRESULT RuntimeClassInitialize(HSTRING name) {
        auto hr = name_.Set(name);
        return FAILED(hr) ? hr : CoCreateGuid(&id_);
    }
    HRESULT STDMETHODCALLTYPE get_TaskId(GUID* value) override {
        if (!value) return E_POINTER;
        *value = id_;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_Name(HSTRING* value) override { return value ? name_.CopyTo(value) : E_POINTER; }
    HRESULT STDMETHODCALLTYPE add_Progress(IBackgroundTaskProgressEventHandler* handler,
                                           EventRegistrationToken* token) override {
        return progress_.Add(handler, token);
    }
    HRESULT STDMETHODCALLTYPE remove_Progress(EventRegistrationToken token) override { return progress_.Remove(token); }
    HRESULT STDMETHODCALLTYPE add_Completed(IBackgroundTaskCompletedEventHandler* handler,
                                            EventRegistrationToken* token) override {
        return completed_.Add(handler, token);
    }
    HRESULT STDMETHODCALLTYPE remove_Completed(EventRegistrationToken token) override {
        return completed_.Remove(token);
    }
    HRESULT STDMETHODCALLTYPE Unregister(boolean) override { return S_OK; }
};
class BackgroundBuilder final
    : public RuntimeClass<RuntimeClassFlags<WinRt>, IBackgroundTaskBuilder, IBackgroundTaskBuilder2, FtmBase> {
    InspectableClass(L"Windows.ApplicationModel.Background.BackgroundTaskBuilder", BaseTrust);
    Wrappers::HString name_, entry_;
    ComPtr<IBackgroundTrigger> trigger_;
    boolean cancelOnConditionLoss_ = false;

public:
    HRESULT STDMETHODCALLTYPE put_TaskEntryPoint(HSTRING value) override { return entry_.Set(value); }
    HRESULT STDMETHODCALLTYPE get_TaskEntryPoint(HSTRING* value) override {
        return value ? entry_.CopyTo(value) : E_POINTER;
    }
    HRESULT STDMETHODCALLTYPE SetTrigger(IBackgroundTrigger* value) override {
        if (!value) return E_INVALIDARG;
        trigger_ = value;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE AddCondition(IBackgroundCondition* value) override { return value ? S_OK : E_INVALIDARG; }
    HRESULT STDMETHODCALLTYPE put_Name(HSTRING value) override { return name_.Set(value); }
    HRESULT STDMETHODCALLTYPE get_Name(HSTRING* value) override { return value ? name_.CopyTo(value) : E_POINTER; }
    HRESULT STDMETHODCALLTYPE Register(IBackgroundTaskRegistration** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        if (!trigger_ || WindowsIsStringEmpty(name_.Get()) || WindowsIsStringEmpty(entry_.Get())) return E_INVALIDARG;
        ComPtr<BackgroundRegistration> registration;
        auto hr = MakeAndInitialize<BackgroundRegistration>(&registration, name_.Get());
        if (FAILED(hr)) return hr;
        Log("TRACE", "background task kept offline: %ls", name_.GetRawBuffer(nullptr));
        return registration.CopyTo(value);
    }
    HRESULT STDMETHODCALLTYPE put_CancelOnConditionLoss(boolean value) override {
        cancelOnConditionLoss_ = value;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_CancelOnConditionLoss(boolean* value) override {
        if (!value) return E_POINTER;
        *value = cancelOnConditionLoss_;
        return S_OK;
    }
};
class BackgroundBuilderFactory final : public Factory<> {
public:
    BackgroundBuilderFactory() : Factory(L"Windows.ApplicationModel.Background.BackgroundTaskBuilder") {}
    HRESULT STDMETHODCALLTYPE ActivateInstance(IInspectable** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        auto builder = Make<BackgroundBuilder>();
        return builder ? builder.CopyTo(value) : E_OUTOFMEMORY;
    }
};
}
HRESULT MakeBackgroundTaskFactory(IActivationFactory** value) {
    auto factory = Make<BackgroundTaskFactory>();
    return factory ? factory.CopyTo(value) : E_OUTOFMEMORY;
}
const ShimClass backgroundTaskClass{L"Windows.ApplicationModel.Background.BackgroundTaskRegistration",
                                    MakeBackgroundTaskFactory, ShimPolicy::Replace, InIslands};
HRESULT MakeBackgroundBuilderFactory(IActivationFactory** value) {
    auto factory = Make<BackgroundBuilderFactory>();
    return factory ? factory.CopyTo(value) : E_OUTOFMEMORY;
}
const ShimClass backgroundBuilderClass{L"Windows.ApplicationModel.Background.BackgroundTaskBuilder",
                                       MakeBackgroundBuilderFactory, ShimPolicy::Replace, InIslands};
}

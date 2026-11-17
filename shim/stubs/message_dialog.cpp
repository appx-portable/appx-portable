// Desktop fallback for Wine's absent Windows.UI.Popups factories.
#include "com/factory.h"
#include "com/async_action.h"
#include "host/host.h"
#include "registry.h"
#include <windows.ui.popups.h>
#include <commctrl.h>
#include <shobjidl.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.UI.Popups.h>
#include <atomic>
#include <thread>
#include <vector>
using namespace Microsoft::WRL;
namespace appx {
HRESULT MakeDesktopMessageDialog(HSTRING, HSTRING, ABI::Windows::UI::Popups::IMessageDialog**);
namespace {
namespace Popups = ABI::Windows::UI::Popups;
using CommandOperation = ABI::Windows::Foundation::IAsyncOperation<Popups::IUICommand*>;
using CommandCompleted = ABI::Windows::Foundation::IAsyncOperationCompletedHandler<Popups::IUICommand*>;
class Command final : public RuntimeClass<RuntimeClassFlags<WinRt>, Popups::IUICommand, FtmBase> {
    InspectableClass(L"Windows.UI.Popups.UICommand", BaseTrust);
    Wrappers::HString label_;
    ComPtr<IInspectable> id_;
    ComPtr<Popups::IUICommandInvokedHandler> invoked_;

public:
    HRESULT STDMETHODCALLTYPE get_Label(HSTRING* value) override { return label_.CopyTo(value); }
    HRESULT STDMETHODCALLTYPE put_Label(HSTRING value) override { return label_.Set(value); }
    HRESULT STDMETHODCALLTYPE get_Id(IInspectable** value) override { return id_.CopyTo(value); }
    HRESULT STDMETHODCALLTYPE put_Id(IInspectable* value) override {
        id_ = value;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_Invoked(Popups::IUICommandInvokedHandler** value) override {
        return invoked_.CopyTo(value);
    }
    HRESULT STDMETHODCALLTYPE put_Invoked(Popups::IUICommandInvokedHandler* value) override {
        invoked_ = value;
        return S_OK;
    }
};
class CommandFactory final : public Factory<Popups::IUICommandFactory> {
public:
    CommandFactory() : Factory(L"Windows.UI.Popups.UICommand") {}
    HRESULT STDMETHODCALLTYPE ActivateInstance(IInspectable** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        auto command = Make<Command>();
        return command ? command.CopyTo(value) : E_OUTOFMEMORY;
    }
    HRESULT STDMETHODCALLTYPE Create(HSTRING label, Popups::IUICommand** value) override {
        return CreateWithHandlerAndId(label, nullptr, nullptr, value);
    }
    HRESULT STDMETHODCALLTYPE CreateWithHandler(HSTRING label, Popups::IUICommandInvokedHandler* handler,
                                                Popups::IUICommand** value) override {
        return CreateWithHandlerAndId(label, handler, nullptr, value);
    }
    HRESULT STDMETHODCALLTYPE CreateWithHandlerAndId(HSTRING label, Popups::IUICommandInvokedHandler* handler,
                                                     IInspectable* id, Popups::IUICommand** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        auto command = Make<Command>();
        if (!command) return E_OUTOFMEMORY;
        auto hr = command->put_Label(label);
        if (SUCCEEDED(hr)) hr = command->put_Id(id);
        if (SUCCEEDED(hr)) hr = command->put_Invoked(handler);
        return FAILED(hr) ? hr : command.CopyTo(value);
    }
};
// Reuse the shim's async state machine without a self-reference cycle in its handler.
class DialogOperation final
    : public RuntimeClass<RuntimeClassFlags<WinRt>, CommandOperation, ABI::Windows::Foundation::IAsyncInfo, FtmBase> {
    InspectableClass(L"Windows.Foundation.IAsyncOperation`1", BaseTrust);
    ComPtr<AsyncAction> state_ = Make<AsyncAction>();
    ComPtr<Popups::IUICommand> result_;
    ComPtr<CommandCompleted> handler_;
    std::mutex mutex_;
    bool assigned_ = false, closed_ = false;
    std::atomic<bool> canceled_{false};

public:
    std::atomic<HWND> window{nullptr};
    bool Valid() const { return state_ != nullptr; }
    bool Canceled() const { return canceled_.load(); }
    void Finish(HRESULT hr, Popups::IUICommand* result) {
        {
            std::lock_guard<std::mutex> lock(mutex_);
            if (!closed_ && !Canceled()) result_ = result;
        }
        state_->Finish(hr);
    }
    HRESULT STDMETHODCALLTYPE put_Completed(CommandCompleted* handler) override {
        ComPtr<DialogOperation> keep(this);
        ABI::Windows::Foundation::AsyncStatus status{};
        auto hr = state_->get_Status(&status);
        if (FAILED(hr)) return hr;
        ComPtr<CommandCompleted> target(handler);
        auto bridge = Callback<ABI::Windows::Foundation::IAsyncActionCompletedHandler>(
            [this, target](auto*, auto status) { return target ? target->Invoke(this, status) : S_OK; });
        {
            std::lock_guard<std::mutex> lock(mutex_);
            if (assigned_) return E_ILLEGAL_DELEGATE_ASSIGNMENT;
            assigned_ = true;
            handler_ = target;
        }
        return state_->put_Completed(bridge.Get());
    }
    HRESULT STDMETHODCALLTYPE get_Completed(CommandCompleted** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        ABI::Windows::Foundation::AsyncStatus status{};
        auto hr = state_->get_Status(&status);
        if (FAILED(hr)) return hr;
        std::lock_guard<std::mutex> lock(mutex_);
        return handler_.CopyTo(value);
    }
    HRESULT STDMETHODCALLTYPE GetResults(Popups::IUICommand** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        auto hr = state_->GetResults();
        if (FAILED(hr)) return hr;
        std::lock_guard<std::mutex> lock(mutex_);
        return result_.CopyTo(value);
    }
    HRESULT STDMETHODCALLTYPE get_Id(UINT32* value) override { return state_->get_Id(value); }
    HRESULT STDMETHODCALLTYPE get_Status(ABI::Windows::Foundation::AsyncStatus* value) override {
        return state_->get_Status(value);
    }
    HRESULT STDMETHODCALLTYPE get_ErrorCode(HRESULT* value) override { return state_->get_ErrorCode(value); }
    HRESULT STDMETHODCALLTYPE Cancel() override {
        ABI::Windows::Foundation::AsyncStatus status{};
        auto hr = state_->get_Status(&status);
        if (FAILED(hr) || status != ABI::Windows::Foundation::AsyncStatus::Started) return hr;
        canceled_ = true;
        hr = state_->Cancel();
        if (auto hwnd = window.load()) PostMessageW(hwnd, TDM_CLICK_BUTTON, IDCANCEL, 0);
        return hr;
    }
    HRESULT STDMETHODCALLTYPE Close() override {
        auto hr = state_->Close();
        if (SUCCEEDED(hr)) {
            std::lock_guard<std::mutex> lock(mutex_);
            closed_ = true;
            handler_.Reset();
            result_.Reset();
        }
        return hr;
    }
};
struct Activation {
    ULONG_PTR cookie{};
    Activation() {
        HMODULE module{};
        winrt::check_bool(
            GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                               reinterpret_cast<LPCWSTR>(&MakeDesktopMessageDialog), &module));
        ACTCTXW description{};
        description.cbSize = sizeof(description);
        description.dwFlags = ACTCTX_FLAG_HMODULE_VALID | ACTCTX_FLAG_RESOURCE_NAME_VALID;
        description.hModule = module;
        description.lpResourceName = MAKEINTRESOURCEW(3);
        HANDLE context = CreateActCtxW(&description);
        if (context == INVALID_HANDLE_VALUE) winrt::throw_last_error();
        BOOL activated = ActivateActCtx(context, &cookie);
        ReleaseActCtx(context);
        winrt::check_bool(activated);
    }
    ~Activation() {
        if (cookie) DeactivateActCtx(0, cookie);
    }
};
struct DialogState {
    DialogOperation* operation;
    bool canCancel;
};
HRESULT CALLBACK DialogCallback(HWND window, UINT event, WPARAM button, LPARAM, LONG_PTR data) {
    auto& state = *reinterpret_cast<DialogState*>(data);
    if (event == TDN_CREATED) {
        state.operation->window = window;
        if (state.operation->Canceled()) PostMessageW(window, TDM_CLICK_BUTTON, IDCANCEL, 0);
    }
    if (event == TDN_DESTROYED) state.operation->window = nullptr;
    if (event == TDN_BUTTON_CLICKED && button == IDCANCEL && !state.canCancel && !state.operation->Canceled())
        return S_FALSE;
    return S_OK;
}
class DesktopDialog final : public RuntimeClass<RuntimeClassFlags<WinRtClassicComMix>, Popups::IMessageDialog,
                                                IInitializeWithWindow, FtmBase> {
    InspectableClass(L"Windows.UI.Popups.MessageDialog", BaseTrust);
    Wrappers::HString content_, title_;
    winrt::Windows::Foundation::Collections::IVector<winrt::Windows::UI::Popups::IUICommand> commands_ =
        winrt::single_threaded_vector<winrt::Windows::UI::Popups::IUICommand>();
    UINT32 default_ = 0, cancel_ = UINT32_MAX;
    Popups::MessageDialogOptions options_{};
    std::atomic<bool> showing_{false};
    HWND owner_ = HostWindow();

public:
    HRESULT STDMETHODCALLTYPE Initialize(HWND owner) override {
        owner_ = owner;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_Title(HSTRING* value) override { return title_.CopyTo(value); }
    HRESULT STDMETHODCALLTYPE put_Title(HSTRING value) override { return title_.Set(value); }
    HRESULT STDMETHODCALLTYPE get_Content(HSTRING* value) override { return content_.CopyTo(value); }
    HRESULT STDMETHODCALLTYPE put_Content(HSTRING value) override { return content_.Set(value); }
    HRESULT STDMETHODCALLTYPE get_Commands(__FIVector_1_Windows__CUI__CPopups__CIUICommand** value) override {
        if (!value) return E_POINTER;
        commands_.as<__FIVector_1_Windows__CUI__CPopups__CIUICommand>().copy_to(value);
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_DefaultCommandIndex(UINT32* value) override {
        if (!value) return E_POINTER;
        *value = default_;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE put_DefaultCommandIndex(UINT32 value) override {
        default_ = value;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_CancelCommandIndex(UINT32* value) override {
        if (!value) return E_POINTER;
        *value = cancel_;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE put_CancelCommandIndex(UINT32 value) override {
        cancel_ = value;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE get_Options(Popups::MessageDialogOptions* value) override {
        if (!value) return E_POINTER;
        *value = options_;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE put_Options(Popups::MessageDialogOptions value) override {
        if (value != 0) return E_NOTIMPL;
        options_ = value;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE ShowAsync(CommandOperation** value) override {
        if (!value) return E_POINTER;
        *value = nullptr;
        if (showing_.exchange(true)) return E_ILLEGAL_METHOD_CALL;
        try {
            std::vector<ComPtr<Popups::IUICommand>> commands;
            for (auto item : commands_) commands.emplace_back(item.as<Popups::IUICommand>().get());
            if (commands.empty()) {
                auto close = Make<Command>();
                if (!close) winrt::throw_hresult(E_OUTOFMEMORY);
                close->put_Label(Wrappers::HStringReference(L"Close").Get());
                commands.push_back(close);
            }
            if (commands.size() > 3 || default_ >= commands.size() ||
                (cancel_ != UINT32_MAX && cancel_ >= commands.size())) {
                showing_ = false;
                return E_INVALIDARG;
            }
            auto operation = Make<DialogOperation>();
            if (!operation || !operation->Valid()) {
                showing_ = false;
                return E_OUTOFMEMORY;
            }
            ComPtr<DesktopDialog> self(this);
            std::wstring title(title_.GetRawBuffer(nullptr)), content(content_.GetRawBuffer(nullptr));
            auto selectedDefault = default_, selectedCancel = cancel_;
            auto owner = owner_;
            bool onHost = OnHostThread();
            std::thread([self, operation, commands, title, content, selectedDefault, selectedCancel, owner, onHost] {
                HRESULT initialized = RoInitialize(RO_INIT_MULTITHREADED), hr = S_OK;
                ComPtr<Popups::IUICommand> selected;
                try {
                    winrt::check_hresult(initialized);
                    Activation activation;
                    using ShowDialog = HRESULT(WINAPI*)(const TASKDIALOGCONFIG*, int*, int*, BOOL*);
                    static auto show = reinterpret_cast<ShowDialog>(
                        GetProcAddress(LoadLibraryW(L"comctl32.dll"), "TaskDialogIndirect"));
                    if (!show) winrt::throw_hresult(E_NOTIMPL);
                    std::vector<winrt::hstring> labels;
                    std::vector<TASKDIALOG_BUTTON> buttons;
                    for (auto& command : commands) {
                        Wrappers::HString label;
                        winrt::check_hresult(command->get_Label(label.GetAddressOf()));
                        labels.emplace_back(label.GetRawBuffer(nullptr));
                    }
                    for (size_t i = 0; i < labels.size(); ++i)
                        buttons.push_back({1000 + static_cast<int>(i), labels[i].c_str()});
                    DialogState state{operation.Get(), selectedCancel < commands.size()};
                    TASKDIALOGCONFIG config{};
                    config.cbSize = sizeof(config);
                    config.hwndParent = owner;
                    config.pszWindowTitle = title.c_str();
                    config.pszContent = content.c_str();
                    config.dwFlags = TDF_ALLOW_DIALOG_CANCELLATION | TDF_SIZE_TO_CONTENT;
                    config.cButtons = static_cast<UINT>(buttons.size());
                    config.pButtons = buttons.data();
                    config.nDefaultButton = 1000 + selectedDefault;
                    config.pfCallback = DialogCallback;
                    config.lpCallbackData = reinterpret_cast<LONG_PTR>(&state);
                    int button{};
                    winrt::check_hresult(show(&config, &button, nullptr, nullptr));
                    if (!operation->Canceled()) {
                        auto index = button == IDCANCEL ? selectedCancel : static_cast<UINT32>(button - 1000);
                        if (index >= commands.size()) winrt::throw_hresult(E_UNEXPECTED);
                        selected = commands[index];
                    }
                } catch (...) {
                    hr = winrt::to_hresult();
                }
                auto complete = [self, operation, selected, hr]() mutable {
                    self->showing_ = false;
                    if (SUCCEEDED(hr) && selected && !operation->Canceled()) {
                        ComPtr<Popups::IUICommandInvokedHandler> callback;
                        hr = selected->get_Invoked(&callback);
                        if (SUCCEEDED(hr) && callback) hr = callback->Invoke(selected.Get());
                    }
                    operation->Finish(hr, selected.Get());
                };
                if (!onHost || FAILED(PostHostWork(0, complete))) complete();
                if (SUCCEEDED(initialized)) RoUninitialize();
            }).detach();
            return operation.CopyTo(value);
        } catch (...) {
            showing_ = false;
            return winrt::to_hresult();
        }
    }
};
HRESULT MakeCommandFactory(IActivationFactory** value) {
    auto factory = Make<CommandFactory>();
    return factory ? factory.CopyTo(value) : E_OUTOFMEMORY;
}
}
HRESULT MakeDesktopMessageDialog(HSTRING content, HSTRING title, ABI::Windows::UI::Popups::IMessageDialog** value) {
    if (!value) return E_POINTER;
    *value = nullptr;
    try {
        auto dialog = Make<DesktopDialog>();
        if (!dialog) return E_OUTOFMEMORY;
        auto hr = dialog->put_Content(content);
        if (SUCCEEDED(hr)) hr = dialog->put_Title(title);
        return FAILED(hr) ? hr : dialog.CopyTo(value);
    } catch (...) {
        return winrt::to_hresult();
    }
}
const ShimClass uiCommandClass{L"Windows.UI.Popups.UICommand", MakeCommandFactory, ShimPolicy::FillMissing};
}

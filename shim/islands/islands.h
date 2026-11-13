#pragma once
#include <windows.h>
#include <activation.h>
#include <windows.ui.core.h>
#include <windows.applicationmodel.activation.h>
#include <functional>
namespace appx {
HRESULT IslandDispatcher(ABI::Windows::UI::Core::ICoreDispatcher** value);
bool IslandsMode() noexcept;
bool ManagedMode() noexcept;
void SetNativeXamlMode() noexcept;
void IslandHostMessage(UINT message, WPARAM wparam, LPARAM lparam);
// Holds work until Window.Current.Content first loads; false when nothing is
// waiting for that (no content yet, or it has loaded).
bool DeferUntilIslandContentLoaded(std::function<void()> work);
void CloseIslandView();
void ResizeIslandView(int width, int height);
void RaiseIslandSuspending(ABI::Windows::ApplicationModel::ISuspendingEventArgs* args);
bool IslandPreTranslate(MSG* message);
HRESULT MakeIslandFactory(const wchar_t* name, IActivationFactory** out);
HRESULT MakeIslandPanelFactory(IActivationFactory** out);
// A SwapChainBackgroundPanel for compiled XAML, composed over a SwapChainPanel.
HRESULT CreateIslandBackgroundPanel(IInspectable** value);
HRESULT MakeIslandSwapChainPanelFactory(IActivationFactory** out);
HRESULT MakeLaunchArgs(ABI::Windows::ApplicationModel::Activation::IActivatedEventArgs** out);
void DismissSplashScreen();
}

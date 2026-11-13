#pragma once
#include <windows.h>
#include <activation.h>
#include <winstring.h>
#include <filesystem>
namespace appx {
// Whether the package serves this module's classes through Windows' CLRHost.dll,
// the host for C# Windows Runtime components.
bool IsClrHostModule(const std::filesystem::path& module) noexcept;
// The activation factory of a C# component class, made by the desktop CLR in
// this process; REGDB_E_CLASSNOTREG when the CLR or the bridge is unavailable.
HRESULT ManagedComponentFactory(HSTRING name, IActivationFactory** factory) noexcept;
// Whether the bridge can be tried: appx-components.dll (or, in older folders,
// appx-host.exe) is beside the game, outside
// Wine, whose 32-bit .NET hangs instead of failing.
bool ManagedComponentBridgeAvailable() noexcept;
// True once the CLR has been started for components: its calls into Windows
// classes then count as the game's.
bool ManagedComponentsHosted() noexcept;
}

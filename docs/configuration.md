# Saves and window settings

## Save location

Saves go to `SaveData` beside the game executable. To change that, edit `appx-portable.ini` in the game folder:

```ini
[storage]
SaveLocation=game
```

| Value            | Save folder                                       |
| ---------------- | ------------------------------------------------- |
| `game` (default) | `SaveData` beside the game executable             |
| `documents`      | `Documents\appx-portable\<package>`               |
| `appdata`        | `%LOCALAPPDATA%\appx-portable\<package>`          |
| A folder path    | That folder; relative paths start beside the game |

`<package>` is the identity name from the game's manifest, so games never share saves. If the folder cannot be written, saves go to `documents`, then `appdata`. Changing the location does not move existing saves.

## Window

Press **F11** or **Alt+Enter** to toggle borderless fullscreen. Window size, position, and mode are remembered in `Local\window.ini` inside the save folder; delete it with the game closed to reset them. The game keeps running when it loses focus and pauses when minimized. Closing the window suspends the game, which saves, and then ends it.

## Store options

## Frame rate

`appx-portable.ini` caps the game at 60 frames a second by default:

```ini
[options]
FrameRate=60
```

Most Store games advance one fixed step per frame and expect 60. Without the cap, they run too fast on a faster display, and under Wine on macOS, which does not wait for the display. Set `FrameRate=0` to remove the cap for a game that keeps its speed at any frame rate, or another number for a different cap. The `APPX_PORTABLE_FRAME_RATE` environment variable overrides the file. Restart the game after a change.

`appx-portable.ini` also holds the Store overrides; see [trial override](trial-mode.md) and [offline in-app purchases](stub-iap.md).

## Network

Games run offline by default. `NetworkInformation.GetInternetConnectionProfile` reports no internet connection, and requests made through `Windows.Web.Http` fail at once with a network error. Many Store games still try servers that have since closed. Some wait on them forever when told they are online: Asphalt 8's age screen keeps its button spinning until Gameloft's servers send their settings. Offline, such games take their offline path; Asphalt 8 reaches its menus.

To give a game Windows' own connection status and HTTP classes, set:

```ini
[options]
Network=1
```

`--network` and the `APPX_PORTABLE_NETWORK` environment variable do the same, and a set variable overrides the file. Restart the game after a change. A game's own sockets, Windows' WebView and the desktop .NET runtime's networking for C# components reach the network either way.

## XAML

A native game with XAML pages runs on Windows' own XAML in an island when its executable's manifest allows XAML Islands; `pyappxportable` writes `<game>.exe.manifest` for such games. To keep a game on the portable XAML backend instead, set:

```ini
[options]
Xaml=portable
```

The `APPX_PORTABLE_XAML` environment variable overrides the file. A game installed before this default has no manifest and stays on the portable backend until its runtime is updated with `--update-runtime`. See [managed games and XAML Islands](islands.md#native-xaml-games).

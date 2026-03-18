# Pop

Pop is a desktop utility that adds momentum-based window snapping. Drag a window by its title bar, flick left or right, and Pop animates the window into the corresponding half of the current monitor.

Windows support exists today, and the repo now includes a macOS Apple Silicon menu-bar app plus a Native AOT bridge that reuses the shared snap and animation logic from `Pop.Core`.

## What It Does

- Runs quietly from the system tray on Windows or the menu bar on macOS.
- Watches for title-bar drag gestures using a global mouse hook.
- Qualifies a snap based on horizontal release velocity and horizontal-vs-vertical motion dominance.
- Animates eligible windows into the left or right half of the active monitor work area.
- Lets you hold `Ctrl` on Windows or `Option` on macOS at release to project the throw onto another monitor and snap there.
- Lets you tune gesture sensitivity, animation duration, launch-at-startup behavior, and optional diagnostics logging.

## Current Behavior

Pop currently targets a focused v1 workflow:

- Left-half and right-half snapping only.
- Gesture must begin on a window title bar.
- Holding `Ctrl` while releasing a drag enables cross-monitor throw targeting without adding extra settings.
- Only standard top-level, visible, resizable desktop windows are eligible.
- Minimized, maximized, fullscreen, cloaked, elevated, and Pop-owned windows are ignored.

## Solution Layout

- `src/Pop.Core`: Shared business logic and models such as gesture qualification, snap decisions, tile calculations, animation planning, settings persistence, and diagnostics formatting.
- `src/Pop.Platform.Abstractions`: Platform-facing contracts for drag observation, window inspection, window movement, startup registration, and future shell integration seams.
- `src/Pop.App.Windows`: The Windows app plus Win32/WPF implementations of the platform abstractions.
- `src/Pop.App.Mac`: The Native AOT macOS bridge and the native Swift/AppKit menu-bar app under `NativeHost`.
- `tests/Pop.Tests`: Cross-platform xUnit tests for shared logic in `Pop.Core`.
- `tests/Pop.Tests.Windows`: Windows-targeted xUnit tests for the current Windows adapters and update flow.

## Requirements

- Windows 10/11 or macOS 13+ on Apple Silicon for end users
- .NET 8 SDK for building from source

`Pop.Core`, `Pop.Platform.Abstractions`, and the macOS bridge target `net8.0`. The Windows app and test project target `net8.0-windows10.0.22621.0` and use WPF plus WinForms interop for the tray icon. The native macOS host is a Swift Package in `src/Pop.App.Mac/NativeHost`.

## Download & Install

Pop is now set up for release artifacts on both platforms:

1. Download the latest `Setup.exe` from the [GitHub Releases](https://github.com/Robertg761/Pop/releases) page.
2. On macOS, download the latest `Pop-macos-arm64-<version>.zip`, unzip it, and move `Pop.app` into `Applications`.
3. Launch Pop and grant Accessibility permission when prompted.
4. Open `Settings` from the tray/menu-bar icon to tune behavior.

Installed builds check for updates automatically after launch and about every six hours after that. Updates download in the background and then prompt you to restart Pop when the new version is ready.

The GitHub release page only needs to host `Setup.exe`. Velopack feed files stay in the repo's `update-feed` branch so installed builds can update without cluttering release assets.

In-app updates only exist on Windows today. The macOS build is intentionally unsigned for now and updates through GitHub Releases.

## Build From Source

Restore, build, and test:

```powershell
dotnet build Pop.sln
dotnet test tests/Pop.Tests/Pop.Tests.csproj
```

Run the Windows app:

```powershell
dotnet run --project .\src\Pop.App.Windows\Pop.App.Windows.csproj
```

Build and package the macOS app:

```zsh
./scripts/package-mac-release.sh
```

When Pop starts, it lives in the system tray on Windows and in the menu bar on macOS. Use `Open Settings` from the tray/menu-bar menu to configure it.

## How To Use Pop

1. Start Pop.
2. Click and hold a supported window's title bar.
3. Drag and release with a fast horizontal flick.
4. Pop snaps the window to the left or right half of the current monitor.
5. Hold `Ctrl` at release on Windows or `Option` on macOS to throw across monitors, including stacked monitors above or below. Fast throws use the landing side on the destination monitor; slower cross-monitor throws prefer the edge closest to the monitor they came from.

If the release is too slow or too vertical, the window is left alone.

## Settings

Pop stores settings in:

```text
%LOCALAPPDATA%\Pop\settings.json
```

On macOS the same logical JSON document lives in:

```text
~/Library/Application Support/Pop/settings.json
```

Available settings:

- `Enabled`: Turns momentum snapping on or off.
- `LaunchAtStartup`: Adds or removes Pop from the current user's Windows startup registry key.
- `ThrowVelocityThresholdPxPerSec`: Minimum horizontal release velocity required to qualify.
- `HorizontalDominanceRatio`: How strongly horizontal the gesture must be.
- `GlideDurationMs`: Duration of the snap animation.
- `EnableDiagnostics`: Enables local diagnostic logging.

The settings window also includes an `Updates` section that shows:

- the currently running version
- automatic/manual update status
- download progress when an update is being prepared
- an install button when a downloaded update is ready

## Diagnostics

When diagnostics are enabled, Pop appends compact JSON log lines to:

```text
%LOCALAPPDATA%\Pop\diagnostics.log
```

On macOS the diagnostics log lives in:

```text
~/Library/Application Support/Pop/diagnostics.log
```

These entries can help explain:

- Why a drag was ignored
- Why a release did not qualify
- Which target was selected
- How the animation plan was built

## Startup Registration

Launch-at-startup is stored in the current user's registry hive:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
```

On macOS, launch-at-login is implemented with a per-user LaunchAgent plist under:

```text
~/Library/LaunchAgents/com.pop.app.plist
```

## Development Notes

- `src/Pop.App.Windows` is the current executable entry point.
- `src/Pop.Core` contains the shared behavior worth preserving across Windows and macOS.
- `src/Pop.Platform.Abstractions` marks the intended seam between shared logic and OS-specific integration code.
- `src/Pop.App.Mac` now contains the Native AOT bridge and the Swift/AppKit host.
- The app is tray-first by design, so there is no main window on launch.
- Tests currently pass with `dotnet test tests/Pop.Tests/Pop.Tests.csproj`, `./scripts/build-mac-bridge.sh`, and `swift test --package-path src/Pop.App.Mac/NativeHost`.
- Shared release metadata lives in `Directory.Build.props`.
- A push to `main` that bumps `Version` in `Directory.Build.props` triggers the Windows and macOS release workflow in `.github/workflows/release.yml`.
- For local release artifacts, run `.\scripts\package-release.ps1` on Windows or `./scripts/package-mac-release.sh` on macOS.

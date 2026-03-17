# Pop

Pop is a Windows tray app that adds momentum-based window snapping. Drag a window by its title bar, flick left or right, and Pop animates the window into the corresponding half of the current monitor.

## What It Does

- Runs quietly from the system tray.
- Watches for title-bar drag gestures using a global mouse hook.
- Qualifies a snap based on horizontal release velocity and horizontal-vs-vertical motion dominance.
- Animates eligible windows into the left or right half of the active monitor work area.
- Lets you tune gesture sensitivity, animation duration, launch-at-startup behavior, and optional diagnostics logging.

## Current Behavior

Pop currently targets a focused v1 workflow:

- Left-half and right-half snapping only.
- Gesture must begin on a window title bar.
- Only standard top-level, visible, resizable desktop windows are eligible.
- Minimized, maximized, fullscreen, cloaked, elevated, and Pop-owned windows are ignored.

## Solution Layout

- `Pop.App`: WPF tray application, settings window, startup registration, diagnostics logging, and host wiring.
- `Pop.Core`: Core gesture tracking, window inspection, snap decision logic, tile calculations, and animation planning.
- `Pop.Tests`: xUnit tests covering settings persistence, snap qualification, tile calculations, animation planning, and eligibility rules.

## Requirements

- Windows 10/11
- .NET 8 SDK

The solution targets `net8.0-windows10.0.22621.0` and uses WPF plus WinForms interop for the tray icon.

## Getting Started

Restore, build, and test:

```powershell
dotnet build Pop.sln
dotnet test Pop.sln
```

Run the app:

```powershell
dotnet run --project .\Pop.App\Pop.App.csproj
```

When the app starts, it lives in the system tray. Double-click the tray icon or use `Open Settings` from the tray menu to configure it.

## How To Use Pop

1. Start Pop.
2. Click and hold a supported window's title bar.
3. Drag and release with a fast horizontal flick.
4. Pop snaps the window to the left or right half of the current monitor.

If the release is too slow or too vertical, the window is left alone.

## Settings

Pop stores settings in:

```text
%LOCALAPPDATA%\Pop\settings.json
```

Available settings:

- `Enabled`: Turns momentum snapping on or off.
- `LaunchAtStartup`: Adds or removes Pop from the current user's Windows startup registry key.
- `ThrowVelocityThresholdPxPerSec`: Minimum horizontal release velocity required to qualify.
- `HorizontalDominanceRatio`: How strongly horizontal the gesture must be.
- `GlideDurationMs`: Duration of the snap animation.
- `EnableDiagnostics`: Enables local diagnostic logging.

## Diagnostics

When diagnostics are enabled, Pop appends compact JSON log lines to:

```text
%LOCALAPPDATA%\Pop\diagnostics.log
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

## Development Notes

- `Pop.App` is the executable entry point.
- `Pop.Core` contains nearly all behavior worth unit testing.
- The app is tray-first by design, so there is no main window on launch.
- Tests currently pass with `dotnet test Pop.sln`.

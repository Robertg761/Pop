# Pop architecture

Pop is a tray/menu-bar utility that turns title-bar flicks into left/right window snaps. The product is intentionally small: one gesture, half-tile targets, and platform shells that stay out of the way.

## Layering

```text
Pop.Core
  Shared models, settings, snap qualification, tile math, animation plans,
  restore math, diagnostics formatting.

Pop.Platform.Abstractions
  OS seams used by managed hosts: drag tracking, window inspection/movement,
  startup registration.

Platform hosts
  Windows  (WPF + Win32 hooks)     → implements abstractions
  Linux    (X11 + optional KWin)   → implements abstractions
  macOS    (Swift/AppKit + AOT)    → native shell; calls Core via bridge DTOs
```

`Pop.Core` is the source of truth for *whether* and *where* a snap happens. Hosts own input capture, accessibility/window APIs, animation execution, tray UI, and updates.

## Shared snap pipeline

Managed hosts should funnel drag completion through:

1. `QualifiedSnapPlanner.Decide` — velocity/dominance/cross-monitor qualification (`SnapDecider`)
2. Host-specific settle/refresh (for example Windows’ short cross-monitor delay)
3. `QualifiedSnapPlanner.TryCreatePlan` — tile bounds + `WindowAnimator` plan
4. Platform `IWindowMover` (or AX/KWin equivalent) to apply the plan
5. `SnapRestoreState` bookkeeping so the next drag can unsnap before rethrowing

Diagnostic field bags live in `SnapDiagnosticFields` so logs stay comparable across OSes.

macOS reimplements the shell orchestration in Swift (`PopRuntimeController`) but still evaluates gestures and animation plans through the Native AOT bridge into `Pop.Core`.

## Settings contract

Settings are a single JSON document with PascalCase keys:

- Canonical defaults and property names: `contracts/app-settings.contract.json`
- C#: `Pop.Core.Models.AppSettings` (+ `JsonSettingsStore`)
- Swift: `PopMacSupport.AppSettings` CodingKeys must match the contract exactly

Paths:

| Platform | Settings | Diagnostics |
|----------|----------|-------------|
| Windows  | `%LOCALAPPDATA%\Pop\settings.json` | `%LOCALAPPDATA%\Pop\diagnostics.log` |
| macOS    | `~/Library/Application Support/Pop/settings.json` | same directory |
| Linux    | `~/.config/Pop/settings.json` | `~/.config/Pop/` |

When adding a setting: update the contract file, C# model defaults, Swift model/CodingKeys, and both contract tests.

## Linux support boundaries

Linux is real but not universal:

- **X11**: full managed host with polling drag tracker and Core snap pipeline
- **Plasma Wayland**: KWin script integration (not the X11 path)
- **Other Wayland compositors**: not supported; global drag + arbitrary window moves need compositor cooperation Pop does not have yet

Treat non-Plasma Wayland as out of scope until a concrete compositor story exists. Prefer additive Linux changes and keep the X11 path stable when the machine-under-test is known-good.

## Updates

- Windows: Velopack against the `update-feed` branch
- macOS: GitHub Releases download + prepared install (writable Applications install required)
- Linux: package/AppImage distribution; no in-app updater parity yet

Shell/update UI is intentionally *not* shared across toolkits. Prefer matching UX, not a shared UI framework.

## Testing expectations

| Suite | What it guards |
|-------|----------------|
| `tests/Pop.Tests` | Core behavior, settings contract, Mac bridge DTOs |
| `tests/Pop.Tests.Windows` | Win32 adapters + update service |
| `src/Pop.App.Mac/NativeHost` Swift tests | settings, permissions, updates, contract keys |
| PR CI (`.github/workflows/ci.yml`) | shared tests on Linux/macOS runners + full solution on Windows + Swift tests |

There is no automated end-to-end “real mouse drag” suite; platform gesture paths need manual smoke testing after host changes.

## Design principles

1. **Shared physics, native shells** — keep snap feel identical; do not force one UI stack.
2. **Pure Core decisions** — hosts may delay/refresh, but qualification and tile math stay in Core.
3. **Fail soft on the hot path** — global hooks run on sensitive threads; drag completion must not crash the process.
4. **Narrow v1 surface** — left/right halves first; denser layouts only after the throw feel is solid.
5. **Contract before convenience** — settings JSON and diagnostic categories are cross-platform APIs.

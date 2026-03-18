# Pop.App.Mac

This folder contains the macOS implementation for Pop.

Current responsibilities:

- Native AOT bridge exports from `Pop.Core` for snap decisions, tile bounds, animation plans, and diagnostics formatting
- Native Swift/AppKit menu-bar host in `NativeHost`
- Accessibility-permission onboarding, global drag tracking, AX-based window movement, diagnostics logging, and launch-at-login support
- Packaging scripts that emit `Pop.app` and `Pop-macos-arm64-<version>.zip`

Build locally:

```zsh
../../scripts/package-mac-release.sh
```

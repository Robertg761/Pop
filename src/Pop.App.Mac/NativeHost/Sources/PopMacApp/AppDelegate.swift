import AppKit
import PopMacSupport

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private let runtime = PopRuntimeController()
    private let statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
    private let enabledMenuItem = NSMenuItem(title: "Enable Pop", action: #selector(toggleEnabled), keyEquivalent: "")
    private let launchMenuItem = NSMenuItem(title: "Launch At Login", action: #selector(toggleLaunchAtLogin), keyEquivalent: "")
    private let permissionMenuItem = NSMenuItem(title: "Accessibility: Unknown", action: nil, keyEquivalent: "")
    private let versionMenuItem = NSMenuItem(title: "Version", action: nil, keyEquivalent: "")
    private lazy var settingsWindowController = SettingsWindowController()
    private lazy var onboardingWindowController = OnboardingWindowController()

    func applicationDidFinishLaunching(_ notification: Notification) {
        configureMenu()
        configureWindows()
        runtime.onStateChanged = { [weak self] in
            self?.refreshMenuState()
        }
        runtime.start()

        if runtime.settings.enabled && runtime.permissionState != .granted {
            onboardingWindowController.present()
        }
    }

    private func configureMenu() {
        statusItem.button?.title = "Pop"
        let menu = NSMenu()
        enabledMenuItem.target = self
        launchMenuItem.target = self
        permissionMenuItem.isEnabled = false
        versionMenuItem.isEnabled = false
        let settingsItem = NSMenuItem(title: "Open Settings", action: #selector(openSettings), keyEquivalent: "")
        settingsItem.target = self
        let quitItem = NSMenuItem(title: "Quit", action: #selector(quit), keyEquivalent: "q")
        quitItem.target = self

        menu.addItem(enabledMenuItem)
        menu.addItem(launchMenuItem)
        menu.addItem(.separator())
        menu.addItem(permissionMenuItem)
        menu.addItem(versionMenuItem)
        menu.addItem(.separator())
        menu.addItem(settingsItem)
        menu.addItem(.separator())
        menu.addItem(quitItem)
        statusItem.menu = menu
    }

    private func configureWindows() {
        settingsWindowController.onSave = { [weak self] settings in
            self?.runtime.applySettings(settings)
            if settings.enabled && self?.runtime.permissionState != .granted {
                self?.runtime.refreshAccessibility(prompt: true)
                self?.onboardingWindowController.present()
            }
        }
        settingsWindowController.onOpenAccessibilitySettings = {
            Self.openAccessibilitySettings()
        }
        onboardingWindowController.onOpenAccessibilitySettings = {
            Self.openAccessibilitySettings()
        }
    }

    private func refreshMenuState() {
        enabledMenuItem.state = runtime.settings.enabled ? .on : .off
        launchMenuItem.state = runtime.settings.launchAtStartup ? .on : .off
        permissionMenuItem.title = runtime.permissionState == .granted ? "Accessibility: Granted" : "Accessibility: Required"
        versionMenuItem.title = "Version \(Self.currentVersion)"
    }

    @objc private func toggleEnabled() {
        let nextValue = !runtime.settings.enabled
        runtime.setEnabled(nextValue, promptForAccessibility: nextValue)
        if nextValue && runtime.permissionState != .granted {
            onboardingWindowController.present()
        }
    }

    @objc private func toggleLaunchAtLogin() {
        runtime.setLaunchAtStartup(!runtime.settings.launchAtStartup)
    }

    @objc private func openSettings() {
        settingsWindowController.present(settings: runtime.settings, permissionState: runtime.permissionState)
    }

    @objc private func quit() {
        NSApplication.shared.terminate(nil)
    }

    private static var currentVersion: String {
        (Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String) ?? "0.0.0"
    }

    private static func openAccessibilitySettings() {
        let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")!
        NSWorkspace.shared.open(url)
    }
}

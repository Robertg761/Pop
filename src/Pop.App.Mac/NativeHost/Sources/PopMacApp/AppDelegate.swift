import AppKit
import PopMacSupport

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private let runtime = PopRuntimeController()
    private let updateService = UpdateService()
    private let statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
    private let enabledMenuItem = NSMenuItem(title: "Enable Pop", action: #selector(toggleEnabled), keyEquivalent: "")
    private let launchMenuItem = NSMenuItem(title: "Launch At Login", action: #selector(toggleLaunchAtLogin), keyEquivalent: "")
    private let permissionMenuItem = NSMenuItem(title: "Accessibility: Unknown", action: nil, keyEquivalent: "")
    private let versionMenuItem = NSMenuItem(title: "Version", action: nil, keyEquivalent: "")
    private let updateStatusMenuItem = NSMenuItem(title: "Updates: Starting...", action: nil, keyEquivalent: "")
    private let checkForUpdatesMenuItem = NSMenuItem(title: "Check for Updates...", action: #selector(checkForUpdates), keyEquivalent: "")
    private let installUpdateMenuItem = NSMenuItem(title: "Install Update", action: #selector(installUpdate), keyEquivalent: "")
    private lazy var settingsWindowController = SettingsWindowController()
    private lazy var onboardingWindowController = OnboardingWindowController()
    private var lastPromptedReadyVersion: String?

    func applicationDidFinishLaunching(_ notification: Notification) {
        configureMenu()
        configureWindows()
        configureUpdates()
        runtime.onStateChanged = { [weak self] in
            self?.refreshMenuState()
        }
        runtime.start()
        updateService.start()
    }

    func applicationWillTerminate(_ notification: Notification) {
        updateService.stop()
    }

    private func configureMenu() {
        statusItem.button?.title = "Pop"
        let menu = NSMenu()
        enabledMenuItem.target = self
        launchMenuItem.target = self
        permissionMenuItem.isEnabled = false
        versionMenuItem.isEnabled = false
        updateStatusMenuItem.isEnabled = false
        checkForUpdatesMenuItem.target = self
        installUpdateMenuItem.target = self
        installUpdateMenuItem.isHidden = true
        let settingsItem = NSMenuItem(title: "Open Settings", action: #selector(openSettings), keyEquivalent: "")
        settingsItem.target = self
        let quitItem = NSMenuItem(title: "Quit", action: #selector(quit), keyEquivalent: "q")
        quitItem.target = self

        menu.addItem(enabledMenuItem)
        menu.addItem(launchMenuItem)
        menu.addItem(.separator())
        menu.addItem(permissionMenuItem)
        menu.addItem(versionMenuItem)
        menu.addItem(updateStatusMenuItem)
        menu.addItem(checkForUpdatesMenuItem)
        menu.addItem(installUpdateMenuItem)
        menu.addItem(.separator())
        menu.addItem(settingsItem)
        menu.addItem(.separator())
        menu.addItem(quitItem)
        statusItem.menu = menu
    }

    private func configureWindows() {
        settingsWindowController.onSave = { [weak self] settings in
            guard let self else {
                return
            }

            let previouslyEnabled = self.runtime.settings.enabled
            self.runtime.applySettings(settings)

            if settings.enabled && !previouslyEnabled && self.runtime.permissionState != .granted {
                self.runtime.refreshAccessibility(prompt: true)
                if self.runtime.permissionState != .granted {
                    self.onboardingWindowController.present()
                }
            }
        }
        settingsWindowController.onOpenAccessibilitySettings = {
            Self.openAccessibilitySettings()
        }
        settingsWindowController.onCheckForUpdates = {
            self.checkForUpdates()
        }
        settingsWindowController.onInstallUpdate = {
            self.installUpdate()
        }
        onboardingWindowController.onOpenAccessibilitySettings = {
            Self.openAccessibilitySettings()
        }
    }

    private func configureUpdates() {
        updateService.onStateChanged = { [weak self] state in
            self?.applyUpdateState(state)
        }
        updateService.onReadyToInstall = { [weak self] version in
            self?.promptForReadyUpdate(version: version)
        }
        applyUpdateState(updateService.currentState)
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
        settingsWindowController.present(
            settings: runtime.settings,
            permissionState: runtime.permissionState,
            updateState: updateService.currentState)
    }

    @objc private func checkForUpdates() {
        if updateService.currentState.status == .unsupported {
            showUpdateUnavailable(message: updateService.currentState.message)
            return
        }

        updateService.checkNow()
    }

    @objc private func installUpdate() {
        do {
            if try updateService.applyPendingUpdateAndRestart() {
                NSApplication.shared.terminate(nil)
            }
        } catch {
            showUpdateError(message: error.localizedDescription)
        }
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

    private func applyUpdateState(_ state: UpdateState) {
        updateStatusMenuItem.title = "Updates: \(state.message)"
        checkForUpdatesMenuItem.isEnabled = state.canCheck || state.status == .unsupported
        installUpdateMenuItem.isHidden = !state.canInstall
        installUpdateMenuItem.isEnabled = state.canInstall
        installUpdateMenuItem.title = state.availableVersion.map { "Install v\($0)" } ?? "Install Update"
        settingsWindowController.applyUpdateState(state)
    }

    private func promptForReadyUpdate(version: String?) {
        guard lastPromptedReadyVersion != version else {
            return
        }

        lastPromptedReadyVersion = version

        let alert = NSAlert()
        alert.messageText = version.map { "Pop v\($0) is ready to install." } ?? "A Pop update is ready to install."
        alert.informativeText = "Restart Pop to finish installing the downloaded update."
        alert.addButton(withTitle: "Install Update")
        alert.addButton(withTitle: "Later")
        NSApp.activate(ignoringOtherApps: true)

        if alert.runModal() == .alertFirstButtonReturn {
            installUpdate()
        }
    }

    private func showUpdateError(message: String) {
        let alert = NSAlert()
        alert.messageText = "Update Failed"
        alert.informativeText = message
        alert.runModal()
    }

    private func showUpdateUnavailable(message: String) {
        let alert = NSAlert()
        alert.messageText = "Updates Unavailable"
        alert.informativeText = message
        alert.runModal()
    }
}

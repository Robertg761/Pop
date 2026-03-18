import AppKit
import PopMacSupport

final class SettingsWindowController: NSWindowController {
    var onSave: ((AppSettings) -> Void)?
    var onOpenAccessibilitySettings: (() -> Void)?

    private let enabledButton = NSButton(checkboxWithTitle: "Enable Pop", target: nil, action: nil)
    private let launchAtStartupButton = NSButton(checkboxWithTitle: "Launch At Login", target: nil, action: nil)
    private let diagnosticsButton = NSButton(checkboxWithTitle: "Enable Diagnostics", target: nil, action: nil)
    private let throwVelocityField = NSTextField(string: "")
    private let dominanceField = NSTextField(string: "")
    private let glideDurationField = NSTextField(string: "")
    private let permissionLabel = NSTextField(labelWithString: "")
    private let touchpadHintLabel = NSTextField(wrappingLabelWithString: "When no external mouse is connected, Pop automatically lowers the effective throw velocity threshold so touchpad flicks qualify more easily.")

    init() {
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 420, height: 320),
            styleMask: [.titled, .closable],
            backing: .buffered,
            defer: false)
        window.title = "Pop Settings"
        super.init(window: window)
        configureUI()
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    func present(settings: AppSettings, permissionState: AccessibilityPermissionState) {
        enabledButton.state = settings.enabled ? .on : .off
        launchAtStartupButton.state = settings.launchAtStartup ? .on : .off
        diagnosticsButton.state = settings.enableDiagnostics ? .on : .off
        throwVelocityField.stringValue = String(format: "%.2f", settings.throwVelocityThresholdPxPerSec)
        dominanceField.stringValue = String(format: "%.2f", settings.horizontalDominanceRatio)
        glideDurationField.stringValue = "\(settings.glideDurationMs)"
        permissionLabel.stringValue = permissionState == .granted ? "Accessibility: Granted" : "Accessibility: Required for snapping"

        showWindow(nil)
        window?.center()
        window?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    private func configureUI() {
        guard let contentView = window?.contentView else {
            return
        }

        let velocityRow = formRow(label: "Throw Velocity", field: throwVelocityField)
        let dominanceRow = formRow(label: "Horizontal Dominance", field: dominanceField)
        let glideRow = formRow(label: "Glide Duration (ms)", field: glideDurationField)

        let openAccessibilityButton = NSButton(title: "Open Accessibility Settings", target: self, action: #selector(openAccessibilitySettings))
        let saveButton = NSButton(title: "Save", target: self, action: #selector(save))
        let cancelButton = NSButton(title: "Cancel", target: self, action: #selector(cancel))

        let buttonRow = NSStackView(views: [cancelButton, saveButton])
        buttonRow.orientation = .horizontal
        buttonRow.alignment = .centerY
        buttonRow.distribution = .fillEqually
        buttonRow.spacing = 12

        let stack = NSStackView(views: [
            enabledButton,
            launchAtStartupButton,
            diagnosticsButton,
            velocityRow,
            touchpadHintLabel,
            dominanceRow,
            glideRow,
            permissionLabel,
            openAccessibilityButton,
            buttonRow
        ])
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 12
        stack.translatesAutoresizingMaskIntoConstraints = false
        touchpadHintLabel.textColor = .secondaryLabelColor

        contentView.addSubview(stack)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: contentView.leadingAnchor, constant: 20),
            stack.trailingAnchor.constraint(equalTo: contentView.trailingAnchor, constant: -20),
            stack.topAnchor.constraint(equalTo: contentView.topAnchor, constant: 20)
        ])
    }

    private func formRow(label: String, field: NSTextField) -> NSStackView {
        field.translatesAutoresizingMaskIntoConstraints = false
        field.widthAnchor.constraint(equalToConstant: 120).isActive = true

        let labelField = NSTextField(labelWithString: label)
        let row = NSStackView(views: [labelField, field])
        row.orientation = .horizontal
        row.alignment = .centerY
        row.spacing = 12
        return row
    }

    @objc private func openAccessibilitySettings() {
        onOpenAccessibilitySettings?()
    }

    @objc private func save() {
        guard let settings = validatedSettings() else {
            return
        }

        onSave?(settings)
        window?.orderOut(nil)
    }

    @objc private func cancel() {
        window?.orderOut(nil)
    }

    private func validatedSettings() -> AppSettings? {
        guard let throwVelocity = Double(throwVelocityField.stringValue), throwVelocity >= 100 else {
            presentValidationError("Throw velocity must be a number greater than or equal to 100.")
            return nil
        }

        guard let dominanceRatio = Double(dominanceField.stringValue), dominanceRatio >= 1 else {
            presentValidationError("Horizontal dominance must be a number greater than or equal to 1.")
            return nil
        }

        guard let glideDuration = Int(glideDurationField.stringValue), glideDuration >= 50, glideDuration <= 1000 else {
            presentValidationError("Glide duration must be an integer between 50 and 1000 milliseconds.")
            return nil
        }

        return AppSettings(
            enabled: enabledButton.state == .on,
            launchAtStartup: launchAtStartupButton.state == .on,
            throwVelocityThresholdPxPerSec: throwVelocity,
            horizontalDominanceRatio: dominanceRatio,
            glideDurationMs: glideDuration,
            enableDiagnostics: diagnosticsButton.state == .on)
    }

    private func presentValidationError(_ message: String) {
        let alert = NSAlert()
        alert.messageText = "Invalid Settings"
        alert.informativeText = message
        alert.runModal()
    }
}

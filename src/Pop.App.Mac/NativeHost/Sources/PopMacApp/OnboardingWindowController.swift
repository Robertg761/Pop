import AppKit

final class OnboardingWindowController: NSWindowController {
    var onOpenAccessibilitySettings: (() -> Void)?

    init() {
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 420, height: 220),
            styleMask: [.titled, .closable],
            backing: .buffered,
            defer: false)
        window.title = "Enable Accessibility"
        super.init(window: window)
        configureUI()
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    func present() {
        showWindow(nil)
        window?.center()
        window?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    private func configureUI() {
        guard let contentView = window?.contentView else {
            return
        }

        let titleLabel = NSTextField(labelWithString: "Pop needs Accessibility permission to watch drags and move windows.")
        titleLabel.maximumNumberOfLines = 0
        let detailLabel = NSTextField(labelWithString: "Open System Settings, grant Pop access under Privacy & Security > Accessibility, then return here.")
        detailLabel.maximumNumberOfLines = 0

        let openButton = NSButton(title: "Open Accessibility Settings", target: self, action: #selector(openAccessibilitySettings))
        let closeButton = NSButton(title: "Close", target: self, action: #selector(closeWindow))

        let stack = NSStackView(views: [titleLabel, detailLabel, openButton, closeButton])
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 14
        stack.translatesAutoresizingMaskIntoConstraints = false

        contentView.addSubview(stack)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: contentView.leadingAnchor, constant: 20),
            stack.trailingAnchor.constraint(equalTo: contentView.trailingAnchor, constant: -20),
            stack.topAnchor.constraint(equalTo: contentView.topAnchor, constant: 20)
        ])
    }

    @objc private func openAccessibilitySettings() {
        onOpenAccessibilitySettings?()
    }

    @objc private func closeWindow() {
        window?.orderOut(nil)
    }
}

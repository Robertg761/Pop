import AppKit
import PopMacSupport

@MainActor
private final class GlobalDragTracker {
    struct ActiveSession {
        var window: AXUIElement
        var initialMonitor: DesktopScreen
        var currentMonitor: DesktopScreen
        var initialBounds: DesktopRect
        var currentBounds: DesktopRect
        var samples: [DragSample]
    }

    var onRejected: ((DesktopPoint, String) -> Void)?
    var onCompleted: ((ActiveSession, Bool) -> Void)?

    private let windowSystem: AccessibilityWindowSystem
    private let screenCoordinator: ScreenCoordinator
    private var eventTap: CFMachPort?
    private var runLoopSource: CFRunLoopSource?
    private var activeSession: ActiveSession?

    init(windowSystem: AccessibilityWindowSystem, screenCoordinator: ScreenCoordinator) {
        self.windowSystem = windowSystem
        self.screenCoordinator = screenCoordinator
    }

    func start() {
        guard eventTap == nil else {
            return
        }

        let mask = (1 << CGEventType.leftMouseDown.rawValue) |
            (1 << CGEventType.leftMouseDragged.rawValue) |
            (1 << CGEventType.leftMouseUp.rawValue)

        let callback: CGEventTapCallBack = { _, type, event, userInfo in
            let tracker = Unmanaged<GlobalDragTracker>.fromOpaque(userInfo!).takeUnretainedValue()
            tracker.handle(type: type, event: event)
            return Unmanaged.passUnretained(event)
        }

        guard let eventTap = CGEvent.tapCreate(
            tap: .cgSessionEventTap,
            place: .headInsertEventTap,
            options: .listenOnly,
            eventsOfInterest: CGEventMask(mask),
            callback: callback,
            userInfo: Unmanaged.passUnretained(self).toOpaque()) else {
            return
        }

        let source = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, eventTap, 0)
        self.eventTap = eventTap
        self.runLoopSource = source
        CFRunLoopAddSource(CFRunLoopGetMain(), source, .commonModes)
        CGEvent.tapEnable(tap: eventTap, enable: true)
    }

    func stop() {
        activeSession = nil

        if let source = runLoopSource {
            CFRunLoopRemoveSource(CFRunLoopGetMain(), source, .commonModes)
        }

        if let eventTap {
            CFMachPortInvalidate(eventTap)
        }

        eventTap = nil
        runLoopSource = nil
    }

    private func handle(type: CGEventType, event: CGEvent) {
        let timestamp = Int64(Date().timeIntervalSince1970 * 1000)
        let point = screenCoordinator.pointInTopLeftSpace(from: event.location)

        switch type {
        case .leftMouseDown:
            let inspection = windowSystem.inspectWindow(atEventLocation: event.location)
            guard inspection.isSupported, let window = inspection.window, let monitor = inspection.monitor else {
                onRejected?(point, inspection.reason)
                return
            }

            activeSession = ActiveSession(
                window: window,
                initialMonitor: monitor,
                currentMonitor: monitor,
                initialBounds: inspection.bounds,
                currentBounds: inspection.bounds,
                samples: [DragSample(point: point, timestampUnixMilliseconds: timestamp)])
        case .leftMouseDragged:
            guard var session = activeSession else {
                return
            }

            session.samples.append(DragSample(point: point, timestampUnixMilliseconds: timestamp))
            if let state = windowSystem.currentState(of: session.window) {
                session.currentBounds = state.bounds
                if let monitor = state.monitor {
                    session.currentMonitor = monitor
                }
            }

            activeSession = session
        case .leftMouseUp:
            guard var session = activeSession else {
                return
            }

            session.samples.append(DragSample(point: point, timestampUnixMilliseconds: timestamp))
            if let state = windowSystem.currentState(of: session.window) {
                session.currentBounds = state.bounds
                if let monitor = state.monitor {
                    session.currentMonitor = monitor
                }
            }

            let isOptionPressed = event.flags.contains(.maskAlternate)
            activeSession = nil
            onCompleted?(session, isOptionPressed)
        default:
            break
        }
    }
}

final class DiagnosticsLogger {
    private let fileURL: URL
    private let bridgeClient: PopMacBridgeClient
    private let queue = DispatchQueue(label: "Pop.DiagnosticsLogger")

    init(directoryURL: URL, bridgeClient: PopMacBridgeClient) {
        self.fileURL = directoryURL.appendingPathComponent("diagnostics.log")
        self.bridgeClient = bridgeClient
    }

    func write(category: String, message: String, fields: [String: String?], enabled: Bool) {
        guard enabled else {
            return
        }

        let line = bridgeClient.formatDiagnosticEvent(timestamp: Date(), category: category, message: message, fields: fields)
        let fileURL = self.fileURL
        queue.async {
            do {
                try FileManager.default.createDirectory(at: fileURL.deletingLastPathComponent(), withIntermediateDirectories: true)
                let data = (line + "\n").data(using: .utf8) ?? Data()
                if FileManager.default.fileExists(atPath: fileURL.path) {
                    let handle = try FileHandle(forWritingTo: fileURL)
                    try handle.seekToEnd()
                    try handle.write(contentsOf: data)
                    try handle.close()
                } else {
                    try data.write(to: fileURL, options: .atomic)
                }
            } catch {
            }
        }
    }
}

@MainActor
final class PopRuntimeController {
    var onStateChanged: (() -> Void)?

    private let settingsStore: AppSettingsStore
    private let launchAgentManager: LaunchAgentManager
    private let permissionCoordinator: AccessibilityPermissionCoordinator
    private let screenCoordinator = ScreenCoordinator()
    private lazy var windowSystem = AccessibilityWindowSystem(screenCoordinator: screenCoordinator)
    private lazy var dragTracker = GlobalDragTracker(windowSystem: windowSystem, screenCoordinator: screenCoordinator)
    private let bridgeClient = PopMacBridgeClient()
    private lazy var diagnosticsLogger = DiagnosticsLogger(directoryURL: settingsStore.directoryURL, bridgeClient: bridgeClient)

    private(set) var settings = AppSettings.default
    private(set) var permissionState: AccessibilityPermissionState = .needsApproval

    init(
        settingsStore: AppSettingsStore = AppSettingsStore(),
        launchAgentManager: LaunchAgentManager = LaunchAgentManager(),
        permissionCoordinator: AccessibilityPermissionCoordinator = AccessibilityPermissionCoordinator(client: LiveAccessibilityPermissionClient())
    ) {
        self.settingsStore = settingsStore
        self.launchAgentManager = launchAgentManager
        self.permissionCoordinator = permissionCoordinator
    }

    func start() {
        settings = (try? settingsStore.load()) ?? .default
        permissionState = permissionCoordinator.refresh(prompt: false)
        dragTracker.onRejected = { [weak self] point, reason in
            self?.diagnosticsLogger.write(
                category: "drag-ignored",
                message: "Pointer down did not start a Pop drag session.",
                fields: [
                    "reason": reason,
                    "point": "{\(point.x),\(point.y)}"
                ],
                enabled: self?.settings.enableDiagnostics == true)
        }
        dragTracker.onCompleted = { [weak self] session, isOptionPressed in
            self?.handleCompletedDrag(session: session, isOptionPressed: isOptionPressed)
        }
        syncLaunchAgent()
        refreshTracking(prompt: false)
        onStateChanged?()
    }

    func setEnabled(_ enabled: Bool, promptForAccessibility: Bool) {
        settings.enabled = enabled
        persistSettings()
        refreshTracking(prompt: promptForAccessibility && enabled)
        onStateChanged?()
    }

    func setLaunchAtStartup(_ enabled: Bool) {
        settings.launchAtStartup = enabled
        syncLaunchAgent()
        persistSettings()
        onStateChanged?()
    }

    func applySettings(_ updatedSettings: AppSettings) {
        settings = updatedSettings
        syncLaunchAgent()
        persistSettings()
        refreshTracking(prompt: false)
        onStateChanged?()
    }

    func refreshAccessibility(prompt: Bool) {
        refreshTracking(prompt: prompt)
        onStateChanged?()
    }

    private func refreshTracking(prompt: Bool) {
        permissionState = permissionCoordinator.refresh(prompt: prompt)
        if settings.enabled && permissionState == .granted {
            dragTracker.start()
        } else {
            dragTracker.stop()
        }
    }

    private func syncLaunchAgent() {
        do {
            try launchAgentManager.setEnabled(settings.launchAtStartup)
        } catch {
        }
    }

    private func persistSettings() {
        do {
            try settingsStore.save(settings)
        } catch {
        }
    }

    private func handleCompletedDrag(session: GlobalDragTracker.ActiveSession, isOptionPressed: Bool) {
        let monitors = screenCoordinator.screens()
        let context = DragDecisionContext(
            initialMonitor: session.initialMonitor,
            currentMonitor: session.currentMonitor,
            initialBounds: session.initialBounds,
            currentBounds: session.currentBounds,
            isOptionPressedAtRelease: isOptionPressed)
        let decision = bridgeClient.evaluateDragGesture(
            samples: session.samples,
            monitors: monitors,
            context: context,
            settings: settings)

        if !decision.isQualified {
            diagnosticsLogger.write(
                category: "drag-release",
                message: "Release did not qualify for snapping.",
                fields: [
                    "target": "\(decision.target)",
                    "reason": "\(decision.rejectionReason)",
                    "velocityX": "\(Int(decision.horizontalVelocityPxPerSec.rounded()))",
                    "velocityY": "\(Int(decision.verticalVelocityPxPerSec.rounded()))"
                ],
                enabled: settings.enableDiagnostics)
            return
        }

        let targetBounds = bridgeClient.tileBounds(target: decision.target, monitor: decision.targetMonitor)
        let plan = bridgeClient.animationPlan(
            startBounds: session.currentBounds,
            targetBounds: targetBounds,
            releaseVelocityX: decision.horizontalVelocityPxPerSec,
            durationMs: settings.glideDurationMs)

        diagnosticsLogger.write(
            category: "drag-release",
            message: "Snap qualified and animation plan generated.",
            fields: [
                "target": "\(decision.target)",
                "frames": "\(plan.frames.count)",
                "overshootPx": "\(plan.maxOvershootPx)"
            ],
            enabled: settings.enableDiagnostics)

        animate(window: session.window, frames: plan.frames, finalBounds: plan.finalBounds, index: 0, startedAt: DispatchTime.now())
    }

    private func animate(window: AXUIElement, frames: [BridgeAnimationFrame], finalBounds: DesktopRect, index: Int, startedAt: DispatchTime) {
        guard index < frames.count else {
            windowSystem.move(window: window, to: finalBounds)
            return
        }

        let frame = frames[index]
        let elapsedNanoseconds = DispatchTime.now().uptimeNanoseconds - startedAt.uptimeNanoseconds
        let targetNanoseconds = UInt64(frame.offsetMilliseconds) * 1_000_000
        let deadlineOffset = targetNanoseconds > elapsedNanoseconds ? Double(targetNanoseconds - elapsedNanoseconds) / 1_000_000_000 : 0

        DispatchQueue.main.asyncAfter(deadline: .now() + deadlineOffset) { [weak self] in
            guard let self else {
                return
            }

            self.windowSystem.move(window: window, to: frame.bounds)
            self.animate(window: window, frames: frames, finalBounds: finalBounds, index: index + 1, startedAt: startedAt)
        }
    }
}

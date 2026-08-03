import AppKit
import ApplicationServices

struct WindowInspection {
    var window: AXUIElement?
    var bounds: DesktopRect
    var monitor: DesktopScreen?
    var isSupported: Bool
    var reason: String
}

@MainActor
final class AccessibilityWindowSystem {
    // AX calls into a hung app block the caller for ~6 seconds by default; keep the
    // main thread responsive by capping per-element messaging time.
    private static let axMessagingTimeoutSeconds: Float = 0.5

    private let screenCoordinator: ScreenCoordinator
    private let currentProcessIdentifier: pid_t

    init(screenCoordinator: ScreenCoordinator, currentProcessIdentifier: pid_t = ProcessInfo.processInfo.processIdentifier) {
        self.screenCoordinator = screenCoordinator
        self.currentProcessIdentifier = currentProcessIdentifier
    }

    func inspectWindow(atEventLocation location: CGPoint) -> WindowInspection {
        let point = screenCoordinator.pointInTopLeftSpace(from: location)
        let systemWide = AXUIElementCreateSystemWide()
        AXUIElementSetMessagingTimeout(systemWide, Self.axMessagingTimeoutSeconds)
        var element: AXUIElement?
        let error = AXUIElementCopyElementAtPosition(systemWide, Float(location.x), Float(location.y), &element)
        if let hitElement = element {
            AXUIElementSetMessagingTimeout(hitElement, Self.axMessagingTimeoutSeconds)
        }

        guard error == .success, let hitElement = element, let window = windowElement(from: hitElement), let bounds = bounds(for: window) else {
            return WindowInspection(window: nil, bounds: DesktopRect(x: 0, y: 0, width: 0, height: 0), monitor: nil, isSupported: false, reason: "No eligible window was found under the pointer.")
        }

        var pid: pid_t = 0
        AXUIElementGetPid(window, &pid)
        let monitor = screenCoordinator.monitor(containing: bounds)
        let role = stringAttribute(kAXRoleAttribute, from: window)
        let subrole = stringAttribute(kAXSubroleAttribute, from: window)
        let isResizable = boolAttribute("AXResizable", from: window) ?? true
        let isMinimized = boolAttribute(kAXMinimizedAttribute, from: window) ?? false
        let isFullscreen = boolAttribute("AXFullScreen", from: window) ?? false
        let isCurrentProcessWindow = pid == currentProcessIdentifier
        let isStandardWindow = role == (kAXWindowRole as String) && (subrole == nil || subrole == (kAXStandardWindowSubrole as String))
        let titleBarHeight = max(28, min(72, bounds.height / 6))
        let isTitleBarHit = point.y >= bounds.y && point.y <= bounds.y + titleBarHeight

        if !isTitleBarHit {
            return WindowInspection(window: window, bounds: bounds, monitor: monitor, isSupported: false, reason: "Pointer was not over a title bar.")
        }

        if isCurrentProcessWindow {
            return WindowInspection(window: window, bounds: bounds, monitor: monitor, isSupported: false, reason: "Pop ignores its own windows.")
        }

        if !isStandardWindow {
            return WindowInspection(window: window, bounds: bounds, monitor: monitor, isSupported: false, reason: "Window is not a standard top-level desktop window.")
        }

        if !isResizable {
            return WindowInspection(window: window, bounds: bounds, monitor: monitor, isSupported: false, reason: "Window does not expose a resizable frame.")
        }

        if isMinimized {
            return WindowInspection(window: window, bounds: bounds, monitor: monitor, isSupported: false, reason: "Minimized windows cannot be thrown into tiles.")
        }

        if isFullscreen {
            return WindowInspection(window: window, bounds: bounds, monitor: monitor, isSupported: false, reason: "Fullscreen windows are ignored.")
        }

        return WindowInspection(window: window, bounds: bounds, monitor: monitor, isSupported: true, reason: "Window is eligible for Pop momentum snapping.")
    }

    func currentState(of window: AXUIElement) -> (bounds: DesktopRect, monitor: DesktopScreen?)? {
        guard let bounds = bounds(for: window) else {
            return nil
        }

        return (bounds, screenCoordinator.monitor(containing: bounds))
    }

    @discardableResult
    func move(window: AXUIElement, to rect: DesktopRect) -> Bool {
        var position = screenCoordinator.axPoint(fromTopLeftRect: rect)
        var size = CGSize(width: rect.width, height: rect.height)
        var success = true

        if let positionValue = AXValueCreate(.cgPoint, &position) {
            success = AXUIElementSetAttributeValue(window, kAXPositionAttribute as CFString, positionValue) == .success && success
        } else {
            success = false
        }

        if let sizeValue = AXValueCreate(.cgSize, &size) {
            success = AXUIElementSetAttributeValue(window, kAXSizeAttribute as CFString, sizeValue) == .success && success
        } else {
            success = false
        }

        // Re-apply the position after resizing: some apps shift the window while applying
        // the new size (notably across monitors with different scales), and the known AX
        // pattern for reliable cross-monitor moves is position, size, position.
        if let positionValue = AXValueCreate(.cgPoint, &position) {
            success = AXUIElementSetAttributeValue(window, kAXPositionAttribute as CFString, positionValue) == .success && success
        }

        return success
    }

    private func windowElement(from element: AXUIElement) -> AXUIElement? {
        var currentElement: AXUIElement? = element
        while let current = currentElement {
            if stringAttribute(kAXRoleAttribute, from: current) == (kAXWindowRole as String) {
                return current
            }

            currentElement = elementAttribute(kAXParentAttribute, from: current)
        }

        return nil
    }

    private func bounds(for window: AXUIElement) -> DesktopRect? {
        guard let position = pointAttribute(kAXPositionAttribute, from: window), let size = sizeAttribute(kAXSizeAttribute, from: window) else {
            return nil
        }

        return screenCoordinator.windowRect(fromAXRect: CGRect(origin: position, size: size))
    }

    private func elementAttribute(_ attribute: String, from element: AXUIElement) -> AXUIElement? {
        var value: CFTypeRef?
        let error = AXUIElementCopyAttributeValue(element, attribute as CFString, &value)
        guard error == .success, let value else {
            return nil
        }

        guard CFGetTypeID(value) == AXUIElementGetTypeID() else {
            return nil
        }

        let copiedElement = value as! AXUIElement
        AXUIElementSetMessagingTimeout(copiedElement, Self.axMessagingTimeoutSeconds)
        return copiedElement
    }

    private func stringAttribute(_ attribute: String, from element: AXUIElement) -> String? {
        var value: CFTypeRef?
        let error = AXUIElementCopyAttributeValue(element, attribute as CFString, &value)
        guard error == .success else {
            return nil
        }

        return value as? String
    }

    private func boolAttribute(_ attribute: String, from element: AXUIElement) -> Bool? {
        var value: CFTypeRef?
        let error = AXUIElementCopyAttributeValue(element, attribute as CFString, &value)
        guard error == .success, let value else {
            return nil
        }

        guard CFGetTypeID(value) == CFBooleanGetTypeID() else {
            return nil
        }

        return CFBooleanGetValue((value as! CFBoolean))
    }

    private func pointAttribute(_ attribute: String, from element: AXUIElement) -> CGPoint? {
        var value: CFTypeRef?
        let error = AXUIElementCopyAttributeValue(element, attribute as CFString, &value)
        guard error == .success, let value else {
            return nil
        }

        guard CFGetTypeID(value) == AXValueGetTypeID() else {
            return nil
        }

        let axValue = value as! AXValue
        var point = CGPoint.zero
        return AXValueGetValue(axValue, .cgPoint, &point) ? point : nil
    }

    private func sizeAttribute(_ attribute: String, from element: AXUIElement) -> CGSize? {
        var value: CFTypeRef?
        let error = AXUIElementCopyAttributeValue(element, attribute as CFString, &value)
        guard error == .success, let value else {
            return nil
        }

        guard CFGetTypeID(value) == AXValueGetTypeID() else {
            return nil
        }

        let axValue = value as! AXValue
        var size = CGSize.zero
        return AXValueGetValue(axValue, .cgSize, &size) ? size : nil
    }
}

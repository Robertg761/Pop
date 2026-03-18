import ApplicationServices
import Foundation

public enum AccessibilityPermissionState: Equatable {
    case granted
    case needsApproval
}

public protocol AccessibilityPermissionClient {
    func isTrusted(prompt: Bool) -> Bool
}

public final class AccessibilityPermissionCoordinator {
    private let client: AccessibilityPermissionClient

    public init(client: AccessibilityPermissionClient) {
        self.client = client
    }

    @discardableResult
    public func refresh(prompt: Bool) -> AccessibilityPermissionState {
        client.isTrusted(prompt: prompt) ? .granted : .needsApproval
    }
}

public struct LiveAccessibilityPermissionClient: AccessibilityPermissionClient {
    public init() {
    }

    public func isTrusted(prompt: Bool) -> Bool {
        let options = ["AXTrustedCheckOptionPrompt": prompt] as CFDictionary
        return AXIsProcessTrustedWithOptions(options)
    }
}

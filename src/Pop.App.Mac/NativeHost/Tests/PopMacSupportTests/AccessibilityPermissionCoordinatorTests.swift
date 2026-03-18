import Testing
@testable import PopMacSupport

private struct FakeAccessibilityPermissionClient: AccessibilityPermissionClient {
    let granted: Bool

    func isTrusted(prompt: Bool) -> Bool {
        granted
    }
}

struct AccessibilityPermissionCoordinatorTests {
    @Test
    func refreshReturnsGrantedWhenClientTrusted() {
        let coordinator = AccessibilityPermissionCoordinator(client: FakeAccessibilityPermissionClient(granted: true))

        #expect(coordinator.refresh(prompt: false) == .granted)
    }

    @Test
    func refreshReturnsNeedsApprovalWhenClientUntrusted() {
        let coordinator = AccessibilityPermissionCoordinator(client: FakeAccessibilityPermissionClient(granted: false))

        #expect(coordinator.refresh(prompt: true) == .needsApproval)
    }
}

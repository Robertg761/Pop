import Testing
@testable import PopMacSupport

struct PointerSensitivityResolverTests {
    @Test
    func resolvedSettingsLeavesThresholdAloneWhenExternalMouseIsPresent() {
        let settings = AppSettings(throwVelocityThresholdPxPerSec: 1800)

        let resolved = PointerSensitivityResolver.resolvedSettings(from: settings, hasExternalMouse: true)

        #expect(resolved == settings)
    }

    @Test
    func resolvedSettingsLowersThresholdForTouchpadOnlyUse() {
        let settings = AppSettings(throwVelocityThresholdPxPerSec: 1800)

        let resolved = PointerSensitivityResolver.resolvedSettings(from: settings, hasExternalMouse: false)

        #expect(resolved.throwVelocityThresholdPxPerSec == 1260)
    }

    @Test
    func resolvedSettingsClampsThresholdToMinimumValue() {
        let settings = AppSettings(throwVelocityThresholdPxPerSec: 120)

        let resolved = PointerSensitivityResolver.resolvedSettings(from: settings, hasExternalMouse: false)

        #expect(resolved.throwVelocityThresholdPxPerSec == 100)
    }
}

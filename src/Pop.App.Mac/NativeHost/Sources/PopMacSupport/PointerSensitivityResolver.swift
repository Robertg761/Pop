import Foundation

public enum PointerSensitivityResolver {
    private static let touchpadOnlyThrowVelocityMultiplier = 0.7
    private static let minimumThrowVelocityThresholdPxPerSec = 100.0

    public static func resolvedSettings(from settings: AppSettings, hasExternalMouse: Bool) -> AppSettings {
        guard !hasExternalMouse else {
            return settings
        }

        var adjustedSettings = settings
        adjustedSettings.throwVelocityThresholdPxPerSec = max(
            minimumThrowVelocityThresholdPxPerSec,
            settings.throwVelocityThresholdPxPerSec * touchpadOnlyThrowVelocityMultiplier)
        return adjustedSettings
    }
}

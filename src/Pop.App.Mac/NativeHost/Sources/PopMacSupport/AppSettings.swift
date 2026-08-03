import Foundation

/// Cross-platform settings document. JSON keys must stay aligned with
/// `contracts/app-settings.contract.json` and `Pop.Core.Models.AppSettings`.
public struct AppSettings: Codable, Equatable, Sendable {
    public var enabled: Bool
    public var launchAtStartup: Bool
    public var throwVelocityThresholdPxPerSec: Double
    public var horizontalDominanceRatio: Double
    public var glideDurationMs: Int
    public var enableDiagnostics: Bool

    public init(
        enabled: Bool = true,
        launchAtStartup: Bool = false,
        throwVelocityThresholdPxPerSec: Double = 1800,
        horizontalDominanceRatio: Double = 1.75,
        glideDurationMs: Int = 220,
        enableDiagnostics: Bool = false
    ) {
        self.enabled = enabled
        self.launchAtStartup = launchAtStartup
        self.throwVelocityThresholdPxPerSec = throwVelocityThresholdPxPerSec
        self.horizontalDominanceRatio = horizontalDominanceRatio
        self.glideDurationMs = glideDurationMs
        self.enableDiagnostics = enableDiagnostics
    }

    public static let `default` = AppSettings()

    /// Returns a copy with numeric fields clamped to safe ranges, mirroring
    /// `Pop.Core.Models.AppSettings.Normalized()`. Values from a hand-edited or corrupt
    /// `settings.json` otherwise break gesture qualification and can trap when narrowed
    /// to 32-bit values at the bridge boundary.
    public func normalized() -> AppSettings {
        var settings = self
        settings.throwVelocityThresholdPxPerSec = throwVelocityThresholdPxPerSec.isFinite
            ? min(max(throwVelocityThresholdPxPerSec, 50), 100_000)
            : AppSettings.default.throwVelocityThresholdPxPerSec
        settings.horizontalDominanceRatio = horizontalDominanceRatio.isFinite
            ? min(max(horizontalDominanceRatio, 1), 50)
            : AppSettings.default.horizontalDominanceRatio
        settings.glideDurationMs = min(max(glideDurationMs, 0), 5_000)
        return settings
    }

    enum CodingKeys: String, CodingKey {
        case enabled = "Enabled"
        case launchAtStartup = "LaunchAtStartup"
        case throwVelocityThresholdPxPerSec = "ThrowVelocityThresholdPxPerSec"
        case horizontalDominanceRatio = "HorizontalDominanceRatio"
        case glideDurationMs = "GlideDurationMs"
        case enableDiagnostics = "EnableDiagnostics"
    }
}

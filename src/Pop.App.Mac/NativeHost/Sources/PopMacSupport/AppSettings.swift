import Foundation

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

    enum CodingKeys: String, CodingKey {
        case enabled = "Enabled"
        case launchAtStartup = "LaunchAtStartup"
        case throwVelocityThresholdPxPerSec = "ThrowVelocityThresholdPxPerSec"
        case horizontalDominanceRatio = "HorizontalDominanceRatio"
        case glideDurationMs = "GlideDurationMs"
        case enableDiagnostics = "EnableDiagnostics"
    }
}

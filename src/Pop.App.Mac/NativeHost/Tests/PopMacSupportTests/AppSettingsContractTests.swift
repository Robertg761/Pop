import Foundation
import Testing
@testable import PopMacSupport

struct AppSettingsContractTests {
    @Test
    func defaultValuesMatchSharedContract() {
        let settings = AppSettings.default

        #expect(settings.enabled == true)
        #expect(settings.launchAtStartup == false)
        #expect(settings.throwVelocityThresholdPxPerSec == 1800)
        #expect(settings.horizontalDominanceRatio == 1.75)
        #expect(settings.glideDurationMs == 220)
        #expect(settings.enableDiagnostics == false)
    }

    @Test
    func jsonKeysMatchSharedContract() throws {
        let encoder = JSONEncoder()
        let data = try encoder.encode(AppSettings.default)
        let object = try #require(JSONSerialization.jsonObject(with: data) as? [String: Any])

        let keys = Set(object.keys)
        #expect(keys == Set([
            "Enabled",
            "LaunchAtStartup",
            "ThrowVelocityThresholdPxPerSec",
            "HorizontalDominanceRatio",
            "GlideDurationMs",
            "EnableDiagnostics"
        ]))
    }
}

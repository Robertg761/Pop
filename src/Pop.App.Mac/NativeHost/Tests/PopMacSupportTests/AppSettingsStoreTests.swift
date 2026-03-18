import Foundation
import Testing
@testable import PopMacSupport

struct AppSettingsStoreTests {
    @Test
    func loadReturnsDefaultsWhenFileMissing() throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
        let store = AppSettingsStore(directoryURL: directory)

        let settings = try store.load()

        #expect(settings == .default)
    }

    @Test
    func saveAndLoadRoundTripsSettings() throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
        let store = AppSettingsStore(directoryURL: directory)
        let original = AppSettings(
            enabled: false,
            launchAtStartup: true,
            throwVelocityThresholdPxPerSec: 2200,
            horizontalDominanceRatio: 2.0,
            glideDurationMs: 300,
            enableDiagnostics: true)

        try store.save(original)

        #expect(try store.load() == original)
    }
}

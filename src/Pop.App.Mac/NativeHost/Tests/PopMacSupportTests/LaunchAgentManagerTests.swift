import Foundation
import Testing
@testable import PopMacSupport

struct LaunchAgentManagerTests {
    @Test
    func setEnabledWritesLaunchAgentPlist() throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
        let manager = LaunchAgentManager(
            launchAgentsDirectoryURL: directory,
            executablePathProvider: { "/Applications/Pop.app/Contents/MacOS/PopMacApp" })

        try manager.setEnabled(true)

        #expect(manager.isEnabled())
        let plistData = try Data(contentsOf: manager.plistURL)
        let plist = try PropertyListSerialization.propertyList(from: plistData, format: nil) as? [String: Any]
        let arguments = plist?["ProgramArguments"] as? [String]
        #expect(arguments == ["/Applications/Pop.app/Contents/MacOS/PopMacApp"])
    }

    @Test
    func setEnabledFalseRemovesPlist() throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
        let manager = LaunchAgentManager(
            launchAgentsDirectoryURL: directory,
            executablePathProvider: { "/Applications/Pop.app/Contents/MacOS/PopMacApp" })
        try manager.setEnabled(true)

        try manager.setEnabled(false)

        #expect(!manager.isEnabled())
    }
}

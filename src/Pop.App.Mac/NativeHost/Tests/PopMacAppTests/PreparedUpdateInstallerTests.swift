import Foundation
import Testing
@testable import PopMacApp

struct PreparedUpdateInstallerTests {
    @Test
    func prepareUpdateExtractsBundleAndPersistsMetadata() throws {
        let fixture = try InstallerTestFixture()
        let installer = fixture.makeInstaller()
        let archiveURL = try fixture.makeArchive(version: "1.2.3")

        let preparedUpdate = try installer.prepareUpdate(fromArchiveAt: archiveURL, version: "1.2.3")

        #expect(preparedUpdate.version == "1.2.3")
        #expect(FileManager.default.fileExists(atPath: preparedUpdate.stagedAppURL.path))
        #expect(preparedUpdate.stagedAppURL.lastPathComponent == "Pop.app")
        #expect(try installer.loadPreparedUpdate()?.version == "1.2.3")
    }

    @Test
    func removeObsoletePreparedUpdateClearsCurrentOrOlderPreparedVersion() throws {
        let fixture = try InstallerTestFixture()
        let installer = fixture.makeInstaller()
        let archiveURL = try fixture.makeArchive(version: "2.0.0")

        _ = try installer.prepareUpdate(fromArchiveAt: archiveURL, version: "2.0.0")
        try installer.removeObsoletePreparedUpdate(ifCurrentVersion: "2.0.0")

        #expect(try installer.loadPreparedUpdate() == nil)
    }

    @Test
    func loadPreparedUpdateRemovesStaleMetadataWhenStagedBundleIsMissing() throws {
        let fixture = try InstallerTestFixture()
        let installer = fixture.makeInstaller()
        let archiveURL = try fixture.makeArchive(version: "3.0.0")

        let preparedUpdate = try installer.prepareUpdate(fromArchiveAt: archiveURL, version: "3.0.0")
        try FileManager.default.removeItem(at: preparedUpdate.stagedAppURL)

        #expect(try installer.loadPreparedUpdate() == nil)
    }
}

struct InstallerTestFixture {
    let rootURL: URL
    let updatesURL: URL
    let targetAppURL: URL

    init(targetIsAppBundle: Bool = true) throws {
        rootURL = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
        updatesURL = rootURL.appendingPathComponent("Updates", isDirectory: true)
        let targetDirectoryURL = rootURL.appendingPathComponent("Applications", isDirectory: true)
        targetAppURL = targetDirectoryURL.appendingPathComponent(targetIsAppBundle ? "Pop.app" : "Pop")

        try FileManager.default.createDirectory(at: targetAppURL.deletingLastPathComponent(), withIntermediateDirectories: true)
        if targetIsAppBundle {
            try Self.createAppBundle(at: targetAppURL, executableContents: "current-build")
        } else {
            try "current-build".write(to: targetAppURL, atomically: true, encoding: .utf8)
        }
    }

    func makeInstaller() -> PreparedUpdateInstaller {
        PreparedUpdateInstaller(baseDirectoryURL: updatesURL, targetAppURL: targetAppURL)
    }

    func makeArchive(version: String) throws -> URL {
        let sourceRootURL = rootURL.appendingPathComponent("ArchiveSource-\(version)", isDirectory: true)
        let appURL = sourceRootURL.appendingPathComponent("Pop.app", isDirectory: true)
        let archiveURL = rootURL.appendingPathComponent("Pop-macos-arm64-\(version).zip")

        try FileManager.default.createDirectory(at: sourceRootURL, withIntermediateDirectories: true)
        try Self.createAppBundle(at: appURL, executableContents: "build-\(version)")
        try Self.runProcess("/usr/bin/ditto", arguments: ["-c", "-k", "--keepParent", appURL.path, archiveURL.path])
        return archiveURL
    }

    static func createAppBundle(at appURL: URL, executableContents: String) throws {
        let contentsURL = appURL.appendingPathComponent("Contents", isDirectory: true)
        let macOSURL = contentsURL.appendingPathComponent("MacOS", isDirectory: true)
        let executableURL = macOSURL.appendingPathComponent("PopMacApp")
        let infoPlistURL = contentsURL.appendingPathComponent("Info.plist")
        let plist: [String: Any] = [
            "CFBundleIdentifier": "com.example.Pop",
            "CFBundleName": "Pop",
            "CFBundlePackageType": "APPL",
            "CFBundleShortVersionString": "1.0.0",
            "CFBundleVersion": "1"
        ]

        try FileManager.default.createDirectory(at: macOSURL, withIntermediateDirectories: true)
        let plistData = try PropertyListSerialization.data(fromPropertyList: plist, format: .xml, options: 0)
        try plistData.write(to: infoPlistURL)
        try executableContents.write(to: executableURL, atomically: true, encoding: .utf8)
    }

    static func runProcess(_ executablePath: String, arguments: [String]) throws {
        let process = Process()
        let outputPipe = Pipe()
        process.executableURL = URL(fileURLWithPath: executablePath)
        process.arguments = arguments
        process.standardOutput = outputPipe
        process.standardError = outputPipe
        try process.run()
        process.waitUntilExit()

        if process.terminationStatus != 0 {
            let output = String(data: outputPipe.fileHandleForReading.readDataToEndOfFile(), encoding: .utf8) ?? ""
            Issue.record("Process failed: \(executablePath) \(arguments.joined(separator: " "))\n\(output)")
        }
        #expect(process.terminationStatus == 0)
    }
}

import Foundation

public final class LaunchAgentManager: @unchecked Sendable {
    public let identifier: String
    public let plistURL: URL
    private let fileManager: FileManager
    private let executablePathProvider: () -> String?

    public init(
        identifier: String = "com.pop.app",
        launchAgentsDirectoryURL: URL? = nil,
        fileManager: FileManager = .default,
        executablePathProvider: @escaping () -> String? = {
            Bundle.main.executableURL?.path ?? ProcessInfo.processInfo.arguments.first
        }
    ) {
        self.identifier = identifier
        self.fileManager = fileManager
        self.executablePathProvider = executablePathProvider
        let directory = launchAgentsDirectoryURL ?? fileManager.homeDirectoryForCurrentUser.appendingPathComponent("Library/LaunchAgents", isDirectory: true)
        self.plistURL = directory.appendingPathComponent("\(identifier).plist")
    }

    public func isEnabled() -> Bool {
        fileManager.fileExists(atPath: plistURL.path)
    }

    public func setEnabled(_ enabled: Bool) throws {
        if !enabled {
            if fileManager.fileExists(atPath: plistURL.path) {
                try fileManager.removeItem(at: plistURL)
            }

            return
        }

        guard let executablePath = executablePathProvider(), !executablePath.isEmpty else {
            throw LaunchAgentError.missingExecutablePath
        }

        try fileManager.createDirectory(at: plistURL.deletingLastPathComponent(), withIntermediateDirectories: true)
        let data = try PropertyListSerialization.data(fromPropertyList: launchAgentContents(executablePath: executablePath), format: .xml, options: 0)
        try data.write(to: plistURL, options: .atomic)
    }

    private func launchAgentContents(executablePath: String) -> [String: Any] {
        [
            "Label": identifier,
            "ProgramArguments": [executablePath],
            "RunAtLoad": true,
            "KeepAlive": false
        ]
    }
}

public enum LaunchAgentError: Error {
    case missingExecutablePath
}

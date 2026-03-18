import Foundation

public final class AppSettingsStore: @unchecked Sendable {
    public let directoryURL: URL
    public let fileURL: URL
    private let fileManager: FileManager
    private let encoder: JSONEncoder
    private let decoder: JSONDecoder

    public init(directoryURL: URL? = nil, fileManager: FileManager = .default) {
        let baseDirectory = directoryURL ?? fileManager.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!.appendingPathComponent("Pop", isDirectory: true)
        self.directoryURL = baseDirectory
        self.fileURL = baseDirectory.appendingPathComponent("settings.json")
        self.fileManager = fileManager

        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        self.encoder = encoder
        self.decoder = JSONDecoder()
    }

    public func load() throws -> AppSettings {
        guard fileManager.fileExists(atPath: fileURL.path) else {
            return .default
        }

        let data = try Data(contentsOf: fileURL)
        return try decoder.decode(AppSettings.self, from: data)
    }

    public func save(_ settings: AppSettings) throws {
        try fileManager.createDirectory(at: directoryURL, withIntermediateDirectories: true)
        let data = try encoder.encode(settings)
        try data.write(to: fileURL, options: .atomic)
    }
}

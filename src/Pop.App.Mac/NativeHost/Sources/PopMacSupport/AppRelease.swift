import Foundation

public struct AppRelease: Equatable, Sendable {
    public let version: String
    public let assetName: String
    public let assetURL: URL

    public init(version: String, assetName: String, assetURL: URL) {
        self.version = version
        self.assetName = assetName
        self.assetURL = assetURL
    }
}

public struct AppVersion: Equatable, Comparable, Sendable {
    public let rawValue: String

    private let components: [Int]

    public init?(_ rawValue: String) {
        let trimmed = rawValue.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else {
            return nil
        }

        let withoutPrefix = trimmed.hasPrefix("v") || trimmed.hasPrefix("V")
            ? String(trimmed.dropFirst())
            : trimmed
        let versionCore = withoutPrefix.split(separator: "+", maxSplits: 1, omittingEmptySubsequences: true).first.map(String.init) ?? withoutPrefix
        let numericCore = versionCore.split(separator: "-", maxSplits: 1, omittingEmptySubsequences: true).first.map(String.init) ?? versionCore
        let parsedComponents = numericCore
            .split(separator: ".", omittingEmptySubsequences: false)
            .compactMap { Int($0) }

        guard !parsedComponents.isEmpty, parsedComponents.count == numericCore.split(separator: ".", omittingEmptySubsequences: false).count else {
            return nil
        }

        self.rawValue = numericCore
        self.components = parsedComponents
    }

    public static func < (lhs: AppVersion, rhs: AppVersion) -> Bool {
        let componentCount = max(lhs.components.count, rhs.components.count)
        for index in 0..<componentCount {
            let left = index < lhs.components.count ? lhs.components[index] : 0
            let right = index < rhs.components.count ? rhs.components[index] : 0
            if left != right {
                return left < right
            }
        }

        return false
    }

    public static func == (lhs: AppVersion, rhs: AppVersion) -> Bool {
        !(lhs < rhs) && !(rhs < lhs)
    }
}

public enum AppReleaseFeedError: Error, Equatable {
    case invalidPayload
    case missingMacAsset
    case invalidVersion(String)
}

public enum AppReleaseFeedParser {
    public static func decodeLatestMacRelease(
        from data: Data,
        assetPrefix: String = "Pop-macos-arm64-",
        assetSuffix: String = ".zip"
    ) throws -> AppRelease {
        let decoder = JSONDecoder()
        let release = try decoder.decode(GitHubReleasePayload.self, from: data)
        let version = release.tagName.hasPrefix("v") || release.tagName.hasPrefix("V")
            ? String(release.tagName.dropFirst())
            : release.tagName

        guard AppVersion(version) != nil else {
            throw AppReleaseFeedError.invalidVersion(release.tagName)
        }

        let expectedAssetName = "\(assetPrefix)\(version)\(assetSuffix)"
        guard let asset = release.assets.first(where: { $0.name == expectedAssetName }) else {
            throw AppReleaseFeedError.missingMacAsset
        }

        return AppRelease(version: version, assetName: asset.name, assetURL: asset.browserDownloadURL)
    }
}

private struct GitHubReleasePayload: Decodable {
    let tagName: String
    let assets: [GitHubReleaseAssetPayload]

    enum CodingKeys: String, CodingKey {
        case tagName = "tag_name"
        case assets
    }
}

private struct GitHubReleaseAssetPayload: Decodable {
    let name: String
    let browserDownloadURL: URL

    enum CodingKeys: String, CodingKey {
        case name
        case browserDownloadURL = "browser_download_url"
    }
}

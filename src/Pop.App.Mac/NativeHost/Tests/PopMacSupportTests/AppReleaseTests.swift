import Foundation
import Testing
@testable import PopMacSupport

struct AppReleaseTests {
    @Test
    func versionComparisonUsesNumericOrdering() {
        #expect(AppVersion("1.10.0")! > AppVersion("1.2.0")!)
        #expect(AppVersion("1.2.0")! == AppVersion("1.2")!)
        #expect(AppVersion("v2.0.1")! > AppVersion("2.0.0")!)
    }

    @Test
    func decodeLatestMacReleaseSelectsMatchingZipAsset() throws {
        let data = Data(
            """
            {
              "tag_name": "v1.4.2",
              "assets": [
                {
                  "name": "Pop-macos-arm64-1.4.2.dmg",
                  "browser_download_url": "https://example.com/Pop-macos-arm64-1.4.2.dmg"
                },
                {
                  "name": "Pop-macos-arm64-1.4.2.zip",
                  "browser_download_url": "https://example.com/Pop-macos-arm64-1.4.2.zip"
                }
              ]
            }
            """.utf8)

        let release = try AppReleaseFeedParser.decodeLatestMacRelease(from: data)

        #expect(release.version == "1.4.2")
        #expect(release.assetName == "Pop-macos-arm64-1.4.2.zip")
        #expect(release.assetURL.absoluteString == "https://example.com/Pop-macos-arm64-1.4.2.zip")
    }

    @Test
    func decodeLatestMacReleaseThrowsWhenZipAssetMissing() {
        let data = Data(
            """
            {
              "tag_name": "1.4.2",
              "assets": [
                {
                  "name": "Pop-macos-arm64-1.4.2.dmg",
                  "browser_download_url": "https://example.com/Pop-macos-arm64-1.4.2.dmg"
                }
              ]
            }
            """.utf8)

        #expect(throws: AppReleaseFeedError.missingMacAsset) {
            try AppReleaseFeedParser.decodeLatestMacRelease(from: data)
        }
    }
}

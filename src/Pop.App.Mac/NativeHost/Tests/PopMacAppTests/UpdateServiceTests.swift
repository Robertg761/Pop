import Foundation
import Testing
@testable import PopMacApp

@MainActor
struct UpdateServiceTests {
    @Test
    func initUsesUnsupportedStateForNonAppInstallLocations() throws {
        let fixture = try InstallerTestFixture(targetIsAppBundle: false)
        let service = UpdateService(
            installer: fixture.makeInstaller(),
            currentVersionProvider: { "1.0.0" })

        #expect(service.currentState.status == UpdateStatus.unsupported)
        #expect(!service.currentState.canCheck)
        #expect(service.currentState.message.contains("writable Applications folder"))
    }

    @Test
    func initUsesReadyToInstallStateWhenPreparedUpdateExists() throws {
        let fixture = try InstallerTestFixture()
        let installer = fixture.makeInstaller()
        let archiveURL = try fixture.makeArchive(version: "2.0.0")
        _ = try installer.prepareUpdate(fromArchiveAt: archiveURL, version: "2.0.0")

        let service = UpdateService(
            installer: installer,
            currentVersionProvider: { "1.0.0" })

        #expect(service.currentState.status == UpdateStatus.readyToInstall)
        #expect(service.currentState.availableVersion == "2.0.0")
        #expect(service.currentState.canInstall)
    }

    @Test
    func checkNowPublishesErrorWhenInstalledVersionIsInvalid() async throws {
        let fixture = try InstallerTestFixture()
        let session = try URLSession.withStubbedResponses([
            URL(string: "https://api.github.com/repos/Robertg761/Pop/releases/latest")!: .success(
                """
                {
                  "tag_name": "v1.2.0",
                  "assets": [
                    {
                      "name": "Pop-macos-arm64-1.2.0.zip",
                      "browser_download_url": "https://example.com/Pop-macos-arm64-1.2.0.zip"
                    }
                  ]
                }
                """)
        ])
        let releaseClient = GitHubReleaseClient(session: session)
        let service = UpdateService(
            releaseClient: releaseClient,
            installer: fixture.makeInstaller(),
            currentVersionProvider: { "not-a-version" })

        service.checkNow()
        try await waitForState(on: service) { $0.status == UpdateStatus.error }

        #expect(service.currentState.message.contains("couldn't compare app versions"))
        service.stop()
    }
}

private extension URLSession {
    static func withStubbedResponses(_ responses: [URL: StubURLProtocol.Response]) throws -> URLSession {
        StubURLProtocol.responses = responses
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [StubURLProtocol.self]
        return URLSession(configuration: configuration)
    }
}

private final class StubURLProtocol: URLProtocol {
    enum Response {
        case success(String)
        case failure(Error)
    }

    nonisolated(unsafe) static var responses: [URL: Response] = [:]

    override class func canInit(with request: URLRequest) -> Bool {
        true
    }

    override class func canonicalRequest(for request: URLRequest) -> URLRequest {
        request
    }

    override func startLoading() {
        guard let url = request.url, let response = Self.responses[url] else {
            client?.urlProtocol(self, didFailWithError: URLError(.badURL))
            return
        }

        switch response {
        case let .success(body):
            let httpResponse = HTTPURLResponse(url: url, statusCode: 200, httpVersion: nil, headerFields: nil)!
            client?.urlProtocol(self, didReceive: httpResponse, cacheStoragePolicy: .notAllowed)
            client?.urlProtocol(self, didLoad: Data(body.utf8))
            client?.urlProtocolDidFinishLoading(self)
        case let .failure(error):
            client?.urlProtocol(self, didFailWithError: error)
        }
    }

    override func stopLoading() {}
}

@MainActor
private func waitForState(
    on service: UpdateService,
    timeoutNanoseconds: UInt64 = 2_000_000_000,
    predicate: @escaping (UpdateState) -> Bool
) async throws {
    let deadline = ContinuousClock.now + .nanoseconds(Int64(timeoutNanoseconds))
    while ContinuousClock.now < deadline {
        if predicate(service.currentState) {
            return
        }

        try await Task.sleep(nanoseconds: 50_000_000)
    }

    Issue.record("Timed out waiting for update state. Last state: \(service.currentState)")
}

import AppKit
import Foundation
import PopMacSupport

enum UpdateStatus: Equatable {
    case idle
    case checking
    case downloading
    case readyToInstall
    case upToDate
    case error
    case unsupported
}

struct UpdateState: Equatable {
    let status: UpdateStatus
    let currentVersion: String
    let message: String
    let availableVersion: String?
    let downloadProgressPercent: Int?
    let canCheck: Bool
    let canInstall: Bool
}

@MainActor
final class UpdateService: NSObject {
    private static let initialDelay: TimeInterval = 5
    private static let checkInterval: TimeInterval = 6 * 60 * 60

    var onStateChanged: ((UpdateState) -> Void)?
    var onReadyToInstall: ((String?) -> Void)?

    private let releaseClient: GitHubReleaseClient
    private let installer: PreparedUpdateInstaller
    private let currentVersionProvider: () -> String

    private var initialTimer: Timer?
    private var repeatingTimer: Timer?
    private var activeTask: Task<Void, Never>?
    private var downloadContinuation: CheckedContinuation<URL, Error>?
    private var activeDownloadTask: URLSessionDownloadTask?
    private var activeDownloadVersion: String?
    private var session: URLSession?
    private var preparedUpdate: PreparedUpdateMetadata?
    private var hasStarted = false

    private(set) var currentState: UpdateState

    init(
        releaseClient: GitHubReleaseClient = GitHubReleaseClient(),
        currentVersionProvider: @escaping () -> String = {
            (Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String) ?? "0.0.0"
        }
    ) {
        self.releaseClient = releaseClient
        self.installer = PreparedUpdateInstaller()
        self.currentVersionProvider = currentVersionProvider
        self.currentState = UpdateService.createUnsupportedState(currentVersion: currentVersionProvider(), message: self.installer.unsupportedReason)
        super.init()
        self.session = URLSession(configuration: .default, delegate: self, delegateQueue: nil)
        self.currentState = createInitialState()
    }

    func start() {
        guard !hasStarted else {
            return
        }

        hasStarted = true
        publishState(createInitialState())

        guard installer.isSupportedInstallation else {
            return
        }

        initialTimer = Timer.scheduledTimer(withTimeInterval: Self.initialDelay, repeats: false) { [weak self] _ in
            Task { @MainActor in
                self?.checkNow()
            }
        }
        repeatingTimer = Timer.scheduledTimer(withTimeInterval: Self.checkInterval, repeats: true) { [weak self] _ in
            Task { @MainActor in
                self?.checkNow()
            }
        }
    }

    func stop() {
        initialTimer?.invalidate()
        repeatingTimer?.invalidate()
        initialTimer = nil
        repeatingTimer = nil
        activeTask?.cancel()
        activeTask = nil
        activeDownloadTask?.cancel()
        activeDownloadTask = nil
        if let continuation = downloadContinuation {
            downloadContinuation = nil
            continuation.resume(throwing: CancellationError())
        }
        session?.invalidateAndCancel()
        session = nil
    }

    func checkNow() {
        guard activeTask == nil else {
            return
        }

        activeTask = Task { [weak self] in
            guard let self else {
                return
            }

            await self.runCheck()
            self.activeTask = nil
        }
    }

    func applyPendingUpdateAndRestart() throws -> Bool {
        guard let preparedUpdate, installer.isSupportedInstallation else {
            publishState(createInitialState())
            return false
        }

        try installer.installPreparedUpdate(preparedUpdate)
        return true
    }

    private func runCheck() async {
        if !installer.isSupportedInstallation {
            publishState(Self.createUnsupportedState(currentVersion: currentVersion, message: installer.unsupportedReason))
            return
        }

        do {
            preparedUpdate = try installer.loadPreparedUpdate()
            try installer.removeObsoletePreparedUpdate(ifCurrentVersion: currentVersion)
            preparedUpdate = try installer.loadPreparedUpdate()
        } catch {
            publishState(UpdateState(
                status: .error,
                currentVersion: currentVersion,
                message: "Update check failed: \(error.localizedDescription)",
                availableVersion: nil,
                downloadProgressPercent: nil,
                canCheck: true,
                canInstall: false))
            return
        }

        if let preparedUpdate {
            publishState(createReadyState(version: preparedUpdate.version))
            return
        }

        publishState(UpdateState(
            status: .checking,
            currentVersion: currentVersion,
            message: "Checking for updates...",
            availableVersion: nil,
            downloadProgressPercent: nil,
            canCheck: false,
            canInstall: false))

        do {
            let release = try await releaseClient.fetchLatestRelease()
            guard let latestVersion = AppVersion(release.version), let installedVersion = AppVersion(currentVersion) else {
                publishState(UpdateState(
                    status: .error,
                    currentVersion: currentVersion,
                    message: "Update check failed: couldn't compare app versions.",
                    availableVersion: nil,
                    downloadProgressPercent: nil,
                    canCheck: true,
                    canInstall: false))
                return
            }

            guard latestVersion > installedVersion else {
                publishState(UpdateState(
                    status: .upToDate,
                    currentVersion: currentVersion,
                    message: "Pop is up to date.",
                    availableVersion: nil,
                    downloadProgressPercent: nil,
                    canCheck: true,
                    canInstall: false))
                return
            }

            let archiveURL = try await downloadRelease(release)
            let preparedUpdate = try installer.prepareUpdate(fromArchiveAt: archiveURL, version: release.version)
            self.preparedUpdate = preparedUpdate
            let readyState = createReadyState(version: release.version)
            publishState(readyState)
            onReadyToInstall?(release.version)
        } catch is CancellationError {
        } catch {
            publishState(UpdateState(
                status: .error,
                currentVersion: currentVersion,
                message: "Update check failed: \(error.localizedDescription)",
                availableVersion: nil,
                downloadProgressPercent: nil,
                canCheck: true,
                canInstall: false))
        }
    }

    private func downloadRelease(_ release: AppRelease) async throws -> URL {
        guard let session else {
            throw UpdateInstallerError.sessionUnavailable
        }

        let request = releaseClient.makeDownloadRequest(for: release.assetURL)
        activeDownloadVersion = release.version
        publishState(UpdateState(
            status: .downloading,
            currentVersion: currentVersion,
            message: "Downloading v\(release.version)... 0%",
            availableVersion: release.version,
            downloadProgressPercent: 0,
            canCheck: false,
            canInstall: false))

        return try await withCheckedThrowingContinuation { continuation in
            downloadContinuation = continuation
            let task = session.downloadTask(with: request)
            activeDownloadTask = task
            task.resume()
        }
    }

    private func createInitialState() -> UpdateState {
        do {
            try installer.removeObsoletePreparedUpdate(ifCurrentVersion: currentVersion)
            preparedUpdate = try installer.loadPreparedUpdate()
        } catch {
            return UpdateState(
                status: .error,
                currentVersion: currentVersion,
                message: "Update check failed: \(error.localizedDescription)",
                availableVersion: nil,
                downloadProgressPercent: nil,
                canCheck: true,
                canInstall: false)
        }

        guard installer.isSupportedInstallation else {
            return Self.createUnsupportedState(currentVersion: currentVersion, message: installer.unsupportedReason)
        }

        if let preparedUpdate {
            return createReadyState(version: preparedUpdate.version)
        }

        return UpdateState(
            status: .idle,
            currentVersion: currentVersion,
            message: "Ready to check for updates.",
            availableVersion: nil,
            downloadProgressPercent: nil,
            canCheck: true,
            canInstall: false)
    }

    private func createReadyState(version: String?) -> UpdateState {
        UpdateState(
            status: .readyToInstall,
            currentVersion: currentVersion,
            message: version.map { "Update v\($0) is ready to install." } ?? "An update is ready to install.",
            availableVersion: version,
            downloadProgressPercent: nil,
            canCheck: true,
            canInstall: true)
    }

    private static func createUnsupportedState(currentVersion: String, message: String) -> UpdateState {
        UpdateState(
            status: .unsupported,
            currentVersion: currentVersion,
            message: message,
            availableVersion: nil,
            downloadProgressPercent: nil,
            canCheck: false,
            canInstall: false)
    }

    private var currentVersion: String {
        currentVersionProvider()
    }

    private func publishState(_ state: UpdateState) {
        guard currentState != state else {
            return
        }

        currentState = state
        onStateChanged?(state)
    }
}

extension UpdateService: URLSessionDownloadDelegate {
    nonisolated func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didWriteData bytesWritten: Int64, totalBytesWritten: Int64, totalBytesExpectedToWrite: Int64) {
        Task { @MainActor [weak self] in
            guard let self, self.activeDownloadTask?.taskIdentifier == downloadTask.taskIdentifier else {
                return
            }

            guard totalBytesExpectedToWrite > 0 else {
                return
            }

            let progress = Int((Double(totalBytesWritten) / Double(totalBytesExpectedToWrite) * 100).rounded())
            let clampedProgress = min(max(progress, 0), 100)
            let version = self.activeDownloadVersion
            self.publishState(UpdateState(
                status: .downloading,
                currentVersion: self.currentVersion,
                message: version.map { "Downloading v\($0)... \(clampedProgress)%" } ?? "Downloading update... \(clampedProgress)%",
                availableVersion: version,
                downloadProgressPercent: clampedProgress,
                canCheck: false,
                canInstall: false))
        }
    }

    nonisolated func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didFinishDownloadingTo location: URL) {
        Task { @MainActor [weak self] in
            guard let self, self.activeDownloadTask?.taskIdentifier == downloadTask.taskIdentifier else {
                return
            }

            self.activeDownloadTask = nil
            self.activeDownloadVersion = nil
            guard let continuation = self.downloadContinuation else {
                return
            }

            self.downloadContinuation = nil
            continuation.resume(returning: location)
        }
    }

    nonisolated func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        Task { @MainActor [weak self] in
            guard let self, self.activeDownloadTask?.taskIdentifier == task.taskIdentifier || error != nil else {
                return
            }

            if let error, let continuation = self.downloadContinuation {
                self.activeDownloadTask = nil
                self.activeDownloadVersion = nil
                self.downloadContinuation = nil
                continuation.resume(throwing: error)
            }
        }
    }
}

struct GitHubReleaseClient {
    private let session: URLSession
    private let owner: String
    private let repository: String

    init(
        session: URLSession = .shared,
        owner: String = "Robertg761",
        repository: String = "Pop"
    ) {
        self.session = session
        self.owner = owner
        self.repository = repository
    }

    func fetchLatestRelease() async throws -> AppRelease {
        let request = makeAPIRequest(path: "/repos/\(owner)/\(repository)/releases/latest")
        let (data, response) = try await session.data(for: request)
        try validate(response: response)
        return try AppReleaseFeedParser.decodeLatestMacRelease(from: data)
    }

    func makeDownloadRequest(for url: URL) -> URLRequest {
        var request = URLRequest(url: url)
        request.setValue("application/octet-stream", forHTTPHeaderField: "Accept")
        request.setValue("PopMacApp", forHTTPHeaderField: "User-Agent")
        return request
    }

    private func makeAPIRequest(path: String) -> URLRequest {
        var request = URLRequest(url: URL(string: "https://api.github.com\(path)")!)
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        request.setValue("PopMacApp", forHTTPHeaderField: "User-Agent")
        return request
    }

    private func validate(response: URLResponse) throws {
        guard let response = response as? HTTPURLResponse else {
            throw UpdateInstallerError.invalidResponse
        }

        guard (200...299).contains(response.statusCode) else {
            throw UpdateInstallerError.httpError(statusCode: response.statusCode)
        }
    }
}

struct PreparedUpdateMetadata: Codable {
    let version: String
    let archiveURL: URL
    let stagedAppURL: URL
    let workingDirectoryURL: URL
}

enum UpdateInstallerError: LocalizedError {
    case unsupportedInstallation
    case invalidResponse
    case httpError(statusCode: Int)
    case missingPreparedApp
    case backupFailed
    case sessionUnavailable
    case processFailed(command: String, output: String)

    var errorDescription: String? {
        switch self {
        case .unsupportedInstallation:
            return "Install Pop from the packaged macOS release in /Applications to enable in-app updates."
        case .invalidResponse:
            return "The update server returned an invalid response."
        case let .httpError(statusCode):
            return "The update server returned HTTP \(statusCode)."
        case .missingPreparedApp:
            return "The downloaded update could not be prepared."
        case .backupFailed:
            return "Pop couldn't restore the previous app after the update copy failed."
        case .sessionUnavailable:
            return "The download session is unavailable."
        case let .processFailed(command, output):
            return output.isEmpty ? "\(command) failed." : "\(command) failed: \(output)"
        }
    }
}

final class PreparedUpdateInstaller {
    private let fileManager: FileManager
    private let baseDirectoryURL: URL
    private let targetAppURL: URL

    init(
        fileManager: FileManager = .default,
        baseDirectoryURL: URL? = nil,
        targetAppURL: URL = Bundle.main.bundleURL
    ) {
        self.fileManager = fileManager
        self.baseDirectoryURL = baseDirectoryURL
            ?? fileManager.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!.appendingPathComponent("Pop", isDirectory: true).appendingPathComponent("Updates", isDirectory: true)
        self.targetAppURL = targetAppURL
    }

    var isSupportedInstallation: Bool {
        targetAppURL.pathExtension == "app" && fileManager.isWritableFile(atPath: targetAppURL.deletingLastPathComponent().path)
    }

    var unsupportedReason: String {
        let parentDirectory = targetAppURL.deletingLastPathComponent().path
        return "Install Pop from the packaged macOS release in a writable Applications folder such as ~/Applications to enable in-app updates. Current install location: \(parentDirectory)"
    }

    func loadPreparedUpdate() throws -> PreparedUpdateMetadata? {
        guard fileManager.fileExists(atPath: metadataURL.path) else {
            return nil
        }

        let data = try Data(contentsOf: metadataURL)
        let preparedUpdate = try JSONDecoder().decode(PreparedUpdateMetadata.self, from: data)
        guard fileManager.fileExists(atPath: preparedUpdate.stagedAppURL.path) else {
            try? fileManager.removeItem(at: preparedUpdate.workingDirectoryURL)
            try? fileManager.removeItem(at: metadataURL)
            return nil
        }

        return preparedUpdate
    }

    func removeObsoletePreparedUpdate(ifCurrentVersion currentVersion: String) throws {
        guard let preparedUpdate = try loadPreparedUpdate() else {
            return
        }

        guard let preparedVersion = AppVersion(preparedUpdate.version), let installedVersion = AppVersion(currentVersion) else {
            try clearPreparedUpdate()
            return
        }

        if preparedVersion <= installedVersion {
            try clearPreparedUpdate()
        }
    }

    func prepareUpdate(fromArchiveAt archiveURL: URL, version: String) throws -> PreparedUpdateMetadata {
        try clearPreparedUpdate()
        let workingDirectoryURL = baseDirectoryURL.appendingPathComponent(version, isDirectory: true)
        let archiveDestinationURL = workingDirectoryURL.appendingPathComponent("Pop-macos-arm64-\(version).zip")
        let extractDirectoryURL = workingDirectoryURL.appendingPathComponent("Extracted", isDirectory: true)
        let stagedAppURL = extractDirectoryURL.appendingPathComponent("Pop.app", isDirectory: true)

        try fileManager.createDirectory(at: extractDirectoryURL, withIntermediateDirectories: true)
        try fileManager.moveItem(at: archiveURL, to: archiveDestinationURL)
        try runProcess("/usr/bin/ditto", arguments: ["-x", "-k", archiveDestinationURL.path, extractDirectoryURL.path])

        guard fileManager.fileExists(atPath: stagedAppURL.path) else {
            throw UpdateInstallerError.missingPreparedApp
        }

        let preparedUpdate = PreparedUpdateMetadata(
            version: version,
            archiveURL: archiveDestinationURL,
            stagedAppURL: stagedAppURL,
            workingDirectoryURL: workingDirectoryURL)
        let data = try JSONEncoder().encode(preparedUpdate)
        try fileManager.createDirectory(at: baseDirectoryURL, withIntermediateDirectories: true)
        try data.write(to: metadataURL, options: .atomic)
        return preparedUpdate
    }

    func installPreparedUpdate(_ preparedUpdate: PreparedUpdateMetadata) throws {
        guard fileManager.fileExists(atPath: preparedUpdate.stagedAppURL.path) else {
            throw UpdateInstallerError.missingPreparedApp
        }

        try fileManager.createDirectory(at: baseDirectoryURL, withIntermediateDirectories: true)
        try installerScript.write(to: installerScriptURL, atomically: true, encoding: .utf8)
        try runProcess("/bin/chmod", arguments: ["755", installerScriptURL.path])

        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/bin/zsh")
        process.arguments = [
            installerScriptURL.path,
            String(ProcessInfo.processInfo.processIdentifier),
            targetAppURL.path,
            preparedUpdate.stagedAppURL.path,
            preparedUpdate.workingDirectoryURL.path,
            metadataURL.path
        ]
        try process.run()
    }

    private func clearPreparedUpdate() throws {
        if fileManager.fileExists(atPath: metadataURL.path),
           let data = try? Data(contentsOf: metadataURL),
           let preparedUpdate = try? JSONDecoder().decode(PreparedUpdateMetadata.self, from: data) {
            try? fileManager.removeItem(at: preparedUpdate.workingDirectoryURL)
        }

        if fileManager.fileExists(atPath: metadataURL.path) {
            try fileManager.removeItem(at: metadataURL)
        }
    }

    private func runProcess(_ executablePath: String, arguments: [String]) throws {
        let process = Process()
        let outputPipe = Pipe()
        process.executableURL = URL(fileURLWithPath: executablePath)
        process.arguments = arguments
        process.standardOutput = outputPipe
        process.standardError = outputPipe
        try process.run()
        process.waitUntilExit()

        guard process.terminationStatus == 0 else {
            let output = String(data: outputPipe.fileHandleForReading.readDataToEndOfFile(), encoding: .utf8) ?? ""
            throw UpdateInstallerError.processFailed(command: executablePath, output: output.trimmingCharacters(in: .whitespacesAndNewlines))
        }
    }

    private var metadataURL: URL {
        baseDirectoryURL.appendingPathComponent("prepared-update.json")
    }

    private var installerScriptURL: URL {
        baseDirectoryURL.appendingPathComponent("install-update.sh")
    }

    private var installerScript: String {
        """
        #!/bin/zsh
        set -euo pipefail

        APP_PID="$1"
        TARGET_APP="$2"
        STAGED_APP="$3"
        WORK_DIR="$4"
        METADATA_FILE="$5"
        BACKUP_APP="${TARGET_APP}.previous"

        while kill -0 "$APP_PID" 2>/dev/null; do
          sleep 0.2
        done

        rm -rf "$BACKUP_APP"
        if [[ -e "$TARGET_APP" ]]; then
          /bin/mv "$TARGET_APP" "$BACKUP_APP"
        fi

        if /usr/bin/ditto "$STAGED_APP" "$TARGET_APP"; then
          rm -rf "$BACKUP_APP"
        else
          rm -rf "$TARGET_APP"
          if [[ -e "$BACKUP_APP" ]]; then
            /bin/mv "$BACKUP_APP" "$TARGET_APP"
          else
            exit 1
          fi
          exit 1
        fi

        rm -rf "$WORK_DIR"
        rm -f "$METADATA_FILE"
        open "$TARGET_APP"
        """
    }
}

import CPopMacBridge
import Foundation
import PopMacSupport

struct BridgeAnimationFrame {
    var offsetMilliseconds: Int
    var bounds: DesktopRect
}

struct BridgeAnimationPlan {
    var frames: [BridgeAnimationFrame]
    var finalBounds: DesktopRect
    var durationMs: Int
    var maxOvershootPx: Int
}

struct BridgeSnapDecision {
    var target: Int
    var targetMonitor: DesktopScreen
    var projectedLandingPoint: DesktopPoint
    var horizontalVelocityPxPerSec: Double
    var verticalVelocityPxPerSec: Double
    var horizontalDominanceRatio: Double
    var isQualified: Bool
    var rejectionReason: Int
}

struct DragSample {
    var point: DesktopPoint
    var timestampUnixMilliseconds: Int64
}

struct DragDecisionContext {
    var initialMonitor: DesktopScreen
    var currentMonitor: DesktopScreen
    var initialBounds: DesktopRect
    var currentBounds: DesktopRect
    var isOptionPressedAtRelease: Bool
}

final class PopMacBridgeClient {
    func evaluateDragGesture(samples: [DragSample], monitors: [DesktopScreen], context: DragDecisionContext, settings: AppSettings) -> BridgeSnapDecision {
        var nativeSamples = samples.map { $0.dto }
        var nativeMonitors = monitors.map { $0.dto }
        let nativeContext = context.dto
        let nativeSettings = settings.dto

        let decision = nativeSamples.withUnsafeMutableBufferPointer { sampleBuffer in
            nativeMonitors.withUnsafeMutableBufferPointer { monitorBuffer in
                PopMacBridge_EvaluateDragGesture(
                    sampleBuffer.baseAddress,
                    Int32(sampleBuffer.count),
                    monitorBuffer.baseAddress,
                    Int32(monitorBuffer.count),
                    nativeContext,
                    nativeSettings)
            }
        }

        return BridgeSnapDecision(dto: decision)
    }

    func tileBounds(target: Int, monitor: DesktopScreen) -> DesktopRect {
        DesktopRect(dto: PopMacBridge_GetTileBounds(Int32(target), monitor.dto))
    }

    func animationPlan(startBounds: DesktopRect, targetBounds: DesktopRect, releaseVelocityX: Double, durationMs: Int) -> BridgeAnimationPlan {
        let nativePlan = PopMacBridge_CreateAnimationPlan(startBounds.dto, targetBounds.dto, releaseVelocityX, Int32(durationMs))
        defer {
            PopMacBridge_FreeAnimationPlan(nativePlan)
        }

        let framesPointer = nativePlan.frames?.assumingMemoryBound(to: PopAnimationFrameDto.self)
        let frames = (0..<Int(nativePlan.frameCount)).compactMap { index -> BridgeAnimationFrame? in
            guard let framesPointer else {
                return nil
            }

            let frame = framesPointer[index]
            return BridgeAnimationFrame(offsetMilliseconds: Int(frame.offsetMilliseconds), bounds: DesktopRect(dto: frame.bounds))
        }

        return BridgeAnimationPlan(
            frames: frames,
            finalBounds: DesktopRect(dto: nativePlan.finalBounds),
            durationMs: Int(nativePlan.durationMs),
            maxOvershootPx: Int(nativePlan.maxOvershootPx))
    }

    func formatDiagnosticEvent(timestamp: Date, category: String, message: String, fields: [String: String?]) -> String {
        let ownedFields = fields.map { (key: strdup($0.key), value: $0.value.flatMap { strdup($0) }) }
        defer {
            ownedFields.forEach {
                free($0.key)
                if let value = $0.value {
                    free(value)
                }
            }
        }

        var nativeFields = ownedFields.map {
            PopDiagnosticFieldDto(
                key: UnsafePointer($0.key),
                value: $0.value.flatMap { UnsafePointer($0) })
        }
        return category.withCString { categoryCString in
            message.withCString { messageCString in
                let pointer = nativeFields.withUnsafeMutableBufferPointer { buffer in
                    PopMacBridge_FormatDiagnosticEvent(
                        Int64(timestamp.timeIntervalSince1970 * 1000),
                        categoryCString,
                        messageCString,
                        buffer.baseAddress,
                        Int32(buffer.count))
                }

                defer {
                    PopMacBridge_FreeUtf8String(pointer)
                }

                guard let pointer else {
                    return ""
                }

                return String(cString: pointer)
            }
        }
    }
}

private extension AppSettings {
    var dto: PopAppSettingsDto {
        PopAppSettingsDto(
            enabled: enabled ? 1 : 0,
            launchAtStartup: launchAtStartup ? 1 : 0,
            throwVelocityThresholdPxPerSec: throwVelocityThresholdPxPerSec,
            horizontalDominanceRatio: horizontalDominanceRatio,
            glideDurationMs: Int32(glideDurationMs),
            enableDiagnostics: enableDiagnostics ? 1 : 0)
    }
}

private extension DragSample {
    var dto: PopDragSampleDto {
        PopDragSampleDto(x: Int32(point.x), y: Int32(point.y), timestampUnixMilliseconds: timestampUnixMilliseconds)
    }
}

private extension DesktopRect {
    init(dto: PopRectDto) {
        self.init(x: Int(dto.x), y: Int(dto.y), width: Int(dto.width), height: Int(dto.height))
    }

    var dto: PopRectDto {
        PopRectDto(x: Int32(x), y: Int32(y), width: Int32(width), height: Int32(height))
    }
}

private extension DesktopScreen {
    init(dto: PopMonitorInfoDto) {
        self.init(frame: DesktopRect(dto: dto.bounds), visibleFrame: DesktopRect(dto: dto.workArea))
    }

    var dto: PopMonitorInfoDto {
        PopMonitorInfoDto(bounds: frame.dto, workArea: visibleFrame.dto)
    }
}

private extension DesktopPoint {
    init(dto: PopPointDto) {
        self.init(x: Int(dto.x), y: Int(dto.y))
    }
}

private extension DragDecisionContext {
    var dto: PopDragContextDto {
        PopDragContextDto(
            initialMonitor: initialMonitor.dto,
            currentMonitor: currentMonitor.dto,
            initialBounds: initialBounds.dto,
            currentBounds: currentBounds.dto,
            isOptionPressedAtRelease: isOptionPressedAtRelease ? 1 : 0)
    }
}

private extension BridgeSnapDecision {
    init(dto: PopSnapDecisionDto) {
        self.init(
            target: Int(dto.target),
            targetMonitor: DesktopScreen(dto: dto.targetMonitor),
            projectedLandingPoint: DesktopPoint(dto: dto.projectedLandingPoint),
            horizontalVelocityPxPerSec: dto.horizontalVelocityPxPerSec,
            verticalVelocityPxPerSec: dto.verticalVelocityPxPerSec,
            horizontalDominanceRatio: dto.horizontalDominanceRatio,
            isQualified: dto.isQualified != 0,
            rejectionReason: Int(dto.rejectionReason))
    }
}

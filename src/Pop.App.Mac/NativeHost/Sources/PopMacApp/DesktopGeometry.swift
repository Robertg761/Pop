import AppKit

struct DesktopPoint: Equatable {
    var x: Int
    var y: Int
}

struct DesktopRect: Equatable {
    var x: Int
    var y: Int
    var width: Int
    var height: Int

    var maxX: Int { x + width }
    var maxY: Int { y + height }
    var midpoint: DesktopPoint { DesktopPoint(x: x + (width / 2), y: y + (height / 2)) }

    func contains(_ point: DesktopPoint) -> Bool {
        point.x >= x && point.x < maxX && point.y >= y && point.y < maxY
    }

    func intersectionArea(with other: DesktopRect) -> Int {
        let overlapX = max(0, min(maxX, other.maxX) - max(x, other.x))
        let overlapY = max(0, min(maxY, other.maxY) - max(y, other.y))
        return overlapX * overlapY
    }
}

struct DesktopScreen {
    var frame: DesktopRect
    var visibleFrame: DesktopRect
}

final class ScreenCoordinator {
    private var desktopMaxY: CGFloat {
        NSScreen.screens.map { $0.frame.maxY }.max() ?? 0
    }

    func pointInTopLeftSpace(from eventLocation: CGPoint) -> DesktopPoint {
        DesktopPoint(
            x: Int(eventLocation.x.rounded()),
            y: Int((desktopMaxY - eventLocation.y).rounded()))
    }

    func rectInTopLeftSpace(from frame: CGRect) -> DesktopRect {
        DesktopRect(
            x: Int(frame.origin.x.rounded()),
            y: Int((desktopMaxY - frame.maxY).rounded()),
            width: Int(frame.width.rounded()),
            height: Int(frame.height.rounded()))
    }

    func cgPoint(fromTopLeftRect rect: DesktopRect) -> CGPoint {
        CGPoint(
            x: CGFloat(rect.x),
            y: desktopMaxY - CGFloat(rect.y + rect.height))
    }

    func screens() -> [DesktopScreen] {
        NSScreen.screens.map {
            DesktopScreen(
                frame: rectInTopLeftSpace(from: $0.frame),
                visibleFrame: rectInTopLeftSpace(from: $0.visibleFrame))
        }
    }

    func monitor(containing rect: DesktopRect) -> DesktopScreen? {
        let allScreens = screens()
        if let direct = allScreens.first(where: { $0.frame.contains(rect.midpoint) }) {
            return direct
        }

        return allScreens.max(by: { $0.frame.intersectionArea(with: rect) < $1.frame.intersectionArea(with: rect) })
    }
}

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
    // Pop.Core works in a single top-left-origin global space (origin at the primary display's
    // top-left, +Y downward). Two of macOS's coordinate sources already use that space and one
    // does not:
    //   - CGEvent.location and the Accessibility API (kAXPosition/kAXSize) are top-left global.
    //   - NSScreen.frame/.visibleFrame are Cocoa: bottom-left origin, +Y upward.
    // Only NSScreen frames are flipped, and the flip pivots on the primary screen's height —
    // NSScreen.screens[0] has a Cocoa origin of (0,0), so its height is the top of the space.
    private var primaryScreenHeight: CGFloat {
        NSScreen.screens.first?.frame.height ?? 0
    }

    /// A CGEvent location is already in top-left global space; round it into integer space.
    func pointInTopLeftSpace(from eventLocation: CGPoint) -> DesktopPoint {
        DesktopPoint(
            x: Int(eventLocation.x.rounded()),
            y: Int(eventLocation.y.rounded()))
    }

    /// An AX window rect (kAXPosition/kAXSize) is already top-left; round it, do not flip.
    func windowRect(fromAXRect rect: CGRect) -> DesktopRect {
        DesktopRect(
            x: Int(rect.origin.x.rounded()),
            y: Int(rect.origin.y.rounded()),
            width: Int(rect.width.rounded()),
            height: Int(rect.height.rounded()))
    }

    /// The top-left corner of a tile maps straight through to an AX position (also top-left).
    func axPoint(fromTopLeftRect rect: DesktopRect) -> CGPoint {
        CGPoint(x: CGFloat(rect.x), y: CGFloat(rect.y))
    }

    /// An NSScreen (Cocoa, bottom-left) frame flipped into top-left global space.
    private func screenRect(fromCocoaFrame frame: CGRect) -> DesktopRect {
        DesktopRect(
            x: Int(frame.origin.x.rounded()),
            y: Int((primaryScreenHeight - frame.maxY).rounded()),
            width: Int(frame.width.rounded()),
            height: Int(frame.height.rounded()))
    }

    func screens() -> [DesktopScreen] {
        NSScreen.screens.map {
            DesktopScreen(
                frame: screenRect(fromCocoaFrame: $0.frame),
                visibleFrame: screenRect(fromCocoaFrame: $0.visibleFrame))
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

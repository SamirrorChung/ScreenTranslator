import AppKit

struct CaptureRegion: Codable, Equatable, Sendable {
    var displayID: UInt32
    var x: Double
    var y: Double
    var width: Double
    var height: Double

    var summary: String {
        "已圈選 \(Int(width.rounded())) × \(Int(height.rounded()))"
    }

    var sourceRect: CGRect {
        CGRect(x: x, y: y, width: width, height: height)
    }

    /// `x`/`y` are points from the top-left of the display. AppKit frames grow upward.
    func appKitFrame(on screen: NSScreen) -> CGRect {
        let frame = screen.frame
        return CGRect(
            x: frame.minX + x,
            y: frame.maxY - y - height,
            width: width,
            height: height
        )
    }
}

enum ScreenIdentity {
    static func displayID(of screen: NSScreen) -> CGDirectDisplayID {
        if let number = screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber {
            return CGDirectDisplayID(number.uint32Value)
        }
        return CGMainDisplayID()
    }

    static func screen(for displayID: CGDirectDisplayID) -> NSScreen? {
        NSScreen.screens.first { self.displayID(of: $0) == displayID }
    }
}

import AppKit
import CoreVideo
import ScreenCaptureKit

struct GrabbedFrame: @unchecked Sendable {
    let image: CGImage
}

enum CaptureError: Error, CustomStringConvertible {
    case displayUnavailable
    case noImage

    var description: String {
        switch self {
        case .displayUnavailable:
            "找不到圈選的螢幕"
        case .noImage:
            "沒有截到畫面"
        }
    }
}

enum RegionGrabber {
    /// `region` is in points, origin at the top-left of its display. That matches
    /// ScreenCaptureKit's source rectangle. The overlay window is excluded so
    /// translated text is not read back in.
    static func grab(region: CaptureRegion, excludingWindowID: UInt32?) async throws -> GrabbedFrame {
        let scale: CGFloat = await MainActor.run {
            ScreenIdentity.screen(for: CGDirectDisplayID(region.displayID))?.backingScaleFactor ?? 0
        }
        guard scale > 0 else { throw CaptureError.displayUnavailable }
        let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: false)
        guard let display = content.displays.first(where: { $0.displayID == CGDirectDisplayID(region.displayID) }) else {
            throw CaptureError.displayUnavailable
        }
        let excluded = content.windows.filter { window in
            guard let excludingWindowID else { return false }
            return window.windowID == excludingWindowID
        }
        let filter = SCContentFilter(display: display, excludingWindows: excluded)
        let configuration = SCStreamConfiguration()
        configuration.sourceRect = region.sourceRect
        configuration.width = max(1, Int((region.width * scale).rounded()))
        configuration.height = max(1, Int((region.height * scale).rounded()))
        configuration.showsCursor = false
        configuration.scalesToFit = false
        configuration.pixelFormat = kCVPixelFormatType_32BGRA
        if #available(macOS 15.0, *) {
            configuration.captureDynamicRange = .SDR
        }
        let image = try await SCScreenshotManager.captureImage(contentFilter: filter, configuration: configuration)
        guard image.width > 0, image.height > 0 else { throw CaptureError.noImage }
        return GrabbedFrame(image: image)
    }
}

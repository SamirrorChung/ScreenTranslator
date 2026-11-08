import AppKit
import TranslatorCore

@MainActor
final class OverlayPanel {
    private let window: OverlayWindow
    private let canvas: OverlayCanvas

    var windowID: UInt32? {
        let number = window.windowNumber
        guard number > 0 else { return nil }
        return UInt32(number)
    }

    init(frame: CGRect) {
        canvas = OverlayCanvas(frame: CGRect(origin: .zero, size: frame.size))
        window = OverlayWindow(
            contentRect: frame,
            styleMask: .borderless,
            backing: .buffered,
            defer: false
        )
        window.contentView = canvas
        window.isOpaque = false
        window.backgroundColor = .clear
        window.hasShadow = false
        window.ignoresMouseEvents = true
        window.level = .screenSaver
        window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        window.sharingType = .none
        window.isReleasedWhenClosed = false
        window.hidesOnDeactivate = false
        window.animationBehavior = .none
        canvas.autoresizingMask = [.width, .height]
    }

    func orderFront() {
        window.orderFrontRegardless()
    }

    func setMasks(_ masks: [TextMask], pixelScale: Double) {
        canvas.pixelScale = CGFloat(pixelScale)
        canvas.masks = masks
    }

    func close() {
        canvas.masks = []
        window.orderOut(nil)
    }
}

private final class OverlayWindow: NSWindow {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

private final class OverlayCanvas: NSView {
    var masks: [TextMask] = [] {
        didSet { needsDisplay = true }
    }
    var pixelScale: CGFloat = 2

    override var isFlipped: Bool { true }

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        layer?.backgroundColor = NSColor.clear.cgColor
        layer?.isOpaque = false
    }

    required init?(coder: NSCoder) {
        nil
    }

    override func draw(_ dirtyRect: NSRect) {
        guard let context = NSGraphicsContext.current?.cgContext else { return }
        context.clear(bounds)
        guard pixelScale > 0 else { return }
        for mask in masks where !mask.text.isEmpty {
            draw(mask, in: context)
        }
    }

    private func draw(_ mask: TextMask, in context: CGContext) {
        var rect = CGRect(
            x: mask.x / pixelScale,
            y: mask.y / pixelScale,
            width: max(mask.width / pixelScale, 8),
            height: max(mask.height / pixelScale, 8)
        ).insetBy(dx: -6, dy: -4)
        rect.size.height = max(rect.height, 22)

        var fontSize = min(28, max(13, rect.height * 0.55))
        let paragraph = NSMutableParagraphStyle()
        paragraph.alignment = .center
        paragraph.lineBreakMode = .byTruncatingTail
        let shadow = NSShadow()
        shadow.shadowColor = NSColor.black.withAlphaComponent(0.85)
        shadow.shadowBlurRadius = 2
        shadow.shadowOffset = .zero

        func attributes(size: CGFloat) -> [NSAttributedString.Key: Any] {
            [
                .font: NSFont.systemFont(ofSize: size, weight: .semibold),
                .foregroundColor: NSColor.white,
                .paragraphStyle: paragraph,
                .shadow: shadow,
            ]
        }

        var attrs = attributes(size: fontSize)
        var measured = (mask.text as NSString).size(withAttributes: attrs)
        while measured.width > max(rect.width - 12, 8), fontSize > 11 {
            fontSize -= 1
            attrs = attributes(size: fontSize)
            measured = (mask.text as NSString).size(withAttributes: attrs)
        }
        let neededWidth = measured.width + 16
        if neededWidth > rect.width {
            let growth = neededWidth - rect.width
            rect.origin.x -= growth / 2
            rect.size.width = neededWidth
        }
        rect = clamp(rect, to: bounds)
        guard rect.width > 4, rect.height > 4 else { return }

        let path = CGPath(roundedRect: rect, cornerWidth: 5, cornerHeight: 5, transform: nil)
        context.setFillColor(NSColor.black.withAlphaComponent(mask.opacity).cgColor)
        context.addPath(path)
        context.fillPath()

        let textRect = CGRect(
            x: rect.minX + 6,
            y: rect.midY - measured.height / 2,
            width: rect.width - 12,
            height: measured.height
        )
        (mask.text as NSString).draw(in: textRect, withAttributes: attrs)
    }

    private func clamp(_ rect: CGRect, to bounds: CGRect) -> CGRect {
        var rect = rect
        let inset: CGFloat = 2
        let maxWidth = max(bounds.width - inset * 2, 1)
        let maxHeight = max(bounds.height - inset * 2, 1)
        if rect.width > maxWidth {
            rect.size.width = maxWidth
            rect.origin.x = inset
        } else if rect.minX < inset {
            rect.origin.x = inset
        } else if rect.maxX > bounds.width - inset {
            rect.origin.x = bounds.width - inset - rect.width
        }
        if rect.height > maxHeight {
            rect.size.height = maxHeight
            rect.origin.y = inset
        } else if rect.minY < inset {
            rect.origin.y = inset
        } else if rect.maxY > bounds.height - inset {
            rect.origin.y = bounds.height - inset - rect.height
        }
        return rect
    }
}

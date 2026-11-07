import AppKit

@MainActor
final class RegionSelector {
    private var windows: [SelectionWindow] = []
    private var keyMonitor: Any?
    private var onSelect: ((CaptureRegion) -> Void)?
    private var onCancel: (() -> Void)?

    func begin(onSelect: @escaping (CaptureRegion) -> Void, onCancel: @escaping () -> Void) {
        guard windows.isEmpty else { return }
        self.onSelect = onSelect
        self.onCancel = onCancel
        windows = NSScreen.screens.map { screen in
            let window = SelectionWindow(screen: screen)
            window.onCommit = { [weak self] region in
                self?.finish(with: region)
            }
            window.onCancel = { [weak self] in
                self?.cancel()
            }
            window.orderFrontRegardless()
            return window
        }
        keyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            if event.keyCode == 53 {
                self?.cancel()
                return nil
            }
            return event
        }
        windows.first?.makeKey()
        NSApp.activate()
    }

    private func finish(with region: CaptureRegion) {
        let callback = onSelect
        teardown()
        callback?(region)
    }

    private func cancel() {
        let callback = onCancel
        teardown()
        callback?()
    }

    private func teardown() {
        if let keyMonitor {
            NSEvent.removeMonitor(keyMonitor)
        }
        keyMonitor = nil
        onSelect = nil
        onCancel = nil
        for window in windows {
            window.orderOut(nil)
            window.close()
        }
        windows = []
    }
}

private final class SelectionWindow: NSWindow {
    var onCommit: ((CaptureRegion) -> Void)?
    var onCancel: (() -> Void)?
    private let screenDisplayID: CGDirectDisplayID

    init(screen: NSScreen) {
        screenDisplayID = ScreenIdentity.displayID(of: screen)
        super.init(
            contentRect: screen.frame,
            styleMask: .borderless,
            backing: .buffered,
            defer: false
        )
        let canvas = SelectionCanvas(frame: CGRect(origin: .zero, size: screen.frame.size))
        canvas.onCommit = { [weak self] rect in
            guard let self else { return }
            self.onCommit?(
                CaptureRegion(
                    displayID: self.screenDisplayID,
                    x: rect.minX,
                    y: rect.minY,
                    width: rect.width,
                    height: rect.height
                )
            )
        }
        canvas.onCancel = { [weak self] in
            self?.onCancel?()
        }
        contentView = canvas
        isOpaque = false
        backgroundColor = .clear
        hasShadow = false
        level = NSWindow.Level(rawValue: NSWindow.Level.screenSaver.rawValue + 1)
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        isReleasedWhenClosed = false
        animationBehavior = .none
        canvas.autoresizingMask = [.width, .height]
    }

    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { false }

    override func cancelOperation(_ sender: Any?) {
        onCancel?()
    }
}

private final class SelectionCanvas: NSView {
    var onCommit: ((CGRect) -> Void)?
    var onCancel: (() -> Void)?
    private var anchor: CGPoint?
    private var current: CGPoint?

    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { true }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        window?.makeFirstResponder(self)
    }

    override func resetCursorRects() {
        addCursorRect(bounds, cursor: .crosshair)
    }

    override func draw(_ dirtyRect: NSRect) {
        guard let context = NSGraphicsContext.current?.cgContext else { return }
        context.setFillColor(NSColor.black.withAlphaComponent(0.35).cgColor)
        if let rect = rubberBand, rect.width > 1, rect.height > 1 {
            context.addRect(bounds)
            context.addRect(rect)
            context.fillPath(using: .evenOdd)
            context.setStrokeColor(NSColor.white.cgColor)
            context.setLineWidth(2)
            context.stroke(rect)
        } else {
            context.fill(bounds)
        }

        let title = "拖曳圈選要翻譯的範圍　·　Esc 取消" as NSString
        let attributes: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: 16, weight: .medium),
            .foregroundColor: NSColor.white,
        ]
        let size = title.size(withAttributes: attributes)
        let titleRect = CGRect(
            x: (bounds.width - size.width) / 2,
            y: 28,
            width: size.width,
            height: size.height
        )
        title.draw(in: titleRect, withAttributes: attributes)
    }

    override func mouseDown(with event: NSEvent) {
        let point = convert(event.locationInWindow, from: nil)
        anchor = point
        current = point
        needsDisplay = true
    }

    override func mouseDragged(with event: NSEvent) {
        current = convert(event.locationInWindow, from: nil)
        needsDisplay = true
    }

    override func mouseUp(with event: NSEvent) {
        current = convert(event.locationInWindow, from: nil)
        guard let rect = rubberBand, rect.width >= 24, rect.height >= 24 else {
            anchor = nil
            current = nil
            needsDisplay = true
            return
        }
        onCommit?(rect)
    }

    override func keyDown(with event: NSEvent) {
        if event.keyCode == 53 {
            onCancel?()
            return
        }
        super.keyDown(with: event)
    }

    private var rubberBand: CGRect? {
        guard let anchor, let current else { return nil }
        return CGRect(
            x: min(anchor.x, current.x),
            y: min(anchor.y, current.y),
            width: abs(current.x - anchor.x),
            height: abs(current.y - anchor.y)
        )
    }
}

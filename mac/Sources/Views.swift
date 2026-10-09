import AppKit
import AVFoundation

/* View whose backing layer is the sample-buffer display layer itself */
final class VideoLayerView: NSView {
    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        layerContentsRedrawPolicy = .never
        displayLayer.videoGravity = .resizeAspect
        displayLayer.backgroundColor = NSColor.black.cgColor
    }

    required init?(coder: NSCoder) { fatalError("not supported") }

    override func makeBackingLayer() -> CALayer { AVSampleBufferDisplayLayer() }
    override var wantsUpdateLayer: Bool { true }
    override func updateLayer() {}

    var displayLayer: AVSampleBufferDisplayLayer { layer as! AVSampleBufferDisplayLayer }
}

/* Rounded, semi-transparent box with centered status text */
final class StatusOverlay: NSView {
    private let label = NSTextField(wrappingLabelWithString: "")

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        layer?.backgroundColor = NSColor(white: 0, alpha: 0.75).cgColor
        layer?.cornerRadius = 18
        label.font = .systemFont(ofSize: 22, weight: .medium)
        label.textColor = .white
        label.alignment = .center
        label.preferredMaxLayoutWidth = 900
        label.translatesAutoresizingMaskIntoConstraints = false
        addSubview(label)
        NSLayoutConstraint.activate([
            label.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 36),
            label.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -36),
            label.topAnchor.constraint(equalTo: topAnchor, constant: 28),
            label.bottomAnchor.constraint(equalTo: bottomAnchor, constant: -28),
        ])
    }

    required init?(coder: NSCoder) { fatalError("not supported") }

    var text: String {
        get { label.stringValue }
        set { label.stringValue = newValue }
    }
}

extension NSCursor {
    /* the Mac pointer over the laptop's picture: Windows draws its own pointer into the video */
    static let invisible = NSCursor(image: NSImage(size: NSSize(width: 1, height: 1)), hotSpot: .zero)
}

/*
 A big symbol for a moment when the laptop's connection changes, in a box like the volume symbol of macOS:
 an Ethernet socket when it comes over the cable, the Wi-Fi fan when over Wi-Fi
 */
final class TransportSymbol: NSView {
    private var shown = Transport.cable
    private var fading = false
    /* counts the shows, so a fade that ends after the next show does not hide it */
    private var shows = 0

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        layer?.backgroundColor = NSColor(white: 0, alpha: 0.75).cgColor
        layer?.cornerRadius = 32
        isHidden = true
        alphaValue = 0
    }

    required init?(coder: NSCoder) { fatalError("not supported") }

    override var isFlipped: Bool { true }

    func show(_ transport: Transport) {
        if transport != shown {
            shown = transport
            needsDisplay = true
        }
        setAccessibilityLabel(transport == .cable ? "Verbunden über LAN-Kabel" : "Verbunden über WLAN")
        guard isHidden || fading else { return }
        shows += 1
        fading = false
        isHidden = false
        NSAnimationContext.runAnimationGroup({ (context: NSAnimationContext) -> Void in
            context.duration = 0.1
            self.animator().alphaValue = 1
        }, completionHandler: nil)
    }

    func hide() {
        guard !isHidden, !fading else { return }
        fading = true
        let current = shows
        NSAnimationContext.runAnimationGroup({ (context: NSAnimationContext) -> Void in
            context.duration = 0.25
            self.animator().alphaValue = 0
        }, completionHandler: { [weak self] () -> Void in
            guard let self = self, self.shows == current else { return }
            self.fading = false
            self.isHidden = true
        })
    }

    override func draw(_ dirtyRect: NSRect) {
        let side = min(bounds.width, bounds.height) * 0.56
        let r = NSRect(x: bounds.midX - side / 2, y: bounds.midY - side / 2, width: side, height: side)
        NSColor.white.set()
        if shown == .cable {
            TransportSymbol.drawSocket(in: r)
        } else {
            TransportSymbol.drawWifi(in: r)
        }
    }

    /* an Ethernet socket from the front: the latch slot below, eight contacts at the top */
    private static func drawSocket(in r: NSRect) {
        let w = r.width
        let h = r.height
        let corners: [(CGFloat, CGFloat)] = [(0.05, 0.14), (0.95, 0.14), (0.95, 0.70), (0.68, 0.70),
                                             (0.68, 0.88), (0.32, 0.88), (0.32, 0.70), (0.05, 0.70)]
        let outline = NSBezierPath()
        for (index, corner) in corners.enumerated() {
            let point = NSPoint(x: r.minX + w * corner.0, y: r.minY + h * corner.1)
            if index == 0 { outline.move(to: point) } else { outline.line(to: point) }
        }
        outline.close()
        outline.lineWidth = w * 0.085
        outline.lineJoinStyle = .round
        outline.stroke()
        let contacts = NSBezierPath()
        contacts.lineWidth = w * 0.04
        contacts.lineCapStyle = .round
        for pin in 0..<8 {
            let x = r.minX + w * (0.23 + CGFloat(pin) * 0.0771)
            contacts.move(to: NSPoint(x: x, y: r.minY + h * 0.27))
            contacts.line(to: NSPoint(x: x, y: r.minY + h * 0.45))
        }
        contacts.stroke()
    }

    /* the Wi-Fi fan: a dot and three arcs above it */
    private static func drawWifi(in r: NSRect) {
        let w = r.width
        let h = r.height
        let center = NSPoint(x: r.midX, y: r.minY + h * 0.80)
        for radius in [0.27, 0.48, 0.69] as [CGFloat] {
            let arc = NSBezierPath()
            arc.appendArc(withCenter: center, radius: h * radius, startAngle: 225, endAngle: 315, clockwise: false)
            arc.lineWidth = w * 0.095
            arc.lineCapStyle = .round
            arc.stroke()
        }
        let dot = h * 0.075
        NSBezierPath(ovalIn: NSRect(x: center.x - dot, y: center.y - dot, width: dot * 2, height: dot * 2)).fill()
    }
}

/*
 Black full-window container: video underneath, status overlay and the symbol of a changed connection on top.
 The Mac cursor hides when idle, and completely while the laptop is being controlled (Windows draws its own
 cursor into the video).
 */
final class ContainerView: NSView {
    let videoView = VideoLayerView()
    let overlay = StatusOverlay()
    let symbol = TransportSymbol()

    var controlling = false {
        didSet {
            guard controlling != oldValue else { return }
            window?.invalidateCursorRects(for: self)
            if controlling {
                hidePointer()
            } else if pointerInside {
                NSCursor.arrow.set()
            }
        }
    }

    override func resetCursorRects() {
        if controlling { addCursorRect(bounds, cursor: NSCursor.invisible) }
    }

    /*
     Hides the Mac pointer over the picture right away. The cursor rect alone acts only when the pointer enters it:
     after a notification, the login window or a sheet the Mac pointer stayed visible next to the Windows one, and
     moving it could leave a trail of pointers over the video, until the desktop was switched.
     */
    func hidePointer() {
        if controlling && pointerInside { NSCursor.invisible.set() }
    }

    private var pointerInside: Bool {
        guard let window = window, window.isVisible else { return false }
        return bounds.contains(convert(window.mouseLocationOutsideOfEventStream, from: nil))
    }

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        layer?.backgroundColor = NSColor.black.cgColor
        videoView.translatesAutoresizingMaskIntoConstraints = false
        overlay.translatesAutoresizingMaskIntoConstraints = false
        symbol.translatesAutoresizingMaskIntoConstraints = false
        addSubview(videoView)
        addSubview(overlay)
        addSubview(symbol)
        NSLayoutConstraint.activate([
            videoView.leadingAnchor.constraint(equalTo: leadingAnchor),
            videoView.trailingAnchor.constraint(equalTo: trailingAnchor),
            videoView.topAnchor.constraint(equalTo: topAnchor),
            videoView.bottomAnchor.constraint(equalTo: bottomAnchor),
            overlay.centerXAnchor.constraint(equalTo: centerXAnchor),
            overlay.centerYAnchor.constraint(equalTo: centerYAnchor),
            symbol.centerXAnchor.constraint(equalTo: centerXAnchor),
            symbol.centerYAnchor.constraint(equalTo: centerYAnchor),
            symbol.widthAnchor.constraint(equalToConstant: 220),
            symbol.heightAnchor.constraint(equalToConstant: 220),
        ])
        addTrackingArea(NSTrackingArea(rect: .zero, options: [.mouseMoved, .activeAlways, .inVisibleRect],
                                       owner: self, userInfo: nil))
    }

    required init?(coder: NSCoder) { fatalError("not supported") }

    override func mouseMoved(with event: NSEvent) { scheduleCursorHide() }

    func scheduleCursorHide() {
        NSObject.cancelPreviousPerformRequests(withTarget: self, selector: #selector(hideCursor), object: nil)
        perform(#selector(hideCursor), with: nil, afterDelay: 1.5)
    }

    @objc private func hideCursor() {
        NSCursor.setHiddenUntilMouseMoves(true)
    }
}

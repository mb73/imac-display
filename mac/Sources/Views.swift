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

/*
 Black full-window container: video underneath, status overlay on top. The Mac cursor hides when
 idle, and completely while the laptop is being controlled (Windows draws its own cursor into the video).
 */
final class ContainerView: NSView {
    let videoView = VideoLayerView()
    let overlay = StatusOverlay()
    private static let invisibleCursor = NSCursor(image: NSImage(size: NSSize(width: 1, height: 1)), hotSpot: .zero)

    var controlling = false {
        didSet {
            if controlling != oldValue { window?.invalidateCursorRects(for: self) }
        }
    }

    override func resetCursorRects() {
        if controlling { addCursorRect(bounds, cursor: ContainerView.invisibleCursor) }
    }

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        layer?.backgroundColor = NSColor.black.cgColor
        videoView.translatesAutoresizingMaskIntoConstraints = false
        overlay.translatesAutoresizingMaskIntoConstraints = false
        addSubview(videoView)
        addSubview(overlay)
        NSLayoutConstraint.activate([
            videoView.leadingAnchor.constraint(equalTo: leadingAnchor),
            videoView.trailingAnchor.constraint(equalTo: trailingAnchor),
            videoView.topAnchor.constraint(equalTo: topAnchor),
            videoView.bottomAnchor.constraint(equalTo: bottomAnchor),
            overlay.centerXAnchor.constraint(equalTo: centerXAnchor),
            overlay.centerYAnchor.constraint(equalTo: centerYAnchor),
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

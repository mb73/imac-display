import AppKit

/* Connection states as the Dock icon shows them, the same as the taskbar button on the laptop */
enum Light {
    case off, busy, paused, on, onWifi, problem
}

/*
 The badges of the laptop's taskbar button (Badges.Draw in windows/src/Controls.cs) with the same proportions,
 drawn in a flipped view like there: a round light with a white ring and a white mark, a check for on, the Wi-Fi
 symbol for on over Wi-Fi, two bars for paused, an exclamation mark for a problem, none while busy.
 */
enum Badge {
    /* the colors of the taskbar badges, which Windows draws on light and dark taskbars alike */
    static func color(_ light: Light) -> NSColor {
        switch light {
        case .on, .onWifi:
            return NSColor(srgbRed: 0x10 / 255.0, green: 0x7C / 255.0, blue: 0x10 / 255.0, alpha: 1)
        case .busy, .paused:
            return NSColor(srgbRed: 0xE0 / 255.0, green: 0x8A / 255.0, blue: 0x00 / 255.0, alpha: 1)
        case .problem:
            return NSColor(srgbRed: 0xD9 / 255.0, green: 0x2D / 255.0, blue: 0x2D / 255.0, alpha: 1)
        case .off:
            return NSColor(srgbRed: 0xA6 / 255.0, green: 0xA6 / 255.0, blue: 0xA6 / 255.0, alpha: 1)
        }
    }

    /* r in a flipped view: y grows downwards, as in the Windows drawing */
    static func draw(_ light: Light, in r: NSRect) {
        let w = r.width
        let h = r.height
        color(light).setFill()
        NSBezierPath(ovalIn: r).fill()
        let ring = NSBezierPath(ovalIn: r)
        ring.lineWidth = w * 0.086
        NSColor.white.set()
        ring.stroke()
        switch light {
        case .on:
            let tick = NSBezierPath()
            tick.lineWidth = w * 0.138
            tick.lineCapStyle = .round
            tick.lineJoinStyle = .round
            tick.move(to: NSPoint(x: r.minX + w * 0.259, y: r.minY + h * 0.534))
            tick.line(to: NSPoint(x: r.minX + w * 0.431, y: r.minY + h * 0.707))
            tick.line(to: NSPoint(x: r.minX + w * 0.776, y: r.minY + h * 0.328))
            tick.stroke()
        case .onWifi:
            /* a dot and two arcs above it */
            let center = NSPoint(x: r.minX + w * 0.5, y: r.minY + h * 0.69)
            for radius in [0.20, 0.36] as [CGFloat] {
                let arc = NSBezierPath()
                arc.appendArc(withCenter: center, radius: w * radius, startAngle: 225, endAngle: 315, clockwise: false)
                arc.lineWidth = w * 0.105
                arc.lineCapStyle = .round
                arc.stroke()
            }
            NSBezierPath(ovalIn: NSRect(x: center.x - w * 0.075, y: center.y - h * 0.075, width: w * 0.15, height: h * 0.15)).fill()
        case .paused:
            NSBezierPath(rect: NSRect(x: r.minX + w * 0.30, y: r.minY + h * 0.28, width: w * 0.14, height: h * 0.44)).fill()
            NSBezierPath(rect: NSRect(x: r.minX + w * 0.56, y: r.minY + h * 0.28, width: w * 0.14, height: h * 0.44)).fill()
        case .problem:
            NSBezierPath(rect: NSRect(x: r.minX + w * 0.43, y: r.minY + h * 0.20, width: w * 0.14, height: h * 0.38)).fill()
            NSBezierPath(ovalIn: NSRect(x: r.minX + w * 0.42, y: r.minY + h * 0.65, width: w * 0.16, height: h * 0.16)).fill()
        case .busy, .off:
            break
        }
    }
}

/* The Dock icon: the app's icon with the light of the connection at its bottom right, like the badge on the taskbar */
final class DockTileView: NSView {
    var light = Light.off {
        didSet {
            if light != oldValue { NSApp.dockTile.display() }
        }
    }

    override var isFlipped: Bool { true }

    override func draw(_ dirtyRect: NSRect) {
        NSApp.applicationIconImage?.draw(in: bounds, from: .zero, operation: .sourceOver, fraction: 1,
                                         respectFlipped: true, hints: nil)
        guard light != .off else { return }
        let side = (bounds.width * 0.42).rounded()
        let inset = bounds.width * 0.03
        Badge.draw(light, in: NSRect(x: bounds.maxX - side - inset, y: bounds.maxY - side - inset, width: side, height: side))
    }
}

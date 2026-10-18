import AppKit

/*
 Captures keyboard and mouse while LaptopScreen is the active app and forwards them to the laptop.
 - Text is sent as Unicode, exactly as the Mac keyboard layout produces it (Option characters,
   dead keys and umlauts just work, independent of the Windows layout).
 - Shortcuts (Cmd or Ctrl held) are sent as Windows keys: Cmd -> Ctrl, Ctrl -> Win, Option -> Alt.
 - Ctrl+click becomes a right click. System shortcuts such as Ctrl+Left/Right (switch desktop)
   or Cmd+Tab never reach the app, so they keep working for macOS.
 - Pushing the pointer against an edge of the screen also sends the movement beyond it ("N"). Where
   Windows has another display next to this one (the laptop's own panel, lid open), the agent moves its
   cursor over and answers "POINTER away": the Mac pointer then rests in the middle of the screen,
   detached from the mouse, and only movements and clicks without position go over, until the agent
   reports "POINTER home x y" and the Mac pointer continues from there.
 - Outside full screen the window's title bar belongs to the Mac: clicks there move, zoom or close the window.
   Outside the picture the Mac pointer is the Mac's, but at an edge of the screen it can still go over to
   another display of the laptop ("X").
 Protocol lines: M x y | N dx dy | X x y dx dy | B button down x y mods | W dy dx mods | K vk down mods ext |
                 C codepoint down mods | T utf16hex | S mods | R
 */
final class InputCapture {
    var send: ((String) -> Void)?
    weak var view: NSView?
    var videoSize = CGSize(width: 16, height: 9)
    var isEnabled = false {
        didSet { if oldValue && !isEnabled { releaseAll() } }
    }

    private var monitor: Any?
    private var poller: Timer?
    /* uptime of the last mouse event that reached LaptopScreen, and where the pointer was at the last poll */
    private var lastEventAt: TimeInterval = 0
    private var lastPolled = NSPoint.zero
    private var pressedSpecial: [UInt16: (vk: Int, mods: Int, extended: Bool)] = [:]
    private var pressedChar: [UInt16: (codepoint: UInt32, mods: Int)] = [:]
    private var lastMods = -1
    private var rightClickViaControl = false
    private var scrollRemainderX = 0.0
    private var scrollRemainderY = 0.0
    /* the laptop's cursor is on another of its displays; the Mac pointer rests in the middle meanwhile */
    private var away = false
    /* uptime just before the last warp of the Mac pointer */
    private var warpedAt: TimeInterval = -1
    /* the first movement after a warp may carry the jump in its delta */
    private var distrustDelta = false
    private var nudgeRemainderX = 0.0
    private var nudgeRemainderY = 0.0
    /* buttons pressed over the laptop's picture: they stay with the laptop until released, wherever that happens */
    private var laptopButtons = Set<Int>()

    func install() {
        let mask: NSEvent.EventTypeMask = [
            .keyDown, .keyUp, .flagsChanged, .scrollWheel, .mouseMoved,
            .leftMouseDown, .leftMouseUp, .rightMouseDown, .rightMouseUp, .otherMouseDown, .otherMouseUp,
            .leftMouseDragged, .rightMouseDragged, .otherMouseDragged,
        ]
        monitor = NSEvent.addLocalMonitorForEvents(matching: mask) { [weak self] event in
            guard let self = self else { return event }
            return self.handle(event)
        }
        lastPolled = NSEvent.mouseLocation
        poller = Timer.scheduledTimer(withTimeInterval: 1.0 / 60, repeats: true) { [weak self] (_: Timer) in
            self?.poll()
        }
    }

    /*
     Sometimes the mouse moves over the picture, but its events no longer reach LaptopScreen: after the Mac
     wakes up and is unlocked, or when a notification shows up at the top right. The pointer then froze until
     a click. Now the polled position goes to the laptop instead, and LaptopScreen takes the focus back from
     the login window or the notifications, though not from apps the user chose (Spotlight, Mission Control)
     nor from its own About panel, whose links could not be clicked otherwise.
     */
    private func poll() {
        let location = NSEvent.mouseLocation
        let moved = location != lastPolled
        lastPolled = location
        guard moved, isEnabled, !away, let view = view, let window = view.window, window.isVisible,
              window.isOnActiveSpace, window.attachedSheet == nil,
              NSApp.keyWindow == nil || NSApp.keyWindow === window else { return }
        let point = view.convert(window.convertPoint(fromScreen: location), from: nil)
        guard InputCapture.isOver(point, view.bounds) else {
            if NSApp.isActive { reachedEdge(location, in: view) }
            return
        }
        guard ProcessInfo.processInfo.systemUptime - lastEventAt > 0.1 else { return }
        let rect = videoRect(in: view.bounds)
        let nx = max(0, min(1, (point.x - rect.minX) / rect.width))
        let ny = max(0, min(1, (rect.maxY - point.y) / rect.height))
        send?("M \(Int(nx * 65535)) \(Int(ny * 65535))")
        if !NSApp.isActive {
            let front = NSWorkspace.shared.frontmostApplication?.bundleIdentifier ?? ""
            if front == "com.apple.loginwindow" || front == "com.apple.notificationcenterui" {
                NSApp.activate(ignoringOtherApps: true)
            }
        } else if !window.isKeyWindow {
            window.makeKey()
        }
    }

    /*
     In a window, the Mac pointer outside the picture goes over to another display of the laptop as in full screen:
     at an edge of the Mac's screen where no other Mac screen continues. "X" names the matching point on the edge of
     the picture and the direction beyond it; the agent moves the cursor over only where Windows has a display there
     and answers "POINTER away", elsewhere its cursor stays put (the menu bar, the Dock). Polled, not taken from
     events: over the desktop or another app, mouse events may not reach LaptopScreen at all. Not while a button is
     held: that drags a window of the Mac, and a drag on the laptop crosses with its own events, as in full screen.
     */
    private func reachedEdge(_ location: NSPoint, in view: NSView) {
        guard NSEvent.pressedMouseButtons == 0, let window = view.window, let screen = window.screen?.frame,
              InputCapture.isOver(location, screen) else { return }
        let picture = window.convertToScreen(view.convert(videoRect(in: view.bounds), to: nil))
        guard picture.width > 0, picture.height > 0 else { return }
        let x = Int(max(0, min(1, (location.x - picture.minX) / picture.width)) * 65535)
        let y = Int(max(0, min(1, (picture.maxY - location.y) / picture.height)) * 65535)
        if location.x <= screen.minX + 1 && !InputCapture.macScreen(at: NSPoint(x: screen.minX - 2, y: location.y)) {
            send?("X 0 \(y) -1 0")
        } else if location.x >= screen.maxX - 1 && !InputCapture.macScreen(at: NSPoint(x: screen.maxX + 2, y: location.y)) {
            send?("X 65535 \(y) 1 0")
        }
        /* y in the protocol grows downwards: the bottom of the screen is the bottom of the picture */
        if location.y <= screen.minY + 1 && !InputCapture.macScreen(at: NSPoint(x: location.x, y: screen.minY - 2)) {
            send?("X \(x) 65535 0 1")
        } else if location.y >= screen.maxY - 1 && !InputCapture.macScreen(at: NSPoint(x: location.x, y: screen.maxY + 2)) {
            send?("X \(x) 0 0 -1")
        }
    }

    func releaseAll() {
        if away {
            /* the Mac pointer stays where it rests, attached to the mouse again */
            away = false
            CGAssociateMouseAndMouseCursorPosition(1)
        }
        pressedSpecial.removeAll()
        pressedChar.removeAll()
        laptopButtons.removeAll()
        lastMods = -1
        rightClickViaControl = false
        send?("R")
    }

    /* "POINTER away": the cursor went over to another display; park the Mac pointer, detached from the mouse */
    func pointerAway() {
        guard isEnabled, NSApp.isActive, !away, let view = view else { return }
        away = true
        /* in the middle: at the edge, macOS might show the Dock or the menu bar */
        warp(to: NSPoint(x: view.bounds.midX, y: view.bounds.midY), in: view)
        CGAssociateMouseAndMouseCursorPosition(0)
    }

    /* "POINTER home x y": the cursor is back on the laptop's picture; the Mac pointer continues there */
    func pointerHome(x: Int, y: Int) {
        guard away, let view = view else { return }
        away = false
        let rect = videoRect(in: view.bounds)
        /* a little inside the edge, so that the next small movement does not push it over again */
        let inner = view.bounds.insetBy(dx: 2, dy: 2)
        let px = min(max(rect.minX + CGFloat(x) / 65535 * rect.width, inner.minX), inner.maxX)
        let py = min(max(rect.maxY - CGFloat(y) / 65535 * rect.height, inner.minY), inner.maxY)
        warp(to: NSPoint(x: px, y: py), in: view)
    }

    private func handle(_ event: NSEvent) -> NSEvent? {
        guard isEnabled, NSApp.isActive, let view = view, event.window === view.window else { return event }
        switch event.type {
        case .keyDown:
            return keyDown(event)
        case .keyUp:
            keyUp(event)
            return nil
        case .flagsChanged:
            let mods = InputCapture.mods(event.modifierFlags)
            if mods != lastMods {
                lastMods = mods
                send?("S \(mods)")
            }
            return event
        case .scrollWheel:
            scroll(event)
            return nil
        default:
            guard forLaptop(event, in: view) else { return event }
            mouse(event, in: view)
            return nil
        }
    }

    /*
     Whether a mouse event goes to the laptop. Outside full screen the window has a title bar above the picture: a
     click there would reach the top edge of the laptop's screen, where a maximized window has its close button, so
     it stays with the Mac and moves, zooms or closes the window. Movements outside the picture stay with the Mac too,
     and the laptop's cursor waits where the Mac pointer left. A button pressed over the picture stays with the laptop
     until it is released, wherever that happens. In full screen the view covers the whole screen.
     */
    private func forLaptop(_ event: NSEvent, in view: NSView) -> Bool {
        let inside = InputCapture.isOver(view.convert(event.locationInWindow, from: nil), view.bounds)
        switch event.type {
        case .leftMouseDown, .rightMouseDown, .otherMouseDown:
            guard inside || !laptopButtons.isEmpty else { return false }
            laptopButtons.insert(event.buttonNumber)
            return true
        case .leftMouseUp, .rightMouseUp, .otherMouseUp:
            /* only the laptop's buttons are tracked: the release of a click on the title bar may never come here */
            return laptopButtons.remove(event.buttonNumber) != nil
        case .leftMouseDragged, .rightMouseDragged, .otherMouseDragged:
            return !laptopButtons.isEmpty
        default:
            return inside
        }
    }

    /*
     Whether a point lies over a view or a screen, edges included: mouse positions count y from 1, so in full screen
     the top row of the screen lies on the upper edge of the view
     */
    private static func isOver(_ point: NSPoint, _ rect: NSRect) -> Bool {
        return point.x >= rect.minX && point.x <= rect.maxX && point.y >= rect.minY && point.y <= rect.maxY
    }

    // MARK: - Keyboard

    private static func mods(_ flags: NSEvent.ModifierFlags) -> Int {
        var mods = 0
        if flags.contains(.shift) { mods |= KeyMap.shift }
        if flags.contains(.command) { mods |= KeyMap.ctrl }
        if flags.contains(.option) { mods |= KeyMap.alt }
        if flags.contains(.control) { mods |= KeyMap.win }
        return mods
    }

    private func keyDown(_ event: NSEvent) -> NSEvent? {
        let flags = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
        let plain = (event.charactersIgnoringModifiers ?? "").lowercased()
        /* keep LaptopScreen's own shortcuts: Cmd+Q quits, Ctrl+Cmd+F toggles full screen */
        if flags.contains(.command) && !flags.contains(.control) && plain == "q" { return event }
        if flags.contains(.command) && flags.contains(.control) && plain == "f" { return event }

        let mods = InputCapture.mods(flags)
        if let special = KeyMap.special[event.keyCode] {
            let translated = KeyMap.translate(special, mods: mods)
            pressedSpecial[event.keyCode] = (translated.key.vk, translated.mods, translated.key.extended)
            send?("K \(translated.key.vk) 1 \(translated.mods) \(translated.key.extended ? 1 : 0)")
            return nil
        }
        if mods & (KeyMap.ctrl | KeyMap.win) != 0 {
            /* shortcut: send the unmodified character of the key, the agent maps it to a Windows key */
            let base = event.characters(byApplyingModifiers: []) ?? event.charactersIgnoringModifiers ?? ""
            guard let scalar = base.lowercased().unicodeScalars.first, scalar.value >= 0x20 else { return nil }
            var keyMods = mods
            /* Ctrl+Space stays Ctrl+Space, code completion in VS Code on both systems; Win+Space would switch the input language */
            if scalar.value == 0x20 && mods & KeyMap.win != 0 && mods & KeyMap.ctrl == 0 {
                keyMods = (mods & ~KeyMap.win) | KeyMap.ctrl
            }
            pressedChar[event.keyCode] = (scalar.value, keyMods)
            send?("C \(scalar.value) 1 \(keyMods)")
            return nil
        }
        if let text = event.characters, !text.isEmpty, text.unicodeScalars.allSatisfy({ InputCapture.isPrintable($0) }) {
            send?("T " + text.utf16.map { String(format: "%04x", UInt32($0)) }.joined())
        }
        return nil
    }

    private func keyUp(_ event: NSEvent) {
        if let pressed = pressedSpecial.removeValue(forKey: event.keyCode) {
            send?("K \(pressed.vk) 0 \(pressed.mods) \(pressed.extended ? 1 : 0)")
        } else if let pressed = pressedChar.removeValue(forKey: event.keyCode) {
            send?("C \(pressed.codepoint) 0 \(pressed.mods)")
        }
    }

    private static func isPrintable(_ scalar: Unicode.Scalar) -> Bool {
        let value = scalar.value
        if value < 0x20 || value == 0x7F { return false }
        if value >= 0xF700 && value <= 0xF8FF { return false }  // AppKit function-key range
        return true
    }

    // MARK: - Mouse

    private func mouse(_ event: NSEvent, in view: NSView) {
        lastEventAt = ProcessInfo.processInfo.systemUptime
        /* the cursor rect alone does not hide the Mac pointer again once something else showed it (ContainerView.hidePointer) */
        NSCursor.invisible.set()
        let rect = videoRect(in: view.bounds)
        let mods = InputCapture.mods(event.modifierFlags)
        /*
         An event from before the last warp belongs to the other side of the edge. Just after the warp,
         an event may still show the old position, and the first movement may carry the jump in its delta.
         */
        let stale = event.timestamp <= warpedAt
        let settled = event.timestamp > warpedAt + 0.02
        /* "-1 -1": wherever the laptop's cursor is */
        var x = -1
        var y = -1
        if !away && settled {
            let point = view.convert(event.locationInWindow, from: nil)
            let nx = max(0, min(1, (point.x - rect.minX) / rect.width))
            let ny = max(0, min(1, (rect.maxY - point.y) / rect.height))
            x = Int(nx * 65535)
            y = Int(ny * 65535)
        }

        switch event.type {
        case .mouseMoved, .leftMouseDragged, .rightMouseDragged, .otherMouseDragged:
            var first = false
            if !stale {
                first = distrustDelta
                distrustDelta = false
            }
            if away || stale {
                if stale || (settled && !first) { nudge(Double(event.deltaX), Double(event.deltaY), in: rect) }
                return
            }
            /* the next movement sends the position; "M" is absolute, so nothing gets lost */
            guard settled else { return }
            var push = (dx: 0.0, dy: 0.0)
            if !first { push = outward(event, in: view) }
            /* pushing against an edge: the cursor goes right up to it, and the rest beyond */
            if push.dx < 0 { x = 0 } else if push.dx > 0 { x = 65535 }
            if push.dy < 0 { y = 0 } else if push.dy > 0 { y = 65535 }
            send?("M \(x) \(y)")
            if push.dx != 0 || push.dy != 0 { nudge(push.dx, push.dy, in: rect) }
        case .leftMouseDown:
            if mods & KeyMap.win != 0 {
                rightClickViaControl = true
                send?("B 2 1 \(x) \(y) \(mods & ~KeyMap.win)")
            } else {
                send?("B 1 1 \(x) \(y) \(mods)")
            }
        case .leftMouseUp:
            if rightClickViaControl {
                rightClickViaControl = false
                send?("B 2 0 \(x) \(y) \(mods & ~KeyMap.win)")
            } else {
                send?("B 1 0 \(x) \(y) \(mods)")
            }
        case .rightMouseDown:
            send?("B 2 1 \(x) \(y) \(mods)")
        case .rightMouseUp:
            send?("B 2 0 \(x) \(y) \(mods)")
        case .otherMouseDown, .otherMouseUp:
            /* buttonNumber 2 = middle, 3/4 = back/forward */
            let button = event.buttonNumber == 2 ? 3 : (event.buttonNumber == 3 ? 4 : 5)
            send?("B \(button) \(event.type == .otherMouseDown ? 1 : 0) \(x) \(y) \(mods)")
        default:
            break
        }
    }

    /*
     Movement past an edge of the screen while the pointer is pinned to it; none where another Mac screen
     continues. NSEvent's deltaY grows downwards, like y in the protocol.
     */
    private func outward(_ event: NSEvent, in view: NSView) -> (dx: Double, dy: Double) {
        guard let window = view.window, let screen = window.screen?.frame else { return (0, 0) }
        let location = window.convertPoint(toScreen: event.locationInWindow)
        let deltaX = Double(event.deltaX)
        let deltaY = Double(event.deltaY)
        var dx = 0.0
        var dy = 0.0
        if deltaX < 0 && location.x <= screen.minX + 1 && !InputCapture.macScreen(at: NSPoint(x: screen.minX - 2, y: location.y)) {
            dx = deltaX
        } else if deltaX > 0 && location.x >= screen.maxX - 1 && !InputCapture.macScreen(at: NSPoint(x: screen.maxX + 2, y: location.y)) {
            dx = deltaX
        }
        if deltaY > 0 && location.y <= screen.minY + 1 && !InputCapture.macScreen(at: NSPoint(x: location.x, y: screen.minY - 2)) {
            dy = deltaY
        } else if deltaY < 0 && location.y >= screen.maxY - 1 && !InputCapture.macScreen(at: NSPoint(x: location.x, y: screen.maxY + 2)) {
            dy = deltaY
        }
        return (dx, dy)
    }

    private static func macScreen(at point: NSPoint) -> Bool {
        return NSScreen.screens.contains { $0.frame.contains(point) }
    }

    /* Relative movement in the units of "M", where 65535 spans the laptop's picture */
    private func nudge(_ deltaX: Double, _ deltaY: Double, in rect: CGRect) {
        guard rect.width > 0, rect.height > 0 else { return }
        nudgeRemainderX += deltaX / Double(rect.width) * 65535
        nudgeRemainderY += deltaY / Double(rect.height) * 65535
        let dx = Int(nudgeRemainderX)
        let dy = Int(nudgeRemainderY)
        nudgeRemainderX -= Double(dx)
        nudgeRemainderY -= Double(dy)
        if dx != 0 || dy != 0 { send?("N \(dx) \(dy)") }
    }

    /* Moves the Mac pointer to a point of the view */
    private func warp(to point: NSPoint, in view: NSView) {
        guard let window = view.window, let primary = NSScreen.screens.first else { return }
        let location = window.convertPoint(toScreen: view.convert(point, to: nil))
        warpedAt = ProcessInfo.processInfo.systemUptime
        distrustDelta = true
        CGWarpMouseCursorPosition(CGPoint(x: location.x, y: primary.frame.maxY - location.y))
        /* attached again right away, macOS does not ignore the mouse for a moment after the warp */
        CGAssociateMouseAndMouseCursorPosition(1)
    }

    private func scroll(_ event: NSEvent) {
        /* Windows: 120 units per wheel notch; trackpads deliver pixel deltas */
        let factor = event.hasPreciseScrollingDeltas ? 4.0 : 120.0
        scrollRemainderY += Double(event.scrollingDeltaY) * factor
        scrollRemainderX += Double(event.scrollingDeltaX) * factor
        let dy = Int(scrollRemainderY)
        let dx = Int(scrollRemainderX)
        scrollRemainderY -= Double(dy)
        scrollRemainderX -= Double(dx)
        if dy != 0 || dx != 0 {
            send?("W \(dy) \(-dx) \(InputCapture.mods(event.modifierFlags))")
        }
    }

    private func videoRect(in bounds: CGRect) -> CGRect {
        guard videoSize.width > 0, videoSize.height > 0 else { return bounds }
        let scale = min(bounds.width / videoSize.width, bounds.height / videoSize.height)
        let width = videoSize.width * scale
        let height = videoSize.height * scale
        return CGRect(x: bounds.midX - width / 2, y: bounds.midY - height / 2, width: width, height: height)
    }
}

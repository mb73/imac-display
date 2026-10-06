import AppKit

/*
 Captures keyboard and mouse while LaptopScreen is the active app and forwards them to the laptop.
 - Text is sent as Unicode, exactly as the Mac keyboard layout produces it (Option characters,
   dead keys and umlauts just work, independent of the Windows layout).
 - Shortcuts (Cmd or Ctrl held) are sent as Windows keys: Cmd -> Ctrl, Ctrl -> Win, Option -> Alt.
 - Ctrl+click becomes a right click. System shortcuts such as Ctrl+Left/Right (switch desktop)
   or Cmd+Tab never reach the app, so they keep working for macOS.
 Protocol lines: M x y | B button down x y mods | W dy dx mods | K vk down mods ext |
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
    private var pressedSpecial: [UInt16: (vk: Int, mods: Int, extended: Bool)] = [:]
    private var pressedChar: [UInt16: UInt32] = [:]
    private var lastMods = -1
    private var rightClickViaControl = false
    private var scrollRemainderX = 0.0
    private var scrollRemainderY = 0.0

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
    }

    func releaseAll() {
        pressedSpecial.removeAll()
        pressedChar.removeAll()
        lastMods = -1
        rightClickViaControl = false
        send?("R")
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
            mouse(event, in: view)
            return nil
        }
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
            pressedChar[event.keyCode] = scalar.value
            send?("C \(scalar.value) 1 \(mods)")
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
        } else if let codepoint = pressedChar.removeValue(forKey: event.keyCode) {
            send?("C \(codepoint) 0 \(InputCapture.mods(event.modifierFlags))")
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
        let point = view.convert(event.locationInWindow, from: nil)
        let rect = videoRect(in: view.bounds)
        let nx = max(0, min(1, (point.x - rect.minX) / rect.width))
        let ny = max(0, min(1, (rect.maxY - point.y) / rect.height))
        let x = Int(nx * 65535)
        let y = Int(ny * 65535)
        let mods = InputCapture.mods(event.modifierFlags)

        switch event.type {
        case .mouseMoved, .leftMouseDragged, .rightMouseDragged, .otherMouseDragged:
            send?("M \(x) \(y)")
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

import AppKit
import Carbon

/*
 What a key types on the Mac's current keyboard layout, dead keys included. macOS composes a dead key with the key after it
 (´ then E gives é) only in its text input system, and LaptopScreen takes key events before they get there: in the events,
 the dead key alone types nothing, and the key after it just its own character. So ´ and ` (next to ß on a German keyboard)
 never reached the laptop. UCKeyTranslate composes them from the layout's own tables, carrying the state of a dead key from
 one key to the next, as the text input system does.
 */
enum KeyLayout {
    /*
     The text of a key, or nil if the layout cannot tell (no Unicode tables). state is the dead key pending from the key
     before, 0 for none; afterwards it is the dead key this key leaves pending. With deadKeys false, a dead key types its
     own character instead, as it does before a key that does not combine with it.
     */
    static func text(of keyCode: UInt16, flags: NSEvent.ModifierFlags, repeated: Bool = false, deadKeys: Bool = true,
                     state: inout UInt32) -> String? {
        guard let source = TISCopyCurrentKeyboardLayoutInputSource()?.takeRetainedValue(),
              let property = TISGetInputSourceProperty(source, kTISPropertyUnicodeKeyLayoutData) else { return nil }
        let data = unsafeBitCast(property, to: CFData.self)
        /* optional or not depends on the SDK's annotations; declared optional, it compiles either way */
        let start: UnsafePointer<UInt8>? = CFDataGetBytePtr(data)
        guard let bytes = start else { return nil }
        let layout = UnsafeRawPointer(bytes).assumingMemoryBound(to: UCKeyboardLayout.self)
        /* Carbon's modifier bits, shifted down by 8 as UCKeyTranslate wants them; Command and Control do not type text here */
        var modifiers: UInt32 = 0
        if flags.contains(.shift) { modifiers |= UInt32(shiftKey >> 8) }
        if flags.contains(.option) { modifiers |= UInt32(optionKey >> 8) }
        if flags.contains(.capsLock) { modifiers |= UInt32(alphaLock >> 8) }
        let action = repeated ? kUCKeyActionAutoKey : kUCKeyActionDown
        let options = deadKeys ? 0 : kUCKeyTranslateNoDeadKeysMask
        var chars = [UniChar](repeating: 0, count: 32)
        var length = 0
        let status = UCKeyTranslate(layout, keyCode, UInt16(action), modifiers, UInt32(LMGetKbdType()), OptionBits(options),
                                    &state, chars.count, &length, &chars)
        guard status == noErr else { return nil }
        return String(utf16CodeUnits: chars, count: length)
    }
}

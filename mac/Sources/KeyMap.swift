import Foundation

/* Mapping of macOS key codes to Windows virtual keys for keys that do not produce text */
enum KeyMap {
    struct Special {
        let vk: Int
        let extended: Bool
    }

    /* Windows modifier bits used in the protocol */
    static let shift = 1
    static let ctrl = 2
    static let alt = 4
    static let win = 8

    static let special: [UInt16: Special] = [
        36: Special(vk: 0x0D, extended: false),   // Return
        76: Special(vk: 0x0D, extended: true),    // keypad Enter
        48: Special(vk: 0x09, extended: false),   // Tab
        51: Special(vk: 0x08, extended: false),   // Backspace
        53: Special(vk: 0x1B, extended: false),   // Escape
        117: Special(vk: 0x2E, extended: true),   // forward Delete
        114: Special(vk: 0x2D, extended: true),   // Help -> Insert
        115: Special(vk: 0x24, extended: true),   // Home
        119: Special(vk: 0x23, extended: true),   // End
        116: Special(vk: 0x21, extended: true),   // Page Up
        121: Special(vk: 0x22, extended: true),   // Page Down
        123: Special(vk: 0x25, extended: true),   // Left
        124: Special(vk: 0x27, extended: true),   // Right
        125: Special(vk: 0x28, extended: true),   // Down
        126: Special(vk: 0x26, extended: true),   // Up
        122: Special(vk: 0x70, extended: false),  // F1
        120: Special(vk: 0x71, extended: false),  // F2
        99: Special(vk: 0x72, extended: false),   // F3
        118: Special(vk: 0x73, extended: false),  // F4
        96: Special(vk: 0x74, extended: false),   // F5
        97: Special(vk: 0x75, extended: false),   // F6
        98: Special(vk: 0x76, extended: false),   // F7
        100: Special(vk: 0x77, extended: false),  // F8
        101: Special(vk: 0x78, extended: false),  // F9
        109: Special(vk: 0x79, extended: false),  // F10
        103: Special(vk: 0x7A, extended: false),  // F11
        111: Special(vk: 0x7B, extended: false),  // F12
        105: Special(vk: 0x7C, extended: false),  // F13
        107: Special(vk: 0x7D, extended: false),  // F14
        113: Special(vk: 0x7E, extended: false),  // F15
        106: Special(vk: 0x7F, extended: false),  // F16
        64: Special(vk: 0x80, extended: false),   // F17
        79: Special(vk: 0x81, extended: false),   // F18
        80: Special(vk: 0x82, extended: false),   // F19
        90: Special(vk: 0x83, extended: false),   // F20
    ]

    /*
     Mac text-navigation semantics on Windows: Option+arrow jumps words, Cmd+Left/Right goes to
     line start/end, Cmd+Up/Down to document start/end, Option+Backspace deletes a word.
     The incoming modifiers are already mapped (Cmd -> ctrl, Option -> alt).
     */
    static func translate(_ key: Special, mods: Int) -> (key: Special, mods: Int) {
        switch key.vk {
        case 0x25, 0x27:
            if mods & alt != 0 { return (key, (mods & ~alt) | ctrl) }
            if mods & ctrl != 0 { return (Special(vk: key.vk == 0x25 ? 0x24 : 0x23, extended: true), mods & ~ctrl) }
        case 0x26, 0x28:
            if mods & ctrl != 0 { return (Special(vk: key.vk == 0x26 ? 0x24 : 0x23, extended: true), mods) }
        case 0x08, 0x2E:
            if mods & alt != 0 { return (key, (mods & ~alt) | ctrl) }
        default:
            break
        }
        return (key, mods)
    }
}

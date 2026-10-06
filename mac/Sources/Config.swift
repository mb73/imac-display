import Foundation

enum Config {
    /* TCP port on which the laptop pushes its FLV/H.264 video stream */
    static let videoPort: UInt16 = 47100
    /* TCP port of the control channel (pairing handshake, input events); advertised via Bonjour */
    static let controlPort: UInt16 = 47101
    static let bonjourType = "_laptopscreen._tcp"
    /* upper limits for data arriving over the control channel */
    static let maxUpdateBytes = 16 << 20
    static let maxClipboardBytes = 4 << 20
}

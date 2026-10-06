import CryptoKit
import Foundation

/*
 Pairing code shown on the Mac and typed once on the laptop. Both sides prove that they
 know it with HMAC-SHA256 over fresh nonces, so a random device on the network can neither
 receive the keystrokes nor inject a fake picture.
 */
enum Pairing {
    private static let defaultsKey = "pairingCode"
    private static let alphabet = Array("ABCDEFGHJKLMNPQRSTUVWXYZ23456789")

    static var code: String {
        if let existing = UserDefaults.standard.string(forKey: defaultsKey), !existing.isEmpty {
            return existing
        }
        return renew()
    }

    @discardableResult
    static func renew() -> String {
        var generator = SystemRandomNumberGenerator()
        var chars: [Character] = []
        for _ in 0..<12 {
            chars.append(alphabet[Int(generator.next(upperBound: UInt32(alphabet.count)))])
        }
        let code = String(chars[0..<4]) + "-" + String(chars[4..<8]) + "-" + String(chars[8..<12])
        UserDefaults.standard.set(code, forKey: defaultsKey)
        return code
    }

    /* Key material: upper-cased code without separators (must match the Windows agent) */
    static func normalized(_ code: String) -> String {
        String(code.uppercased().filter { $0.isLetter || $0.isNumber })
    }

    static func hmacHex(_ message: String, code: String) -> String {
        let key = SymmetricKey(data: Data(normalized(code).utf8))
        let mac = HMAC<SHA256>.authenticationCode(for: Data(message.utf8), using: key)
        return Data(mac).map { String(format: "%02x", UInt32($0)) }.joined()
    }

    static func nonce() -> String {
        var generator = SystemRandomNumberGenerator()
        var parts: [String] = []
        for _ in 0..<16 {
            parts.append(String(format: "%02x", UInt32(UInt8.random(in: 0...255, using: &generator))))
        }
        return parts.joined()
    }

    /* Constant-time comparison of two hex strings */
    static func matches(_ a: String, _ b: String) -> Bool {
        let x = Array(a.utf8)
        let y = Array(b.utf8)
        guard x.count == y.count else { return false }
        var difference: UInt8 = 0
        for i in 0..<x.count { difference |= x[i] ^ y[i] }
        return difference == 0
    }
}

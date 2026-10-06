import AppKit

/* Version of this build (CFBundleShortVersionString, which build.sh takes from VERSION) */
enum AppInfo {
    static let version: String = { () -> String in
        let value = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String
        let trimmed = (value ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed.isEmpty ? "0.0.0" : trimmed
    }()

    /* Compares dotted version numbers: negative if a is older than b, 0 if equal, positive if newer */
    static func compare(_ a: String, _ b: String) -> Int {
        let x = numbers(a)
        let y = numbers(b)
        for index in 0..<max(x.count, y.count) {
            let p = index < x.count ? x[index] : 0
            let q = index < y.count ? y[index] : 0
            if p != q { return p < q ? -1 : 1 }
        }
        return 0
    }

    private static func numbers(_ version: String) -> [Int] {
        return version.split(separator: ".").map { (part: Substring) -> Int in
            Int(String(part.prefix(while: { $0.isASCII && $0.isNumber }))) ?? 0
        }
    }
}

struct UpdateError: Error {
    let message: String
}

/*
 Installs a LaptopScreen update that the laptop sent (a zip with VERSION and the mac folder):
 builds it with build.sh and Apple's Command Line Tools, swaps this app bundle for the new one
 and restarts. If anything fails, the running version stays in place.
 */
final class SelfUpdater {
    /* Called on the main queue with a message if the update failed */
    var onFailure: ((String) -> Void)?
    private(set) var isRunning = false
    private let queue = DispatchQueue(label: "self-updater", qos: .userInitiated)

    func install(_ archive: Data) {
        if isRunning { return }
        isRunning = true
        let target = Bundle.main.bundleURL
        queue.async { [weak self] in
            guard let self = self else { return }
            do {
                let built = try SelfUpdater.build(archive)
                try SelfUpdater.replace(target, with: built)
                DispatchQueue.main.async { SelfUpdater.relaunch(target) }
            } catch {
                let message = (error as? UpdateError)?.message ?? error.localizedDescription
                DispatchQueue.main.async {
                    self.isRunning = false
                    self.onFailure?(message)
                }
            }
        }
    }

    /* Unpacks the archive into a temporary folder and runs build.sh there; returns the built app */
    private static func build(_ archive: Data) throws -> URL {
        guard succeeds("/usr/bin/xcode-select", ["-p"]) else {
            throw UpdateError(message: "Zum Aktualisieren braucht der Mac Apples Command Line Tools.\n"
                + "Im Terminal installieren: xcode-select --install")
        }
        let fileManager = FileManager.default
        let work = fileManager.temporaryDirectory.appendingPathComponent("LaptopScreen-update", isDirectory: true)
        try? fileManager.removeItem(at: work)
        try fileManager.createDirectory(at: work, withIntermediateDirectories: true, attributes: nil)
        let zip = work.appendingPathComponent("update.zip")
        try archive.write(to: zip)
        let sources = work.appendingPathComponent("sources", isDirectory: true)
        try run("/usr/bin/ditto", ["-x", "-k", zip.path, sources.path], step: "Das Entpacken")
        let script = sources.appendingPathComponent("mac/build.sh")
        guard fileManager.fileExists(atPath: script.path) else {
            throw UpdateError(message: "Im Update fehlt mac/build.sh.")
        }
        try run("/bin/sh", [script.path], step: "Das Bauen")
        let app = sources.appendingPathComponent("mac/LaptopScreen.app")
        guard fileManager.fileExists(atPath: app.path) else {
            throw UpdateError(message: "build.sh hat kein LaptopScreen.app erzeugt.")
        }
        return app
    }

    /* The old bundle is moved aside first and comes back if copying the new one fails */
    private static func replace(_ target: URL, with built: URL) throws {
        let fileManager = FileManager.default
        let backup = target.deletingLastPathComponent().appendingPathComponent(".LaptopScreen-previous.app")
        try? fileManager.removeItem(at: backup)
        do {
            try fileManager.moveItem(at: target, to: backup)
        } catch {
            throw UpdateError(message: "\(target.path) lässt sich nicht ersetzen: \(error.localizedDescription)\n\n"
                + "Falls macOS das blockiert: Systemeinstellungen → Datenschutz & Sicherheit → App-Verwaltung → LaptopScreen erlauben.")
        }
        do {
            try fileManager.copyItem(at: built, to: target)
        } catch {
            try? fileManager.moveItem(at: backup, to: target)
            throw UpdateError(message: "Die neue Version lässt sich nicht installieren: \(error.localizedDescription)")
        }
        try? fileManager.removeItem(at: backup)
    }

    /* Starts the new bundle once this process is gone, since both cannot hold the ports at once */
    private static func relaunch(_ app: URL) {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/bin/sh")
        process.arguments = ["-c", "sleep 2; /usr/bin/open \"$0\"", app.path]
        try? process.run()
        NSApp.terminate(nil)
    }

    /* Runs a tool; if it fails, its last output lines become the error message */
    private static func run(_ tool: String, _ arguments: [String], step: String) throws {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: tool)
        process.arguments = arguments
        let pipe = Pipe()
        process.standardOutput = pipe
        process.standardError = pipe
        try process.run()
        let output = pipe.fileHandleForReading.readDataToEndOfFile()
        process.waitUntilExit()
        guard process.terminationStatus == 0 else {
            let lines = String(decoding: output, as: UTF8.self).split(separator: "\n").suffix(6)
            throw UpdateError(message: "\(step) ist fehlgeschlagen:\n" + lines.joined(separator: "\n"))
        }
    }

    private static func succeeds(_ tool: String, _ arguments: [String]) -> Bool {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: tool)
        process.arguments = arguments
        process.standardOutput = FileHandle.nullDevice
        process.standardError = FileHandle.nullDevice
        do {
            try process.run()
        } catch {
            return false
        }
        process.waitUntilExit()
        return process.terminationStatus == 0
    }
}

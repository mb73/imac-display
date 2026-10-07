import CoreGraphics
import CryptoKit
import Foundation
import Network

/* Accumulates bytes and splits them into "\n"-terminated ASCII lines */
final class LineReader {
    private var buffer = Data()

    func feed(_ data: Data) -> [String] {
        buffer.append(data)
        var lines: [String] = []
        while let index = buffer.firstIndex(of: 0x0A) {
            let lineData = buffer.subdata(in: buffer.startIndex..<index)
            buffer.removeSubrange(buffer.startIndex...index)
            if let line = String(data: lineData, encoding: .utf8) {
                lines.append(line.trimmingCharacters(in: CharacterSet(charactersIn: "\r")))
            }
        }
        if buffer.count > 65536 { buffer.removeAll() }  // protection against garbage without newlines
        return lines
    }
}

/*
 Control channel to the Windows agent. The listener is advertised via Bonjour so the laptop
 finds the Mac on cable or Wi-Fi without configuration. Handshake (all lines end with "\n"):
   Mac   -> "LAPTOPSCREEN 1 <nonceMac>"
   Agent -> "HELLO <nonceAgent> <hmac(code, "agent|<nonceMac>|<nonceAgent>")>"
   Mac   -> "WELCOME <hmac(code, "mac|<nonceAgent>|<nonceMac>")> <videoPort>"   or "DENIED"
 Right after it both sides announce "VERSION <x>", the agent also "CLIPBOARD on|off", the Mac also
 "SCREEN <width> <height>" (pixels of its screen, again after every change; the agent picks its display mode by it).
 Afterwards the Mac sends input events and "P" pings; the agent answers with "P" and reports
 "STATE locked", "STATE unlocked" or "STATE nodisplay"; "POINTER away" when its cursor went over the
 edge onto another display, "POINTER home x y" when it is back. Clipboard text travels in both directions
 as "CLIP+ <base64>" … "CLIP <base64>"; "FOCUS 1|0" from the Mac says whether LaptopScreen is in
 front, and only then does the agent send its clipboard. On "GETUPDATE" the agent sends the LaptopScreen sources
 it carries: "UPDATE <version> <bytes> <sha256> <proof>", "D <base64>" lines and "E", or "NOUPDATE";
 proof = hmac(code, "update|<nonceMac>|<nonceAgent>|<version>|<sha256>").
 */
final class ControlServer {
    enum Event {
        case waiting
        case connected
        case locked(Bool)
        case noDisplay
        case pointerAway
        case pointerHome(x: Int, y: Int)
        case switchingToCable
        case agentVersion(String)
        case clipboardEnabled(Bool)
        case clipboard(String)
        case update(version: String, archive: Data)
        case updateFailed(String)
        case failed(String)
    }

    /* An update in transit: the zip arrives in "D" lines after the "UPDATE" header */
    private struct IncomingUpdate {
        let version: String
        let size: Int
        let sha256: String
        let proof: String
        var data = Data()
    }

    /* Always called on the main queue */
    var onEvent: ((Event) -> Void)?

    private let port: UInt16
    private let videoPort: UInt16
    private let queue = DispatchQueue(label: "control-server", qos: .userInteractive)
    private var listener: NWListener?
    private var timer: DispatchSourceTimer?
    private var pending: [ObjectIdentifier: NWConnection] = [:]
    private var agent: NWConnection?
    private var lastHeard = Date.distantPast
    private let hostLock = NSLock()
    private var agentHost: String?
    /* pixels of the Mac screen LaptopScreen is on; set on the main queue, read on this server's queue */
    private let screenLock = NSLock()
    private var screen: (width: Int, height: Int)?
    /* nonces of the agent's session; an update must be signed with them */
    private var sessionNonces: (mac: String, agent: String)?
    private var incomingUpdate: IncomingUpdate?
    private var clipboardData = Data()
    private var clipboardBroken = false
    /* signalled when the agent hangs up after "SLEEP" */
    private var sleepWaiter: DispatchSemaphore?

    init(port: UInt16, videoPort: UInt16) {
        self.port = port
        self.videoPort = videoPort
    }

    /*
     LaptopScreen can be found only while the display is on. In a dark wake for maintenance the Mac would
     otherwise answer the agent's search, and the agent's connection attempts kept waking it all night.
     Running sessions are not affected: connections outlive the listener. The search alone does not wake
     a sleeping Mac (measured).
     */
    func setAdvertised(_ on: Bool) {
        queue.async { [weak self] () -> Void in
            guard let self = self, on != (self.listener != nil) else { return }
            if on {
                self.startListener()
            } else {
                self.listener?.cancel()
                self.listener = nil
            }
        }
    }

    func start() {
        setAdvertised(true)
        startTimer()
    }

    private func startListener() {
        do {
            let parameters = NWParameters.tcp
            if let tcp = parameters.defaultProtocolStack.transportProtocol as? NWProtocolTCP.Options {
                tcp.noDelay = true
            }
            parameters.allowLocalEndpointReuse = true
            let listener = try NWListener(using: parameters, on: NWEndpoint.Port(rawValue: port)!)
            listener.service = NWListener.Service(type: Config.bonjourType)
            listener.newConnectionHandler = { [weak self] connection in self?.handshake(connection) }
            listener.stateUpdateHandler = { [weak self] state in
                guard let self = self else { return }
                if case .failed(let error) = state {
                    self.emit(.failed("Steuer-Port \(self.port) lässt sich nicht öffnen: \(error)"))
                }
            }
            listener.start(queue: queue)
            self.listener = listener
        } catch {
            emit(.failed("Steuer-Port \(port) lässt sich nicht öffnen: \(error)"))
        }
    }

    private func startTimer() {
        let timer = DispatchSource.makeTimerSource(queue: queue)
        timer.schedule(deadline: .now() + 2, repeating: 2)
        timer.setEventHandler { [weak self] in self?.tick() }
        timer.resume()
        self.timer = timer
    }

    /* True if the endpoint belongs to the authenticated laptop; vets the video connection */
    func isAgent(_ endpoint: NWEndpoint) -> Bool {
        guard let host = ControlServer.hostKey(endpoint) else { return false }
        hostLock.lock()
        defer { hostLock.unlock() }
        return host == agentHost
    }

    func send(_ line: String) {
        let data = Data((line + "\n").utf8)
        queue.async { [weak self] in
            self?.agent?.send(content: data, completion: .idempotent)
        }
    }

    func disconnectAgent() {
        queue.async { [weak self] in self?.dropAgent() }
    }

    /* Asks the agent for the LaptopScreen sources it carries */
    func requestUpdate() {
        send("GETUPDATE")
    }

    /* The pixels of the Mac screen LaptopScreen is on; the agent picks its display mode by them */
    func setScreen(width: Int, height: Int) {
        screenLock.lock()
        let changed = screen?.width != width || screen?.height != height
        screen = (width, height)
        screenLock.unlock()
        if changed { send("SCREEN \(width) \(height)") }
    }

    private func screenLine() -> String? {
        screenLock.lock()
        defer { screenLock.unlock() }
        guard let screen = screen else { return nil }
        return "SCREEN \(screen.width) \(screen.height)"
    }

    // MARK: - Handshake

    private func handshake(_ connection: NWConnection) {
        let id = ObjectIdentifier(connection)
        pending[id] = connection
        let nonce = Pairing.nonce()
        let reader = LineReader()
        connection.stateUpdateHandler = { [weak self] state in
            switch state {
            case .failed, .cancelled:
                self?.pending[id] = nil
            default:
                break
            }
        }
        connection.start(queue: queue)
        connection.send(content: Data("LAPTOPSCREEN 1 \(nonce)\n".utf8), completion: .idempotent)
        receiveHello(connection, reader: reader, nonce: nonce)
        /* drop connections that do not finish the handshake quickly */
        queue.asyncAfter(deadline: .now() + 5) { [weak self] in
            guard let self = self, let stale = self.pending.removeValue(forKey: id) else { return }
            stale.cancel()
        }
    }

    private func receiveHello(_ connection: NWConnection, reader: LineReader, nonce: String) {
        connection.receive(minimumIncompleteLength: 1, maximumLength: 4096) { [weak self] data, _, isComplete, error in
            guard let self = self else { return }
            var lines: [String] = []
            if let data = data { lines = reader.feed(data) }
            if let first = lines.first {
                self.verify(first, connection: connection, nonce: nonce, reader: reader, rest: Array(lines.dropFirst()))
                return
            }
            if isComplete || error != nil {
                connection.cancel()
                return
            }
            self.receiveHello(connection, reader: reader, nonce: nonce)
        }
    }

    private func verify(_ line: String, connection: NWConnection, nonce: String, reader: LineReader, rest: [String]) {
        pending[ObjectIdentifier(connection)] = nil
        let parts = line.split(separator: " ").map { String($0) }
        let code = Pairing.code
        guard parts.count == 3, parts[0] == "HELLO",
              Pairing.matches(parts[2], Pairing.hmacHex("agent|\(nonce)|\(parts[1])", code: code)) else {
            connection.send(content: Data("DENIED\n".utf8), completion: .contentProcessed({ _ in connection.cancel() }))
            return
        }
        /* the display sleeps, or the Mac is in a dark wake for maintenance: a session would end only when the Mac sleeps, */
        /* and the agent's hang-up after that would wake it again */
        if CGDisplayIsAsleep(CGMainDisplayID()) != 0 {
            connection.send(content: Data("SLEEP\n".utf8), completion: .contentProcessed({ _ in connection.cancel() }))
            return
        }
        let proof = Pairing.hmacHex("mac|\(parts[1])|\(nonce)", code: code)
        /* the screen goes with the greeting: the agent picks the display mode before it switches the display on */
        var greeting = "WELCOME \(proof) \(videoPort)\nVERSION \(AppInfo.version)\n"
        if let line = screenLine() { greeting += line + "\n" }
        connection.send(content: Data(greeting.utf8), completion: .idempotent)
        adopt(connection, reader: reader, nonces: (mac: nonce, agent: parts[1]))
        for line in rest { handleAgentLine(line) }
    }

    // MARK: - Authenticated agent

    private func adopt(_ connection: NWConnection, reader: LineReader, nonces: (mac: String, agent: String)) {
        if let old = agent, old !== connection { old.cancel() }
        agent = connection
        sessionNonces = nonces
        incomingUpdate = nil
        clipboardData = Data()
        clipboardBroken = false
        lastHeard = Date()
        hostLock.lock()
        agentHost = ControlServer.hostKey(connection.endpoint)
        hostLock.unlock()
        connection.stateUpdateHandler = { [weak self, weak connection] state in
            guard let self = self, let connection = connection else { return }
            switch state {
            case .failed, .cancelled:
                if self.agent === connection { self.dropAgent() }
            default:
                break
            }
        }
        emit(.connected)
        receiveAgent(connection, reader: reader)
    }

    private func receiveAgent(_ connection: NWConnection, reader: LineReader) {
        connection.receive(minimumIncompleteLength: 1, maximumLength: 65536) { [weak self] data, _, isComplete, error in
            guard let self = self, self.agent === connection else { return }
            if let data = data {
                for line in reader.feed(data) { self.handleAgentLine(line) }
            }
            if isComplete || error != nil {
                self.dropAgent()
                return
            }
            self.receiveAgent(connection, reader: reader)
        }
    }

    private func handleAgentLine(_ line: String) {
        lastHeard = Date()
        let parts = line.split(separator: " ", maxSplits: 1).map { String($0) }
        guard let command = parts.first else { return }
        let argument = parts.count > 1 ? parts[1] : ""
        switch command {
        case "STATE":
            if argument == "locked" {
                emit(.locked(true))
            } else if argument == "unlocked" {
                emit(.locked(false))
            } else if argument == "nodisplay" {
                emit(.noDisplay)
            }
        case "POINTER":
            let fields = argument.split(separator: " ").map { String($0) }
            if fields.first == "away" {
                emit(.pointerAway)
            } else if fields.count == 3, fields[0] == "home", let x = Int(fields[1]), let y = Int(fields[2]) {
                emit(.pointerHome(x: x, y: y))
            }
        case "SWITCH":
            /* the agent hangs up and comes back over the cable right away */
            if argument == "cable" { emit(.switchingToCable) }
        case "VERSION":
            if !argument.isEmpty { emit(.agentVersion(argument)) }
        case "CLIPBOARD":
            emit(.clipboardEnabled(argument == "on"))
        case "CLIP+", "CLIP":
            receiveClipboard(argument, last: command == "CLIP")
        case "UPDATE":
            beginUpdate(argument)
        case "D":
            continueUpdate(argument)
        case "E":
            finishUpdate()
        case "NOUPDATE":
            emit(.updateFailed("Auf dem Laptop fehlen die Dateien für den Mac (Ordner mac)."))
        default:
            break
        }
    }

    private func receiveClipboard(_ chunk: String, last: Bool) {
        if !clipboardBroken, let data = Data(base64Encoded: chunk),
           clipboardData.count + data.count <= Config.maxClipboardBytes {
            clipboardData.append(data)
        } else {
            clipboardBroken = true
            clipboardData = Data()
        }
        guard last else { return }
        if !clipboardBroken, let text = String(data: clipboardData, encoding: .utf8) {
            emit(.clipboard(text))
        }
        clipboardData = Data()
        clipboardBroken = false
    }

    private func beginUpdate(_ header: String) {
        let fields = header.split(separator: " ").map { String($0) }
        guard fields.count == 4, let size = Int(fields[1]), size > 0, size <= Config.maxUpdateBytes else {
            incomingUpdate = nil
            emit(.updateFailed("Der Laptop hat ein unbrauchbares Update geschickt."))
            return
        }
        incomingUpdate = IncomingUpdate(version: fields[0], size: size, sha256: fields[2], proof: fields[3])
    }

    private func continueUpdate(_ chunk: String) {
        guard var update = incomingUpdate else { return }
        incomingUpdate = nil  // keeps the buffer uniquely referenced while appending
        guard let data = Data(base64Encoded: chunk), update.data.count + data.count <= update.size else {
            emit(.updateFailed("Das Update kam beschädigt an."))
            return
        }
        update.data.append(data)
        incomingUpdate = update
    }

    private func finishUpdate() {
        guard let update = incomingUpdate else { return }
        incomingUpdate = nil
        let digest = Data(SHA256.hash(data: update.data)).map { String(format: "%02x", UInt32($0)) }.joined()
        guard let nonces = sessionNonces, update.data.count == update.size, Pairing.matches(digest, update.sha256),
              Pairing.matches(update.proof, Pairing.hmacHex(
                  "update|\(nonces.mac)|\(nonces.agent)|\(update.version)|\(update.sha256)", code: Pairing.code)) else {
            emit(.updateFailed("Das Update ließ sich nicht als echt bestätigen und wurde verworfen."))
            return
        }
        emit(.update(version: update.version, archive: update.data))
    }

    /*
     The Mac is about to sleep. "SLEEP" makes the agent stop its video and hang up while the Mac is still awake:
     a hang-up after that would wake the Mac again through a Bonjour sleep proxy. Blocks the caller for at most
     two seconds (the main queue may delay the sleep that long).
     */
    func goingToSleep() {
        let done = DispatchSemaphore(value: 0)
        queue.async { [weak self] () -> Void in
            guard let self = self, let agent = self.agent else {
                done.signal()
                return
            }
            self.sleepWaiter = done
            agent.send(content: Data("SLEEP\n".utf8), completion: .idempotent)
        }
        _ = done.wait(timeout: .now() + 2)
        queue.sync { () -> Void in
            self.sleepWaiter = nil
            self.dropAgent()
        }
    }

    private func dropAgent() {
        sleepWaiter?.signal()
        sleepWaiter = nil
        guard let old = agent else { return }
        agent = nil
        old.cancel()
        sessionNonces = nil
        incomingUpdate = nil
        clipboardData = Data()
        clipboardBroken = false
        hostLock.lock()
        agentHost = nil
        hostLock.unlock()
        emit(.waiting)
    }

    private func tick() {
        guard let agent = agent else { return }
        if Date().timeIntervalSince(lastHeard) > 8 {
            dropAgent()
            return
        }
        agent.send(content: Data("P\n".utf8), completion: .idempotent)
    }

    private func emit(_ event: Event) {
        DispatchQueue.main.async { [weak self] in self?.onEvent?(event) }
    }

    /* Address of an endpoint without port and interface scope, for comparing control and video peers */
    static func hostKey(_ endpoint: NWEndpoint) -> String? {
        guard case .hostPort(let host, _) = endpoint else { return nil }
        switch host {
        case .ipv4(let address):
            return address.rawValue.map { String($0) }.joined(separator: ".")
        case .ipv6(let address):
            return address.rawValue.map { String(format: "%02x", UInt32($0)) }.joined()
        case .name(let name, _):
            return name
        @unknown default:
            return nil
        }
    }
}

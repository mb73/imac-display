import AppKit
import Network

final class AppDelegate: NSObject, NSApplicationDelegate {
    private var window: NSWindow!
    private let container = ContainerView()
    private let control = ControlServer(port: Config.controlPort, videoPort: Config.videoPort)
    private let input = InputCapture()
    private let clipboard = ClipboardSync()
    private let updater = SelfUpdater()
    private var receiver: VideoReceiver?
    private var agentConnected = false
    private var laptopLocked = false
    private var dummyMissing = false
    private var videoRunning = false
    private var failure: String?
    /* shown on top of everything while an update is fetched and built */
    private var updateStatus: String?
    private var updateOfferVisible = false
    private var declinedVersion: String?
    /* how the laptop is connected in this session, and in the last one where that was known */
    private var transport: Transport?
    private var lastTransport: Transport?
    /* counts the sessions, so a late look at the way the laptop came belongs to its own session */
    private var sessions = 0
    /* the big symbol of a changed connection and since when it shows: instead of flashing the waiting screen */
    private var symbol: Transport?
    private var symbolSince: Date?
    private static let symbolMinimum: TimeInterval = 1.5
    private static let symbolMaximum: TimeInterval = 10
    /* when a running video stopped: a mere restart (lid, display change) keeps the last picture for a moment */
    private var videoStoppedAt: Date?
    private let dockTile = DockTileView()

    func applicationDidFinishLaunching(_ notification: Notification) {
        buildMenu()
        dockTile.frame = NSRect(origin: .zero, size: NSApp.dockTile.size)
        NSApp.dockTile.contentView = dockTile
        NSApp.dockTile.display()
        let frame = NSScreen.main?.frame ?? NSRect(x: 0, y: 0, width: 1600, height: 900)
        window = NSWindow(contentRect: frame, styleMask: [.titled, .closable, .miniaturizable, .resizable],
                          backing: .buffered, defer: false)
        window.title = "Laptop"
        window.collectionBehavior = [.fullScreenPrimary]
        window.backgroundColor = .black
        window.acceptsMouseMovedEvents = true
        window.contentView = container
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        perform(#selector(enterFullScreen), with: nil, afterDelay: 0.2)

        let receiver = VideoReceiver(port: Config.videoPort, displayLayer: container.videoView.displayLayer)
        let control = self.control
        receiver.isAllowed = { endpoint in control.isAgent(endpoint) }
        receiver.onState = { [weak self] state in self?.videoChanged(state) }
        receiver.onVideoSize = { [weak self] size in self?.input.videoSize = size }
        self.receiver = receiver

        control.onEvent = { [weak self] event in self?.controlChanged(event) }
        input.view = container
        input.send = { line in control.send(line) }
        input.install()
        clipboard.send = { line in control.send(line) }
        updater.onFailure = { [weak self] message in self?.updateFailed(message) }
        NotificationCenter.default.addObserver(self, selector: #selector(appResignedActive),
                                               name: NSApplication.didResignActiveNotification, object: nil)
        NotificationCenter.default.addObserver(self, selector: #selector(appBecameActive),
                                               name: NSApplication.didBecomeActiveNotification, object: nil)
        NotificationCenter.default.addObserver(self, selector: #selector(screenChanged),
                                               name: NSWindow.didChangeScreenNotification, object: window)
        NotificationCenter.default.addObserver(self, selector: #selector(screenChanged),
                                               name: NSApplication.didChangeScreenParametersNotification, object: nil)
        let workspace = NSWorkspace.shared.notificationCenter
        workspace.addObserver(self, selector: #selector(willSleep), name: NSWorkspace.willSleepNotification, object: nil)
        workspace.addObserver(self, selector: #selector(didWake), name: NSWorkspace.didWakeNotification, object: nil)
        workspace.addObserver(self, selector: #selector(screensSlept), name: NSWorkspace.screensDidSleepNotification, object: nil)
        workspace.addObserver(self, selector: #selector(screensWoke), name: NSWorkspace.screensDidWakeNotification, object: nil)
        screenChanged()
        refresh()
        receiver.start()
        control.start()
    }

    /* The laptop picks its display mode by the pixels of the screen LaptopScreen shows it on */
    @objc private func screenChanged() {
        guard let screen = window.screen ?? NSScreen.main else { return }
        let scale = screen.backingScaleFactor
        control.setScreen(width: Int((screen.frame.width * scale).rounded()),
                          height: Int((screen.frame.height * scale).rounded()))
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }

    /* gives the mouse back to the pointer if it was parked for the laptop's own display */
    func applicationWillTerminate(_ notification: Notification) {
        input.releaseAll()
    }

    @objc private func enterFullScreen() {
        if !window.styleMask.contains(.fullScreen) { window.toggleFullScreen(nil) }
    }

    @objc private func appResignedActive() {
        input.releaseAll()
        clipboard.resignedActive()
    }

    /* the user switched to the laptop: a text copied on the Mac goes along, and the Mac pointer hides over the picture */
    @objc private func appBecameActive() {
        if agentConnected { clipboard.becameActive() }
        container.hidePointer()
    }

    /* the laptop hangs up while the Mac is still awake and finds LaptopScreen again only once the display is on: */
    /* see ControlServer.goingToSleep and setAdvertised */
    @objc private func willSleep() {
        input.releaseAll()
        control.setAdvertised(false)
        control.goingToSleep()
    }

    /* a safety net in case the display's wake notification does not come; a dark wake keeps the display asleep */
    @objc private func didWake() {
        if CGDisplayIsAsleep(CGMainDisplayID()) == 0 { control.setAdvertised(true) }
    }

    @objc private func screensSlept() {
        control.setAdvertised(false)
    }

    @objc private func screensWoke() {
        control.setAdvertised(true)
    }

    @objc private func renewPairingCode() {
        Pairing.renew()
        control.disconnectAgent()
        refresh()
    }

    /* the standard About panel; below the version it names the project LaptopScreen belongs to and its license */
    @objc private func showAbout() {
        let centered = NSMutableParagraphStyle()
        centered.alignment = .center
        let font = NSFont.systemFont(ofSize: NSFont.smallSystemFontSize)
        let parts: [(String, URL?)] = [("Gehört zu ", nil), ("imac-display", Config.projectURL),
                                       ("\n\n", nil), ("MIT-Lizenz", Config.licenseURL)]
        let credits = NSMutableAttributedString()
        for (text, link) in parts {
            var attributes: [NSAttributedString.Key: Any] = [.font: font, .paragraphStyle: centered]
            /* links take the text view's link color, plain text the label color, which follows dark mode */
            if let link = link { attributes[.link] = link } else { attributes[.foregroundColor] = NSColor.labelColor }
            credits.append(NSAttributedString(string: text, attributes: attributes))
        }
        NSApp.orderFrontStandardAboutPanel(options: [.credits: credits])
    }

    private func videoChanged(_ state: VideoReceiver.State) {
        switch state {
        case .receiving: videoRunning = true
        case .waiting:
            if videoRunning {
                videoStoppedAt = Date()
                DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) { [weak self] in self?.refresh() }
            }
            videoRunning = false
        case .failed(let message): failure = message
        }
        refresh()
    }

    private func controlChanged(_ event: ControlServer.Event) {
        switch event {
        case .connected(let guess):
            agentConnected = true
            laptopLocked = false
            dummyMissing = false
            transport = nil
            sessions += 1
            /* an agent before 1.9.0 never says how it came: then the Mac's own view counts */
            let session = sessions
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) { [weak self] () -> Void in
                guard let self = self, self.sessions == session, self.agentConnected, self.transport == nil,
                      let guess = guess else { return }
                self.adoptTransport(guess)
            }
        case .via(let way):
            adoptTransport(way)
            return
        case .waiting:
            agentConnected = false
            laptopLocked = false
            dummyMissing = false
            transport = nil
            clipboard.isEnabled = false
            if !updater.isRunning { updateStatus = nil }
        case .locked(let locked):
            laptopLocked = locked
            dummyMissing = false
        case .noDisplay:
            dummyMissing = true
        case .pointerAway:
            input.pointerAway()
            return
        case .pointerHome(let x, let y):
            input.pointerHome(x: x, y: y)
            return
        case .switchingToCable:
            showSymbol(.cable)
        case .agentVersion(let version):
            offerUpdate(version)
        case .clipboardEnabled(let enabled):
            clipboard.isEnabled = enabled
            if enabled && NSApp.isActive { clipboard.becameActive() }
        case .clipboard(let text):
            clipboard.received(text)
        case .update(let version, let archive):
            updateStatus = "LaptopScreen wird auf \(version) aktualisiert …\n\n"
                + "Das dauert etwa eine Minute, danach startet LaptopScreen neu."
            updater.install(archive)
        case .updateFailed(let message):
            updateFailed(message)
        case .failed(let message):
            failure = message
        }
        refresh()
    }

    /* The laptop carries a newer LaptopScreen: ask once per version and run */
    private func offerUpdate(_ version: String) {
        guard AppInfo.compare(version, AppInfo.version) > 0, version != declinedVersion,
              !updateOfferVisible, !updater.isRunning, updateStatus == nil else { return }
        updateOfferVisible = true
        let alert = NSAlert()
        alert.messageText = "Neue Version von LaptopScreen"
        alert.informativeText = "Der Laptop bringt LaptopScreen \(version) mit, auf diesem Mac ist \(AppInfo.version) installiert.\n\n"
            + "Beim Aktualisieren baut der Mac die neue Version (etwa eine Minute) und startet LaptopScreen dann neu."
        alert.addButton(withTitle: "Aktualisieren")
        alert.addButton(withTitle: "Später")
        _ = NSApp.requestUserAttention(.informationalRequest)
        alert.beginSheetModal(for: window) { [weak self] (response: NSApplication.ModalResponse) in
            guard let self = self else { return }
            self.updateOfferVisible = false
            if response == .alertFirstButtonReturn {
                if self.agentConnected {
                    self.updateStatus = "Hole LaptopScreen \(version) vom Laptop …"
                    self.control.requestUpdate()
                }
            } else {
                self.declinedVersion = version
            }
            self.refresh()
        }
        refresh()
    }

    private func updateFailed(_ message: String) {
        updateStatus = nil
        refresh()
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = "Aktualisierung fehlgeschlagen"
        alert.informativeText = message + "\n\nLaptopScreen läuft in der bisherigen Version weiter."
        alert.addButton(withTitle: "OK")
        alert.beginSheetModal(for: window, completionHandler: nil)
    }

    private func refresh() {
        let restarting = agentConnected && !videoRunning
            && (videoStoppedAt.map { Date().timeIntervalSince($0) < 1.5 } ?? false)
        let showing = agentConnected && (videoRunning || restarting) && !laptopLocked && !dummyMissing
        let controlling = showing && !updateOfferVisible
        input.isEnabled = controlling
        container.controlling = controlling
        dockTile.light = light(showing: showing)
        if let status = updateStatus {
            container.symbol.hide()
            container.overlay.isHidden = false
            container.overlay.text = status
            return
        }
        if let shown = symbol, let since = symbolSince {
            let elapsed = Date().timeIntervalSince(since)
            /* at least a moment, and while the picture is still coming, but not for ever */
            let coming = failure == nil && (!agentConnected || (!videoRunning && !laptopLocked && !dummyMissing))
            if elapsed < AppDelegate.symbolMinimum || (coming && elapsed < AppDelegate.symbolMaximum) {
                container.overlay.isHidden = true
                container.symbol.show(shown)
                if controlling { container.scheduleCursorHide() }
                return
            }
            symbol = nil
            symbolSince = nil
        }
        container.symbol.hide()
        if showing {
            container.overlay.isHidden = true
            if controlling { container.scheduleCursorHide() }
            return
        }
        container.overlay.isHidden = false
        if let failure = failure {
            container.overlay.text = failure
        } else if !agentConnected {
            let addresses = localIPv4Addresses()
            var text = "Warte auf den Laptop …\n\nKopplungscode: \(Pairing.code)\n\n"
            text += "Auf dem Laptop imac-display.exe starten.\nDieser Mac ist erreichbar unter:\n"
            text += addresses.isEmpty ? "(keine Netzwerkadresse)" : addresses.joined(separator: "\n")
            text += "\n\nLaptopScreen \(AppInfo.version)"
            container.overlay.text = text
        } else if dummyMissing {
            container.overlay.text = "Am Laptop steckt kein HDMI-Dummy-Stecker.\n\n"
                + "Bitte in den HDMI-Anschluss des Laptops stecken – das Bild kommt dann von selbst."
        } else if laptopLocked {
            container.overlay.text = "Der Laptop ist gesperrt.\n\nBitte direkt am Laptop entsperren.\n\n"
                + "Bleibt sein Bildschirm beim Aufklappen schwarz, den HDMI-Dummy-Stecker kurz ziehen: "
                + "Dann zeigt Windows die Anmeldung auf dem Laptop. Danach wieder einstecken."
        } else {
            container.overlay.text = "Laptop verbunden – das Bild kommt gleich …"
        }
    }

    /* The way the laptop came in this session; a change since the last session shows its symbol for a moment */
    private func adoptTransport(_ way: Transport) {
        guard transport != way else { return }
        transport = way
        if let last = lastTransport, last != way {
            showSymbol(way)
        } else if let shown = symbol, shown != way {
            /* the cable was announced, but the laptop came over Wi-Fi again */
            showSymbol(way)
        }
        lastTransport = way
        refresh()
    }

    private func showSymbol(_ way: Transport) {
        if symbol != way { symbolSince = Date() }
        symbol = way
        /* look again when it may go: after the minimum, and at the latest */
        DispatchQueue.main.asyncAfter(deadline: .now() + AppDelegate.symbolMinimum) { [weak self] in self?.refresh() }
        DispatchQueue.main.asyncAfter(deadline: .now() + AppDelegate.symbolMaximum) { [weak self] in self?.refresh() }
    }

    /* The light of the laptop's taskbar button, for the Dock icon */
    private func light(showing: Bool) -> Light {
        if failure != nil || (agentConnected && dummyMissing) { return .problem }
        if updateStatus != nil || !agentConnected { return .busy }
        if laptopLocked { return .paused }
        if showing { return transport == .wifi ? .onWifi : .on }
        return .busy
    }

    private func buildMenu() {
        let mainMenu = NSMenu()
        let appItem = NSMenuItem()
        mainMenu.addItem(appItem)
        let appMenu = NSMenu()
        let about = appMenu.addItem(withTitle: "Über LaptopScreen", action: #selector(showAbout), keyEquivalent: "")
        about.target = self
        appMenu.addItem(NSMenuItem.separator())
        let fullScreen = appMenu.addItem(withTitle: "Vollbild ein/aus",
                                         action: #selector(NSWindow.toggleFullScreen(_:)), keyEquivalent: "f")
        fullScreen.keyEquivalentModifierMask = [.command, .control]
        let renew = appMenu.addItem(withTitle: "Neuen Kopplungscode erzeugen",
                                    action: #selector(renewPairingCode), keyEquivalent: "")
        renew.target = self
        appMenu.addItem(NSMenuItem.separator())
        appMenu.addItem(withTitle: "LaptopScreen beenden",
                        action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        appItem.submenu = appMenu
        NSApp.mainMenu = mainMenu
    }
}

/* Lists the Mac's IPv4 addresses (with interface names) for the waiting screen */
func localIPv4Addresses() -> [String] {
    var result: [String] = []
    var list: UnsafeMutablePointer<ifaddrs>?
    guard getifaddrs(&list) == 0, let first = list else { return result }
    defer { freeifaddrs(list) }
    for pointer in sequence(first: first, next: { $0.pointee.ifa_next }) {
        let entry = pointer.pointee
        guard let address = entry.ifa_addr, address.pointee.sa_family == UInt8(AF_INET) else { continue }
        let flags = Int32(entry.ifa_flags)
        guard flags & IFF_UP != 0, flags & IFF_LOOPBACK == 0 else { continue }
        var host = [CChar](repeating: 0, count: Int(NI_MAXHOST))
        if getnameinfo(address, socklen_t(address.pointee.sa_len), &host, socklen_t(host.count),
                       nil, 0, NI_NUMERICHOST) == 0 {
            result.append("\(String(cString: host))  (\(String(cString: entry.ifa_name)))")
        }
    }
    return result
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.setActivationPolicy(.regular)
app.run()

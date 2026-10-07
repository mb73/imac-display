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
    /* the laptop announced its switch to the cable: say so instead of flashing the waiting screen */
    private var switchingSince: Date?
    /* when a running video stopped: a mere restart (lid, display change) keeps the last picture for a moment */
    private var videoStoppedAt: Date?

    func applicationDidFinishLaunching(_ notification: Notification) {
        buildMenu()
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

    /* the user switched to the laptop: a text copied on the Mac goes along */
    @objc private func appBecameActive() {
        if agentConnected { clipboard.becameActive() }
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
        case .connected:
            agentConnected = true
            laptopLocked = false
            dummyMissing = false
        case .waiting:
            agentConnected = false
            laptopLocked = false
            dummyMissing = false
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
            switchingSince = Date()
            /* visible for at least a second, and at most ten if the laptop does not come back */
            DispatchQueue.main.asyncAfter(deadline: .now() + 1) { [weak self] in self?.refresh() }
            DispatchQueue.main.asyncAfter(deadline: .now() + 10) { [weak self] in self?.refresh() }
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
        if let status = updateStatus {
            container.overlay.isHidden = false
            container.overlay.text = status
            return
        }
        if let since = switchingSince {
            let elapsed = Date().timeIntervalSince(since)
            if elapsed < 10 && (elapsed < 1 || !showing) {
                container.overlay.isHidden = false
                container.overlay.text = "Wechsle vom WLAN aufs Kabel …\n\nDas Bild ist gleich wieder da."
                return
            }
            switchingSince = nil
        }
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

    private func buildMenu() {
        let mainMenu = NSMenu()
        let appItem = NSMenuItem()
        mainMenu.addItem(appItem)
        let appMenu = NSMenu()
        appMenu.addItem(withTitle: "Über LaptopScreen",
                        action: #selector(NSApplication.orderFrontStandardAboutPanel(_:)), keyEquivalent: "")
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

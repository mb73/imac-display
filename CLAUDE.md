# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Worum es geht

Der Mac (erprobt: iMac Retina 5K 2020, macOS 15) wird zum zusätzlichen Bildschirm eines Firmen-Laptops (erprobt: Dell Pro 16, Windows 11, **ohne Adminrechte**). Ein HDMI-Dummy-Stecker liefert Windows einen echten 4K-Bildschirm; `imac-display.exe` nimmt ihn auf und schickt ihn an die Mac-App **LaptopScreen**, die das Bild im Vollbild zeigt und Tastatur/Maus zurückschickt.

## Rahmenbedingungen

- **Laptop ohne Adminrechte:** keine Treiber, Dienste, Firewall-Regeln, kein RDP. Eingehende Verbindungen kann ein Standardbenutzer in der Windows-Firewall nicht freigeben – der Laptop baut deshalb **nur ausgehende** Verbindungen zum Mac auf. Voraussetzung ist, dass portable Programme laufen dürfen (keine Sperre per AppLocker oder WDAC).
- **Kein Zugriff auf den Mac:** Einen SSH-Schlüssel für Claude zu hinterlegen, hat der Auto-Mode-Classifier als „Unauthorized Persistence“ blockiert. Mac-Schritte führt der Benutzer aus; Dateien kommen über den mpv-Trick hinüber (siehe unten).
- **Gesperrtes Windows:** Anmeldung und Sperrbildschirm erscheinen nur auf dem **Hauptbildschirm**, die übrigen bleiben leer (nicht einstellbar, Microsoft Q&A). Bis 1.3.0 war der Dummy Hauptbildschirm; dann sah man die Anmeldung nicht, Deckel offen oder zu – und die Übertragung ruht, weil ddagrab den Sperr-Desktop nicht aufnehmen kann. Umschalten geht dann auch nicht: `SetDisplayConfig` liefert `ERROR_ACCESS_DENIED` (nur der Sperr-Desktop darf), Win+P geht dort nicht. Seit 1.4.0 ist deshalb bei offenem Deckel das Panel Hauptbildschirm (`--mac-primary` für den Dummy); beim Aufklappen eines gesperrten Laptops stellt Windows die gespeicherte Anordnung samt Hauptbildschirm selbst her, die Anmeldung erscheint auf dem Panel (vom Benutzer am 2026-10-06 mit `--internal-primary` bestätigt). Weitere Auswege: Dummy-Stecker kurz ziehen (LaptopScreen und README nennen das), Windows Hello per Gesicht (der Dell hat eine IR-Kamera), PIN blind tippen.
- **Anzeige-Umschaltung ist für den Benutzer sichtbar.** `--test` ist rein lesend; echte Läufe nur mit dem Benutzer. Der Dummy ist ein unsichtbarer Bildschirm: Fenster dort sieht man nur, solange LaptopScreen läuft.
- **Die exe im Projektordner ist die, die der Benutzer startet.** Eine laufende exe lässt sich nicht überschreiben, wohl aber umbenennen: `windows\build.cmd` schiebt sie deshalb nach `imac-display.exe.old` (ignoriert) und baut daneben neu; die laufende Instanz arbeitet unverändert weiter, der nächste Start nimmt die neue.
- **Hintergrund-Schleifen:** `TaskStop` beendet bei Git-Bash-Konstrukten wie `timeout … bash -c 'until …'` nur die Hülle – danach die Prozessliste prüfen (`Get-CimInstance Win32_Process`). Der Agent selbst hält ffmpeg in einem Kill-on-close-Job-Objekt.
- **HEVC ist auf dem Dell gesperrt** (`hevc_qsv`, `hevc_d3d12va`, `hevc_mf` scheitern alle; Dell/HP haben HEVC 2025 aus Lizenzgründen abgeschaltet). Darum H.264 und höchstens 4096 Pixel Breite, also 4K statt 5K.
- **Testen ohne echte Sitzung:** Ein zweites `imac-display.exe` startet nicht, solange eines läuft (Mutex `Local\imac-display`; es holt nur dessen Fenster nach vorne). Fenster und Updater testet man deshalb mit einer Test-Hülle im Scratchpad: `windows\src\*.cs` plus eine eigene Klasse mit `Main`, gebaut mit `/main:<Klasse>`, demselben Manifest und einer Kopie von `imac-display.exe.config`. Per Reflection `Program.stopping = true` setzen (der Agent endet sofort, verbindet nichts, schaltet keine Anzeige) und `Program.LogPath` umbiegen; Fenster bei `StartPosition = Manual` weit außerhalb des Bildschirms zeigen und mit `PrintWindow(…, 2)` als PNG abfotografieren; DPI-Wechsel per `WM_DPICHANGED` an das Fenster simulieren; den Dark Mode über `Theme.Create(true, false)` erzwingen. **Unbedingt** `Application.ThreadException` abfangen: Sonst zeigt WinForms bei einem Fehler seinen eigenen Dialog mitten auf dem Bildschirm des Benutzers (so geschehen). `MacUpdate`, `ClipboardSync` und Co. lassen sich auch per `[Reflection.Assembly]::LoadFile('…\imac-display.exe')` aus PowerShell aufrufen. Achtung: `settings.txt`, Kopplungscode und Zwischenablage sind die echten.

## Kommandos

```powershell
# build: compiles windows\src\*.cs into .\imac-display.exe (C# 5 compiler of .NET Framework 4.x)
windows\build.cmd

# read-only diagnostics: displays + ddagrab index, dummy plug, lock, lid, ffmpeg, Quick Sync, Bonjour discovery
# (a window program: only redirected output is written, otherwise it shows a window; UTF-8)
[Console]::OutputEncoding = [Text.Encoding]::UTF8; .\imac-display.exe --test | Out-String

# emergency: laptop panel only
.\imac-display.exe --restore

# open the window and look for a newer version right away, or install the given release zip; refuses in a git checkout
.\imac-display.exe --update [zip]

# share the clipboard or not (remembered in settings.txt)
.\imac-display.exe --clipboard off

# redraw windows\imac-display.ico and mac\AppIcon.icns (both checked in)
powershell -File windows\dev\make-icons.ps1

# log (UTF-8, rotated to imac-display.log.old above 1 MB)
Get-Content "$env:LOCALAPPDATA\imac-display\imac-display.log" -Encoding UTF8 -Tail 40

# sender latency on the laptop alone (clock -> capture -> encode -> ffplay); see the script header
powershell -File windows\dev\latency-probe.ps1
```

```sh
# on the Mac: build and start in place
cd mac && sh build.sh && open LaptopScreen.app

# on the Mac: first installation (Command Line Tools if missing, build, copy to /Applications, start);
# for users: double-click mac/install.command (Gatekeeper blocks it once: Privacy & Security -> Open Anyway)
sh mac/install.sh
```

Tests gibt es nicht; geprüft wird über Build, `--test`, das Log und die Latenzmessung.

## Architektur

**Windows-Agent (`windows/src`, C# 5):**

- Ein Fensterprogramm (`/target:winexe`, WinForms). `--test` und `--restore` laufen ohne Hauptfenster: Ausgabe auf stdout nur, wenn sie umgeleitet ist (`GetFileType`: Pipe oder Datei), sonst ein Fenster bzw. eine MessageBox.
- `Program.cs` – Optionen, gemerkte Einstellungen (`%APPDATA%\imac-display\settings.txt`, Zeilen `key=value`: `scale`, `clipboard`), Kopplungscode (`pairing.txt`), Sperr-Erkennung über den Namen des Eingabe-Desktops. `Main`: Erkennung „läuft aus einer Zip“, eine Instanz pro Benutzer (Mutex `Local\imac-display`, ein zweiter Start holt das Fenster nach vorne), dann `MainWindow`. Der Agent läuft auf eigenem Thread: ffmpeg, Kopplungscode, Mac suchen → Handshake → `RunSession`; er meldet sich per `ShowStatus` und fragt über das Fenster (`Ask`, `AskCode`). `paused` („Verbindung trennen“) beendet die Sitzung und schaltet die Anzeige sofort zurück, `stopping` beendet das Programm. Die Session meldet Version und Zwischenablage-Einstellung (auch später, wenn der Schalter wechselt), wartet notfalls auf den Dummy (`STATE nodisplay`), konfiguriert die Anzeige, startet die Aufnahme, setzt Eingaben um und überwacht jede Sekunde Sperre, Deckel und Anzeigeanordnung. Nach dem Ende einer Sitzung bleibt der Dummy 15 s aktiv (LaptopScreen startet z. B. nach einem Update neu); `Configure` lässt eine noch passende Anzeige unangetastet. Ein Absturz (`UnhandledException`) schaltet die Anzeige noch zurück.
- `MainWindow.cs` – Status (Punkt, Überschrift, Detail, `ThinBar`), Update-Banner, Zwischenablage-Schalter, „Verbindung trennen“/„Verbinden“, Links „Anleitung“ (README auf GitHub) und „Log“. Badge und Fortschritt am Taskleisten-Symbol (`Taskbar.cs`, `ITaskbarList3`, nach jeder Nachricht `TaskbarButtonCreated` neu; scheitert die Anbindung, steht der Grund im Log). Alle 5 s ein Blick in „Downloads“, beim Start und alle 6 h auf GitHub. Bei zugeklapptem Deckel fragen Trennen und Schließen nach.
- `Dialogs.cs` – Log-Fenster (Zeilen dieses Laufs live, „Diagnose“ = `--test` ins Log, „Logdatei öffnen“), Kopplungscode-Dialog, Textdialog (Update-Hinweise, `--test` ohne Umleitung).
- `Theme.cs` – Hell/Dunkel nach `AppsUseLightTheme`, bei hohem Kontrast Systemfarben; dunkle Titelleiste per `DwmSetWindowAttribute` (20, sonst 19), Scrollbalken per `SetWindowTheme("DarkMode_Explorer")`, Buttons flach. Umschalten im Betrieb über `WM_SETTINGCHANGE`. `ThemedForm` ist die Basis aller Fenster: Layout in 96-DPI-Einheiten mit `AutoScaleMode.Dpi` wie Designer-Code (`EndLayout`), AutoSize-Buttons und -Panels auf `GrowAndShrink` (sonst messen sie Text schon in Bildschirm-DPI und werden doppelt skaliert), abgeleitete Schriften in `UpdateFonts` (nie die alte `Dispose`n: Ein Steuerelement behält bei gleicher neuer Schrift die alte). WinForms skaliert ein Fenster nur beim Monitorwechsel; öffnet es auf einem Monitor mit anderer DPI als der System-DPI (bei der Anmeldung festgelegt), spielt `OnLoad` den fehlenden `WM_DPICHANGED` nach. `Controls.cs` – Statuspunkt, Badges, `ThinBar`, `ThemedCheckBox` (WinForms zeichnet Checkboxen nur hell).
- Per-Monitor-V2 in WinForms braucht alles zusammen: `imac-display.exe.config` (`DpiAwareness`), das Manifest `windows\imac-display.manifest` (Windows-10-Kompatibilität, sonst meldet Windows 6.2) und `TargetFramework` 4.8 (in der temporären AssemblyInfo von `build.cmd`; schaltet auch TLS 1.2 frei).
- `Setup.cs` – Erststart: ffmpeg selbst laden (Version und SHA-256 dort gepinnt, GitHub-Spiegel von gyan.dev; Fortschritt im Fenster, entpackt über `tools\.unpack`, das `FindFfmpeg` überspringt), Quick-Sync-Probe (drei kleine Bilder mit `h264_qsv`), Startmenü-Eintrag über `WScript.Shell`.
- `Updater.cs` – `Updater`: installierte Version aus `VERSION`, aktuelle von `raw.githubusercontent.com/…/VERSION`, Zip im Downloads-Ordner (`SHGetKnownFolderPath`, jede Datei nur einmal geöffnet), Download nach `%TEMP%` (`Web.cs`: System-Proxy, TLS 1.2+), entpacken nach `.update\`, Dateien ersetzen (exe umbenennen statt überschreiben), Quellordner spiegeln, Neustart. Während einer Sitzung übergibt der Neustart die Anzeige (`IMAC_DISPLAY_UPDATED=display`): Der neue Prozess hält den Dummy 30 s, bis der Mac wieder verbunden ist. `MacUpdate`: packt `VERSION`, `CHANGELOG.md` und `mac\` für LaptopScreen.
- `ClipboardSync.cs` – Text-Zwischenablage: `GetClipboardSequenceNumber` pollen (gesendet wird nur, solange der Mac `FOCUS 1` meldet, und bis 3 s danach), lesen/schreiben über WinForms auf einem STA-Thread, eigene Änderungen nicht zurückschicken, Formate von Passwort-Managern (`ExcludeClipboardContentFromMonitorProcessing`, `Clipboard Viewer Ignore`) auslassen.
- `Displays.cs` – CCD-API (`QueryDisplayConfig`/`SetDisplayConfig`), `ChangeDisplaySettingsEx`, undokumentierte DPI-Abfrage/-Setzung (`DisplayConfigGet/SetDeviceInfo` mit Typ −3/−4) und der DXGI-Ausgangsindex, den `ddagrab` braucht. Ob der Dummy steckt, zeigt `QDC_ALL_PATHS` auch bei inaktivem Bildschirm (`targetAvailable`, gemessen: HDMI-Ziel mit Technologie 5).
- `Discovery.cs` – mDNS-Browser für `_laptopscreen._tcp` mit Legacy-Unicast-Queries (Antworten per Unicast, kein Konflikt mit dem Windows-mDNS auf UDP 5353). Link-Local-Adressen (Kabel) zuerst.
- `ControlClient.cs` – Steuerkanal und Handshake; merkt sich die Nonces für die Update-Signatur. `Injector.cs` – `SendInput`. `VideoSender.cs` – ffmpeg mit Neustart-Schleife. `LidWatcher.cs` – `GUID_LIDSWITCH_STATE_CHANGE` über ein Message-only-Fenster.
- Deckel zu → `SDC_TOPOLOGY_EXTERNAL` (falls Windows nicht schon selbst umgeschaltet hat), offen → Erweitern mit dem Panel als Hauptbildschirm (`--mac-primary`: Dummy; `Configure` prüft, wer bei (0,0) liegt). Die Anordnung bestimmt Windows: `SDC_TOPOLOGY_EXTEND` holt die zuletzt gespeicherte des erweiterten Desktops (die aus Einstellungen → Anzeige), und `Arrange` setzt nur Auflösung und Hauptbildschirm, ohne Panel und Dummy gegeneinander zu verschieben; ändert der Dummy seine Größe, bleibt seine dem Panel zugewandte Kante stehen. Auch `EnsureMode` geht bei erweitertem Desktop über `Arrange`. Setzt Windows Auflösung/Skalierung zurück (beim Zuklappen: 1920×1200 bei 100 %), wird der Wunschwert wiederhergestellt; eine reine Skalierungsänderung mehr als 10 s nach einem Deckelwechsel gilt als Wahl des Benutzers und wird gemerkt.

**Mac-App (`mac/Sources`, Swift):**

- `ControlServer.swift` – `NWListener` mit Bonjour, Handshake, zeilenbasiertes Protokoll, Ping alle 2 s, Trennung nach 8 s Stille.
- `VideoReceiver.swift` + `FLVParser.swift` + `H264.swift` – FLV über TCP, AVCC-Rahmen ohne In-Band-SPS/PPS, `AVSampleBufferDisplayLayer` mit `DisplayImmediately`. Nimmt Video nur von der IP des gekoppelten Agenten an.
- `InputCapture.swift` + `KeyMap.swift` – lokale Event-Monitore. Text als Unicode, Kürzel als Zeichen (Agent mappt per `VkKeyScanEx`), Sondertasten als VK mit Mac-Navigationssemantik.
- `ClipboardSync.swift` – Text-Zwischenablage, die dem Fokus folgt: beim Aktivieren von LaptopScreen geht ein neuer Mac-Text an den Laptop (`changeCount`), Laptop-Text wird nur übernommen, solange LaptopScreen vorne ist oder es bis vor 3 s war. Typen `org.nspasteboard.ConcealedType`/`TransientType`/`AutoGeneratedType` werden nie gesendet.
- `Updater.swift` – `AppInfo` (Version aus der `Info.plist`, Versionsvergleich) und `SelfUpdater`: Zip nach `$TMPDIR/LaptopScreen-update`, `ditto -x -k`, `sh mac/build.sh`, altes Bundle beiseite, neues hineinkopieren, per `sh -c 'sleep 2; open …'` neu starten.
- `Pairing.swift` (Code in `UserDefaults`, HMAC via CryptoKit), `Views.swift`, `main.swift` (Zustände, Overlay, Update-Dialog als Sheet, Menü mit „Über LaptopScreen“).

**Protokoll** (Zeilen mit `\n`, nur ASCII):

- Ports: 47100 Video (FLV/H.264), 47101 Steuerung (Bonjour `_laptopscreen._tcp`).
- Handshake: Mac `LAPTOPSCREEN 1 <nonceMac>` → Agent `HELLO <nonceAgent> <hmac(code, "agent|<nonceMac>|<nonceAgent>")>` → Mac `WELCOME <hmac(code, "mac|<nonceAgent>|<nonceMac>")> <videoPort>` oder `DENIED`. Schlüssel: Code in Großbuchstaben ohne Trennzeichen, UTF-8; Hex klein.
- Direkt nach dem Handshake: beide Seiten `VERSION <semver>`, der Agent zusätzlich `CLIPBOARD on|off`. Ältere Gegenstellen ignorieren unbekannte Zeilen; LaptopScreen vor 1.2.0 sendet keine Version, das meldet der Agent einmal im Log.
- Mac → Agent: `M x y` (0..65535 über den Bildschirm), `B button down x y mods`, `W dy dx mods`, `K vk down mods ext`, `C codepoint down mods`, `T utf16hex`, `S mods`, `R`, `P`, `GETUPDATE`, `FOCUS 1|0` (LaptopScreen vorne oder nicht; nur dann bzw. bis 3 s danach schickt der Agent seine Zwischenablage). Modifier-Bits: 1 Shift, 2 Strg, 4 Alt, 8 Win. Alt/Win drückt der Agent nur zusammen mit einer Taste oder einem Klick, nie allein (sonst Menüleiste bzw. Startmenü).
- Agent → Mac: `P`, `STATE locked`, `STATE unlocked`, `STATE nodisplay`; auf `GETUPDATE` `UPDATE <version> <bytes> <sha256> <proof>`, Zeilen `D <base64>` (je 16 KiB) und `E`, oder `NOUPDATE`. `proof = hmac(code, "update|<nonceMac>|<nonceAgent>|<version>|<sha256>")` bindet das Update an die Sitzung.
- Beide Richtungen: `CLIP+ <base64>` (weitere Stücke folgen) und `CLIP <base64>` (letztes Stück), UTF-8 mit `\n`, je höchstens 32 KiB; der Agent wandelt Zeilenenden für Windows um. Beide Seiten puffern Zeilen nur bis 64 KiB.

**Videopipeline** (in `Program.FfmpegArguments`): `ddagrab` → `hwmap=derive_device=qsv` → `vpp_qsv=format=nv12:async_depth=1` → `h264_qsv -low_power 1 -async_depth 1 -bf 0 -scenario displayremoting` → FLV über TCP mit `tcp_nodelay`. Messwerte und Gründe:

- `vpp_qsv` hat standardmäßig `async_depth=4`; 1 spart ~50 ms (lokal 155 → 105 ms pro Messdurchlauf).
- `-low_power 0` wird auf dieser GPU nicht unterstützt, und `-low_delay_brc 1` ignoriert sie (`LowDelayBRC: OFF` im Verbose-Log).
- `-bufsize` begrenzt die Größe jedes einzelnen Bilds (QSV: `BufferSizeInKB` × `BRCParamMultiplier`). Mit 2M waren das 334 KB; 4K-Schlüsselbilder (0,7–0,9 MB) wurden abgeschnitten, und der untere Bildrand zeigte Müll, bis sich dort etwas änderte (behoben in 1.1.0, jetzt 16M). Prüfen lässt sich das am Laptop allein: kurz mit denselben Argumenten in eine `.flv` aufzeichnen und `ffmpeg -v error -i aufnahme.flv -f null -` laufen lassen. Meldet der Software-Decoder „error while decoding MB …“, ist der Stream schon beim Encoder kaputt.
- FLV statt MPEG-TS: Der TS-Demuxer gibt ein Bild erst beim nächsten PES-Kopf frei.
- mpv als Empfänger staut beim Start ~1 s und baut den Stau bei gleicher Bild- und Bildwiederholrate nie ab; deshalb die eigene App.

## Update-Mechanismus

Der Laptop ist die Update-Zentrale, der Mac braucht weder GitHub noch das Terminal:

1. **Laptop:** Der Browser lädt das Zip-Archiv des Branches `main` von GitHub („Code“ → „Download ZIP“, `Updater.DownloadUrl`). `imac-display.exe` sucht beim Start in „Downloads“ nach `imac-display*.zip` mit höherer `VERSION` und bietet sie an; `update.cmd` (= `--update`) öffnet zusätzlich den Download und wartet bis zu 10 min auf die Datei. Installiert wird alles unter dem Ordner, der `VERSION` und `imac-display.exe` enthält (Name egal); `tools\`, `.git` und `.update` werden nie angefasst, `mac\Sources`, `windows\src` und `windows\dev` gespiegelt (eine entfallene Swift-Datei würde der Mac sonst mitbauen). Danach startet das Programm im selben Konsolenfenster neu (`IMAC_DISPLAY_UPDATED` verhindert ein zweites Angebot). In einem Git-Arbeitsverzeichnis verweigert sich der Updater.
2. **Mac:** Meldet der Agent eine höhere `VERSION`, fragt LaptopScreen per Sheet nach (einmal pro Version und Programmlauf), schickt `GETUPDATE`, prüft Größe, SHA-256 und `proof`, baut mit `build.sh` und tauscht das eigene Bundle aus. Schlägt etwas fehl, läuft die alte Version weiter, und ein Dialog zeigt die letzten Zeilen der Ausgabe. Weil das neue Bundle eine neue Ad-hoc-Signatur hat, fragt macOS womöglich erneut nach Netzwerkzugriff.

`update.cmd` steht absichtlich in einer einzigen Zeile mit `& exit /b`: Die Aktualisierung ersetzt die Datei, während `cmd` sie noch zeilenweise liest.

## Deployment auf den Mac während der Entwicklung

Die macOS-Firewall lässt mpv durch, `nc` nicht. Der Benutzer startet auf dem Mac:

```sh
cd ~/LaptopScreen && /Applications/mpv.app/Contents/MacOS/mpv --no-config --stream-dump=laptopscreen.tgz "tcp://0.0.0.0:50030?listen" && tar -xzf laptopscreen.tgz && sh build.sh && { pkill -x LaptopScreen; sleep 1; open LaptopScreen.app; }
```

Auf dem Laptop dann `powershell -File windows\dev\send-to-mac.ps1`: Es packt `build.sh install.sh Info.plist AppIcon.icns Sources`, findet den Mac über `--test` (Kabel zuerst; sonst `-Mac <Adresse>`) und versucht jede Sekunde zu senden, bis mpv lauscht. **Keine Probe-Verbindung vorher:** mpv nimmt genau eine Verbindung an und beendet sich danach. Ohne `VERSION` im Elternordner baut `build.sh` die Version 0.0.0 – LaptopScreen bietet dann sofort das Update vom Laptop an, was sich zum Testen des Selbst-Updates nutzen (oder mit „Später“ übergehen) lässt.

## Konventionen

- **Das Repo ist öffentlich** (GitHub `mb73/imac-display`, MIT-Lizenz): nichts Firmenspezifisches einchecken, also keine internen Hostnamen, Firmennamen oder Sicherheitseinstellungen eines bestimmten Arbeitgebers.
- **C# 5:** kein `$"…"`, kein `?.`, keine `=>`-Member, kein `nameof`. Quelltexte UTF-8 ohne BOM, deshalb `/codepage:65001` im Build.
- **Swift wie Swift 5.3/5.4 und macOS-11-SDK:** Der Compiler auf dem iMac des Benutzers ist älter als Swift 5.5. Also kein `@MainActor`, keine `Task`, kein `async`/`await`, kein `sampleBufferRenderer`; mehrzeilige Closures mit expliziter Signatur; `NWListener.Service(type:)` statt der mehrdeutigen Überladung mit `txtRecord: nil`.
- Kommentare auf Englisch, eigenständige Kommentarzeilen als `/* … */`; Meldungen an den Benutzer auf Deutsch.
- `.sh`, `.command`, `.swift`, `.plist` mit LF, `.cmd` und `.ps1` mit CRLF (`.gitattributes`). `mac/install.command` braucht im Repo das Ausführungsrecht (Modus 100755), sonst startet der Doppelklick nicht; Windows kennt es nicht, deshalb `git update-index --chmod=+x`. `.ps1` mit Umlauten brauchen ein UTF-8-BOM, sonst liest Windows PowerShell 5.1 sie als ANSI.
- Die gebaute `imac-display.exe` wird mit eingecheckt (samt `imac-display.exe.config`), damit niemand selbst bauen muss; ffmpeg nicht (lädt der erste Start, Version und SHA-256 in `Setup.cs` gepinnt). Ebenfalls eingecheckt: die Icons `windows\imac-display.ico` und `mac\AppIcon.icns` aus `windows\dev\make-icons.ps1`.
- `build.cmd` erzeugt Titel und Dateiversion der exe aus `VERSION` (temporäre AssemblyInfo), damit Taskleiste und Explorer „iMac-Display“ zeigen.

## Versionierung

`VERSION` (SemVer) und `CHANGELOG.md` (deutsch, Keep a Changelog) gemeinsam pflegen; der Commit-Betreff ist die Versionsnummer. `mac/build.sh` schreibt die Version aus `VERSION` in die `Info.plist` der gebauten App. **`VERSION` steuert die Updates:** Nur eine höhere Nummer bietet der Laptop zur Installation an und LaptopScreen zur Aktualisierung; der Updater zeigt die CHANGELOG-Abschnitte oberhalb der installierten Version.

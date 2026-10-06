# Changelog

Alle nennenswerten Änderungen dieses Projekts. Datumsformat: JJJJ-MM-TT.

## [1.2.0] – 2026-10-06

Erste öffentliche Version, unter der MIT-Lizenz auf GitHub.

### Hinzugefügt

- **Aktualisieren ohne Handarbeit.**
  - Laptop: Liegt eine neuere imac-display-Zip im Downloads-Ordner (etwa frisch von GitHub), bietet `imac-display.exe` beim Start an, sie zu installieren, und startet danach neu. `update.cmd` öffnet den Download im Browser und installiert ihn, sobald er da ist; eine Zip-Datei lässt sich auch auf `update.cmd` ziehen. Kopplungscode, Einstellungen und ffmpeg bleiben erhalten.
  - Mac: Bringt der Laptop eine neuere Version mit, fragt LaptopScreen, ob es sich aktualisieren soll, holt die Quelltexte über die gekoppelte Verbindung, baut sie und startet neu. Der Mac braucht dafür weder GitHub noch das Terminal. Das Update ist mit dem Kopplungscode signiert und an die laufende Sitzung gebunden (HMAC-SHA256).
  - Beide Seiten nennen sich nach dem Verbinden ihre Version; das Log meldet, welche Seite älter ist.
- **Geteilte Zwischenablage für Text.** Sie folgt dem Fokus: Wer zu LaptopScreen wechselt, nimmt den auf dem Mac kopierten Text mit, und was auf dem Laptop kopiert wird, landet in der Zwischenablage des Macs. Der Laptop schickt seine Zwischenablage nur, solange LaptopScreen vorne ist (oder bis 3 s danach); was Passwort-Manager als vertraulich markieren, wird nie übertragen. Abschalten mit `imac-display.exe --clipboard off` (wieder an mit `--clipboard on`); die Wahl wird gemerkt.
- **Leichterer Einstieg:**
  - `mac/install.command` (Doppelklick im Finder) bzw. `mac/install.sh` richtet den Mac in einem Schritt ein: installiert bei Bedarf Apples Command Line Tools, baut LaptopScreen, legt es in den Programme-Ordner und startet es.
  - Der erste Start von `imac-display.exe` lädt ffmpeg nach Rückfrage selbst, prüft Intel Quick Sync und legt im Startmenü „iMac-Display“ an. `setup.cmd` ist damit nicht mehr nötig, funktioniert aber weiter.
  - Fehlt der HDMI-Dummy-Stecker, sagt LaptopScreen das auf dem Mac; sobald er steckt, kommt das Bild von selbst.
  - Startet man `imac-display.exe` direkt aus der Zip-Datei heraus oder ein zweites Mal, erklärt es das. Bei Fehlern bleibt das Fenster offen, bis man eine Taste drückt.
  - Programm-Icons für beide Seiten. Die Version steht im Startfenster des Laptops, im Wartebildschirm des Macs und unter „Über LaptopScreen“.
  - `--test` zeigt zusätzlich Dummy-Stecker, Intel Quick Sync und die Zwischenablage-Einstellung.

### Geändert

- Bricht die Verbindung zum Mac ab, etwa weil LaptopScreen neu startet, bleibt der Mac-Bildschirm noch 15 s erhalten; beim Wiederverbinden stellt das Programm die Anzeige nur um, wenn es nötig ist. Kurze Aussetzer werfen die Fenster also nicht mehr durcheinander.

## [1.1.0] – 2026-10-05

### Geändert

- **Standard-Skalierung 200 % statt 150 %.** Bei gleicher Arbeitsfläche ist Windows-Schrift kleiner als die von macOS (Segoe UI 12 px bei 100 %, macOS-Systemschrift 13 pt). Mit 150 % wirkte sie auf dem 27-Zoll-iMac kleiner als die Schrift des Macs selbst, mit 200 % ist sie gut lesbar. Eine selbst gewählte Skalierung bleibt wie bisher gespeichert.
- `windows\build.cmd` benennt eine laufende `imac-display.exe` vor dem Bauen in `imac-display.exe.old` um, statt am gesperrten Programm zu scheitern.

### Behoben

- **Bildfehler am unteren Bildrand** (Taskleiste und darüber: bunte Blöcke oder grobe Artefakte, die erst verschwanden, wenn sich dort etwas änderte). Ursache war ein zu kleiner Encoder-Puffer (`-bufsize 2M`): Er begrenzte jedes einzelne Bild auf 334 KB, ein 4K-Schlüsselbild braucht aber 0,7–0,9 MB. Abgeschnitten wurde jeweils das Ende des Schlüsselbilds – also der untere Bildteil –, und alle Folgebilder bauten darauf auf. Mit `-bufsize 16M` passt jedes Bild hinein; ffmpegs eigener Decoder fand in 10 s Testaufnahme keinen Fehler mehr statt sieben.

## [1.0.0] – 2026-10-05

### Hinzugefügt

- **LaptopScreen (Mac):** zeigt den Bildschirm des Laptops im Vollbild auf einem eigenen Schreibtisch (Space). Das Bild kommt als FLV/H.264 über TCP und wird über `AVSampleBufferDisplayLayer` sofort nach dem Dekodieren angezeigt, ohne Wiedergabetakt – es gibt also keinen Puffer, in dem sich Verzögerung stauen könnte.
- **Steuerung per Mac-Tastatur und -Maus,** solange LaptopScreen vorne ist:
  - Text kommt so an, wie die Mac-Tastaturbelegung ihn erzeugt (Umlaute, Zeichen mit Option wie `@ € { } [ ]`), unabhängig vom Windows-Layout.
  - Cmd wird zu Strg, Ctrl zur Windows-Taste, Option zu Alt.
  - Mac-typische Textnavigation: Option+←/→ springt wortweise, Cmd+←/→ an Zeilenanfang und -ende, Cmd+↑/↓ an Dokumentanfang und -ende.
  - Ctrl+Klick ist ein Rechtsklick, Cmd+Scrollen zoomt.
  - Ctrl+←/→ bleibt bei macOS, um zwischen den Schreibtischen zu wechseln.
- **imac-display.exe (Laptop):** läuft ohne Adminrechte und baut nur ausgehende Verbindungen auf. Findet den Mac per Bonjour (direktes Kabel bevorzugt), schaltet den HDMI-Dummy-Stecker als erweiterten 4K-Bildschirm zu und überträgt ihn per Intel Quick Sync (H.264, 60 fps, 80 Mbit/s). ffmpeg läuft in einem Job-Objekt und endet garantiert zusammen mit dem Programm.
- **Kopplungscode** mit gegenseitiger Prüfung (HMAC-SHA256 über frische Nonces). Video nimmt der Mac nur vom gekoppelten Laptop an.
- **Deckel zugeklappt:** Der Mac wird der einzige Bildschirm, weiterhin in 4K. Beim Aufklappen kommt das Laptop-Display zurück. Setzt Windows dabei Auflösung oder Skalierung zurück, stellt das Programm sie wieder her und führt die Aufnahme dem Bildschirm nach.
- **Skalierung:** Standard 150 %. Eine in den Windows-Einstellungen gewählte Skalierung wird übernommen und für den nächsten Start gemerkt.
- **Sperrbildschirm:** Bei gesperrtem Windows pausiert die Übertragung, und LaptopScreen zeigt „Der Laptop ist gesperrt“.
- Diagnose mit `--test`, Notfall-Option `--restore` (nur Laptop-Display), Log unter `%LOCALAPPDATA%\imac-display\imac-display.log`.
- `setup.cmd` lädt ffmpeg 9.0.2 herunter (fest gepinnt, mit SHA-256-Prüfung).
- Entwickler-Werkzeug `windows\dev\latency-probe.ps1` misst die Latenz des Senders direkt auf dem Laptop.

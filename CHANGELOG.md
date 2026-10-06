# Changelog

Alle nennenswerten Änderungen dieses Projekts. Datumsformat: JJJJ-MM-TT.

## [1.7.0] – 2026-10-06

### Behoben

- **Keine Hänger mehr, weil die Verbindung im WLAN hängen blieb.** Wacht der Mac auf, ist das Kabel zu ihm ein paar Sekunden lang mal da, mal weg. Suchte iMac-Display genau dann, verband es sich übers WLAN und blieb dort – Bild und Mauszeiger hingen dann immer wieder kurz. Jetzt sieht es während einer WLAN-Verbindung alle 10 Sekunden nach, ob der Mac übers Kabel erreichbar ist, und wechselt hinüber. Das Bild setzt dabei etwa eine Sekunde aus, nie während du eine Taste oder Maustaste gedrückt hältst.
- **Ein schlafender Mac wird nicht mehr jede Minute geweckt.** Legst du den Mac schlafen, sagt LaptopScreen vorher Bescheid, und iMac-Display trennt sich, solange der Mac noch wach ist. Danach wartet es, bis der Mac wirklich wieder wach ist: Apple TV und HomePod antworten für einen schlafenden Mac und hätten ihn bei jedem Verbindungsversuch geweckt. Der Mac-Bildschirm bleibt in Windows so lange eingerichtet, deine Fenster bleiben also, wo sie sind. Ist nur der Bildschirm des Mac aus, wartet iMac-Display ebenfalls. Beide Seiten brauchen dafür 1.7.0; der Mac aktualisiert sich wie gewohnt selbst.

## [1.6.1] – 2026-10-06

### Hinzugefügt

- Die Anleitung erklärt Schritt für Schritt, wie Windows den Laptop beim Zuklappen weiterlaufen lässt („Beim Zuklappen: Nichts unternehmen“): über die Systemsteuerung und in neueren Versionen von Windows 11 über die Einstellungen.

### Geändert

- **Der Laptop schläft nicht mehr ein, solange das Bild läuft.** Bisher konnte Windows ihn nach einer Weile ohne Eingabe in den Energiesparmodus schicken, auch mitten in einer Verbindung – etwa zugeklappt, während du nur am Mac gearbeitet hast, und wach wurde er erst wieder beim Aufklappen. Jetzt hält `imac-display.exe` ihn wach, solange das Bild zum Mac läuft. Ist Windows gesperrt oder die Verbindung getrennt, gelten wieder die Energiespareinstellungen von Windows. Auf dem Mac ändert sich nichts.
- **„Verbindung trennen“ beendet jetzt auch das Programm** und heißt deshalb „Trennen und beenden“. Bisher blieb iMac-Display danach offen und musste eigens über die Taskleiste beendet werden. Für die nächste Verbindung startest du es wieder. Ist der Deckel zu, fragt es wie bisher vorher nach.

## [1.6.0] – 2026-10-06

### Hinzugefügt

- **MacBooks bekommen eine passende Auflösung.** LaptopScreen meldet die Größe seines Bildschirms, und `imac-display.exe` wählt den Modus des Dummy-Steckers, der ihn am besten ausfüllt: 3840 × 2160 für einen iMac wie bisher, 2560 × 1600 für die 16:10-Bildschirme der MacBooks. Bisher gab es dort schwarze Balken und zu kleine Schrift. Wer in den Windows-Einstellungen eine andere Auflösung wählt, behält sie; `imac-display.exe` merkt sie sich für diesen Mac. LaptopScreen auf dem Mac braucht dafür ebenfalls 1.6.0.

### Geändert

- **Das X schließt nur das Fenster, nicht die Verbindung.** Es legt das Fenster in die Taskleiste, und der Mac bleibt dein Bildschirm. Beenden lässt sich iMac-Display über die Taskleiste (Rechtsklick auf das Symbol → „Fenster schließen“) oder mit Alt+F4; bei zugeklapptem Deckel fragt es wie bisher vorher nach.

### Behoben

- **Das Fenster ging nach dem Auf- oder Zuklappen verloren:** War es minimiert, während der Mac-Bildschirm seinen Platz wechselte, kam es neben allen Bildschirmen zurück und flackerte beim Klick aufs Taskleisten-Symbol nur kurz auf. Jetzt erscheint es dann in der Mitte des Bildschirms mit dem Mauszeiger.
- Rückfragen und Meldungen folgen jetzt dem dunklen Modus, etwa die Warnung bei zugeklapptem Deckel; bisher erschienen sie als helle Windows-Meldungsfenster. Ihre Knöpfe sagen, was passiert („Beenden“, „Trennen“, „Herunterladen“), statt „Ja“ und „Nein“.

## [1.5.0] – 2026-10-06

### Hinzugefügt

- **Die Maus vom Mac kommt auch auf das Laptop-Display.** Ist der Deckel offen, schiebst du sie einfach über den Rand des Mac-Bildschirms, an dem in Windows das Laptop-Display liegt: Der Zeiger läuft auf dem Laptop weiter, wie bei einem zweiten Monitor, und über denselben Rand kommt er zurück. So ziehst du auch Fenster mit der Mac-Maus von einem Bildschirm auf den anderen. Welcher Rand das ist, bestimmt die Anordnung in den Windows-Einstellungen. LaptopScreen auf dem Mac braucht dafür ebenfalls 1.5.0 und bietet die Aktualisierung beim Verbinden an.
- Die Anleitung nennt Ctrl+Shift+← / →: Damit schiebst du das aktive Fenster auf den anderen Bildschirm.

## [1.4.0] – 2026-10-06

### Hinzugefügt

- **Ein richtiges Programmfenster statt der Konsole.** Ein farbiger Punkt und ein Satz zeigen, was gerade passiert: grün, wenn das Bild läuft, gelb beim Suchen und Verbinden, mit Pause-Zeichen bei gesperrtem Windows, rot bei einem Problem.
  - „Verbindung trennen“ schaltet den Mac-Bildschirm ab, ohne das Programm zu beenden; „Verbinden“ holt ihn zurück. Ist der Deckel zu, fragt das Programm vorher nach, denn dann ist der Mac der einzige Bildschirm.
  - Das Log erscheint nur noch auf Wunsch („Log“): live mitlaufend, mit „Diagnose“ (was bisher `--test` zeigte) und „Logdatei öffnen“.
  - „Anleitung“ öffnet diese Beschreibung auf GitHub.
  - Der Schalter „Zwischenablage mit dem Mac teilen“ wirkt sofort, auch während einer Verbindung.
  - Das Taskleisten-Symbol trägt den Zustand als Badge und zeigt Downloads und das Umschalten der Anzeige als Fortschrittsbalken.
  - Das Fenster folgt dem hellen oder dunklen App-Modus von Windows, auch beim Umschalten im laufenden Betrieb, und bleibt auf Bildschirmen mit verschiedener Skalierung scharf (Laptop 125 %, Mac 200 %).
  - Ein zweiter Start holt das laufende Fenster nach vorne.
- **Sucht selbst nach neuen Versionen.** Beim Start und danach alle sechs Stunden fragt iMac-Display bei GitHub nach der aktuellen Versionsnummer und schaut im Ordner „Downloads“ nach einer neueren Zip-Datei. Gibt es eine, bietet das Fenster sie an: „Aktualisieren …“ lädt sie direkt, zeigt, was neu ist, installiert sie und startet neu. Während einer Verbindung bleibt der Mac-Bildschirm dabei erhalten. Klappt der Download nicht, etwa wegen eines Firmen-Proxys, übernimmt der Browser.

### Geändert

- **Hauptbildschirm ist bei offenem Deckel jetzt das Laptop-Display**, nicht mehr der Mac. Windows zeigt die Anmeldung nach einer Sperre nur auf dem Hauptbildschirm; auf dem Mac-Bildschirm blieb sie unsichtbar, und das Laptop-Display blieb schwarz, auch nach dem Aufklappen. Die Taskleiste erscheint weiterhin auf beiden Bildschirmen, bei zugeklapptem Deckel ist der Mac ohnehin der einzige. Den Mac als Hauptbildschirm gibt es mit `--mac-primary`.
- ffmpeg lädt das Programm beim ersten Start selbst, mit Fortschrittsbalken und Prüfsumme; `setup.cmd` und `windows\setup.ps1` entfallen.
- `update.cmd` öffnet das Fenster und sucht sofort nach einer neuen Version; eine darauf gezogene Zip-Datei wird wie bisher installiert.
- `imac-display.exe --test` schreibt die Diagnose nur noch, wenn die Ausgabe umgeleitet wird (etwa `| Out-String`), und zeigt sie sonst in einem Fenster.
- Ist der Laptop gesperrt, nennt LaptopScreen auf dem Mac den Ausweg, falls das Laptop-Display beim Aufklappen schwarz bleibt: den HDMI-Dummy-Stecker kurz ziehen. Im gesperrten Zustand lässt Windows kein Programm die Anzeige umschalten, und die Anmeldung liegt dann auf dem Mac-Bildschirm.

## [1.3.0] – 2026-10-06

### Geändert

- **Die Bildschirm-Anordnung aus den Windows-Einstellungen bleibt erhalten.** Bisher legte `imac-display.exe` das Laptop-Display bei jedem Zuschalten links neben den Mac-Bildschirm, mit der Maus ging es also immer rechts hinüber. Jetzt gilt die Anordnung, die Windows sich merkt: Wer den Mac-Bildschirm unter Einstellungen → System → Anzeige über das Laptop-Display zieht, kommt am oberen Rand hinüber – auch nach dem Zu- und Aufklappen und beim nächsten Verbinden. Wer nichts umstellt, behält die bisherige Anordnung.

### Behoben

- `update.cmd` öffnete einen Download, den es nicht gibt (Branch `master` statt `main`), und die Einrichtung des Macs nannte den Ordner `imac-display-master` statt `imac-display-main`.

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

# imac-display

Nutzt den Mac als zusätzlichen, großen Bildschirm für den Firmen-Laptop – ohne Adminrechte auf dem Laptop.

Der Laptop bekommt einen zweiten Bildschirm in 4K, dessen Bild live auf den Mac übertragen wird. Dort läuft es im Vollbild auf einem eigenen Schreibtisch: Mit Ctrl+←/→ wechselst du zwischen macOS und dem Laptop. Solange das Laptop-Bild vorne ist, steuern Tastatur und Maus des Macs den Laptop, und kopierter Text wandert mit. Der Mac bleibt also ganz normal benutzbar, der Laptop kann daneben stehen oder sogar zugeklappt sein.

Erprobt mit einem Dell Pro 16 (Windows 11, ohne Adminrechte) und einem iMac Retina 5K (2020, macOS 15).

## So funktioniert es

- **Ein HDMI-Dummy-Stecker** im Laptop gibt sich als 4K-Monitor aus. Ohne Adminrechte darf Windows keinen virtuellen Bildschirm anlegen – ein Stecker für rund 6 € löst das.
- **`imac-display.exe`** auf dem Laptop nimmt diesen Bildschirm auf, kodiert ihn mit der Intel-Grafik als H.264 und schickt ihn per Kabel oder WLAN an den Mac. Umgekehrt setzt es die Eingaben vom Mac um.
- **LaptopScreen** auf dem Mac zeigt das Bild ohne Zwischenpuffer an (Verzögerung etwa 50–100 ms) und schickt Tastatur und Maus zurück.
- Beide finden sich von selbst und koppeln sich über einen **Kopplungscode**, den der Mac anzeigt. Alles bleibt in deinem Netz, es gibt keinen Cloud-Dienst und kein Konto.
- **Updates** holst du nur auf den Laptop; den Mac bringt der Laptop dann selbst auf den neuen Stand.

## Was du brauchst

| | |
|---|---|
| Laptop | Windows 10 oder 11 mit Intel-Grafik (Quick Sync), z. B. Dell Pro 16. Adminrechte sind nicht nötig, selbst heruntergeladene Programme müssen aber starten dürfen (manche Firmen sperren das). |
| Stecker | HDMI-Dummy-Stecker (auch „Display Emulator“ genannt) mit 4K bei 60 Hz, z. B. [von FUERAN](https://www.amazon.de/dp/B0FMG2CPL6/?th=1), ca. 6 €. |
| Mac | macOS 11 oder neuer. Die kostenlosen Command Line Tools von Apple, mit denen LaptopScreen gebaut wird, installiert die Einrichtung bei Bedarf selbst. |
| Verbindung | Am besten ein LAN-Kabel direkt zwischen Laptop und Mac (ohne jede Einrichtung), z. B. [ein Cat6](https://www.amazon.de/dp/B00N2VIALK/?th=1), ca. 4 €. Sonst geht es auch über das gemeinsame WLAN. |

## Einrichtung auf dem Mac (einmalig)

1. **Dieses Projekt herunterladen** mit Klick auf https://github.com/mb73/imac-display/archive/refs/heads/main.zip. Safari entpackt es meist von selbst in den Ordner „Downloads“.
2. **Einrichtung starten:** im Ordner `imac-display-main/mac` die Datei `install.command` doppelklicken.
3. **Beim ersten Mal blockiert macOS das,** weil die Datei aus dem Internet kommt und Apple sie nicht geprüft hat: „Fertig“ klicken, dann Systemeinstellungen → Datenschutz & Sicherheit öffnen, ganz nach unten scrollen und bei „install.command“ auf „Dennoch öffnen“ klicken. Nach der Bestätigung öffnet sich das Terminal und zeigt den Fortschritt.
4. Fehlen die Command Line Tools, erscheint ein Fenster von Apple: „Installieren“ wählen und warten, das dauert einige Minuten. Danach macht die Einrichtung von selbst weiter.
5. LaptopScreen landet im Programme-Ordner und startet. macOS fragt, ob es **eingehende Verbindungen** annehmen und das **lokale Netzwerk** nutzen darf: beides erlauben.

Alternative ohne Doppelklick und Systemeinstellungen: das Programm „Terminal“ öffnen (Spotlight: Cmd+Leertaste, „Terminal“ tippen), `sh ` tippen (mit Leerzeichen dahinter), die Datei `install.sh` aus demselben Ordner ins Fenster ziehen und Return drücken.

Tipp: Rechtsklick auf das LaptopScreen-Symbol im Dock → Optionen → „Im Dock behalten“.

## Einrichtung auf dem Laptop (einmalig)

1. **Dieses Projekt herunterladen** mit Klick auf https://github.com/mb73/imac-display/archive/refs/heads/main.zip.
2. **Vor dem Entpacken freigeben:** Rechtsklick auf die Zip-Datei → Eigenschaften → unten „Zulassen“ anhaken → OK. Sonst behandelt Windows die enthaltenen Programme als „aus dem Internet“ und blockiert sie.
3. **Entpacken,** z. B. nach `C:\Users\<dein Name>\imac-display`. Das Programm nicht direkt aus der Zip-Datei heraus starten.
4. **Den Dummy-Stecker** in den HDMI-Anschluss des Laptops stecken.
5. **`imac-display.exe` doppelklicken.** Beim ersten Start lädt es das freie Programm ffmpeg (ca. 110 MB, einmalig), prüft die Intel-Grafik, legt im Startmenü „iMac-Display“ an und fragt nach dem Kopplungscode vom Mac.

Tipp: Im Startmenü per Rechtsklick auf „iMac-Display“ → „An Taskleiste anheften“.

## Benutzung

1. **Mac:** LaptopScreen starten. Es zeigt „Warte auf den Laptop …“ und den Kopplungscode.
2. **Laptop:** `imac-display.exe` starten. Beim ersten Mal fragt es im Konsolenfenster nach dem Kopplungscode und merkt ihn sich.
3. Nach wenigen Sekunden erscheint der Laptop-Desktop auf dem Mac. Der neue Bildschirm ist jetzt der Windows-Hauptbildschirm mit Taskleiste; das Laptop-Display liegt zunächst links daneben (umstellen: siehe [Anordnung](#anordnung)).

Die Reihenfolge ist egal: Das Programm auf dem Laptop sucht so lange, bis der Mac da ist.

### Tastatur und Maus

Solange LaptopScreen vorne ist, gehen Tastatur und Maus an den Laptop. Gedacht ist es so, dass du tippen kannst wie auf dem Mac:

| Auf dem Mac | Wirkung in Windows |
|---|---|
| Cmd+C, V, X, Z, S, A, F … | Strg+C, V, X, Z, S, A, F … |
| Option+← / → | Wort zurück / vor |
| Cmd+← / → | Zeilenanfang / Zeilenende |
| Cmd+↑ / ↓ | Dokumentanfang / Dokumentende |
| Option+Backspace | Wort löschen |
| Ctrl+Taste | Windows-Taste+Taste, z. B. Ctrl+E für den Explorer |
| Ctrl+Klick | Rechtsklick |
| Cmd+Scrollen | Zoomen (Strg+Mausrad) |
| Umlaute und Zeichen wie @ € { } [ ] \| ~ | kommen genau so an, wie der Mac sie tippt |
| **Ctrl+← / →** oder Wischgeste | zwischen macOS und Laptop wechseln |
| Cmd+Tab | zu einem anderen Mac-Programm wechseln |
| Ctrl+Cmd+F | Vollbild ein/aus |
| Cmd+Q | LaptopScreen beenden |

### Zwischenablage

Kopierter Text wandert mit, und zwar in die Richtung, in die du wechselst:

- **Vom Mac zum Laptop:** Text in einem Mac-Programm kopieren, zu LaptopScreen wechseln, mit Cmd+V einfügen.
- **Vom Laptop zum Mac:** Text auf dem Laptop kopieren, zu einem Mac-Programm wechseln, einfügen.

Übertragen wird nur Text, keine Bilder oder Dateien. Was du auf dem Laptop kopierst, während LaptopScreen nicht vorne ist, bleibt auf dem Laptop, und was Passwort-Manager als vertraulich markieren, bleibt immer, wo es ist. Wer die Zwischenablage nicht teilen möchte, startet einmal `imac-display.exe --clipboard off`; das gilt dann dauerhaft (wieder an mit `--clipboard on`).

### Deckel zuklappen

Hängt der Laptop am Ladekabel, kannst du ihn zuklappen: Dann ist der Mac sein einziger Bildschirm, weiterhin in 4K. Klappst du ihn wieder auf, kommt das Laptop-Display zurück. Ohne Ladekabel schickt Windows den Laptop beim Zuklappen womöglich in den Standby.

### Anordnung

Zunächst liegt das Laptop-Display links neben dem Mac-Bildschirm: Mit Maus oder Touchpad des Laptops kommst du am rechten Rand hinüber. Steht der Laptop zum Beispiel vor dem iMac, zieh den großen Bildschirm unter Einstellungen → System → Anzeige über das Laptop-Display – dann geht es am oberen Rand hinüber. Windows merkt sich die Anordnung, und `imac-display.exe` behält sie bei, auch nach dem Zu- und Aufklappen und beim nächsten Verbinden.

### Skalierung

Voreingestellt sind 200 %: Die Arbeitsfläche entspricht dann Full HD (1920 × 1080), und die Schrift ist auf einem 27-Zoll-iMac angenehm groß und scharf. Wer mehr Platz braucht, nimmt 175 % oder 150 % (wie 2560 × 1440) – dann wird Windows-Schrift allerdings kleiner als die von macOS. Ändern kannst du das wie gewohnt: Einstellungen → System → Anzeige → den großen Bildschirm auswählen → Skalierung. `imac-display.exe` übernimmt deinen Wert und merkt ihn sich.

### Beenden

Das Konsolenfenster von `imac-display.exe` schließen oder dort Strg+C drücken. Der 4K-Bildschirm verschwindet, und alle Fenster wandern zurück auf das Laptop-Display.

## Aktualisieren

**Laptop:** Die neue Version auf GitHub herunterladen („Code“ → „Download ZIP“) und `imac-display.exe` starten. Es findet die Zip-Datei im Downloads-Ordner, zeigt, was neu ist, und fragt, ob es sie installieren soll. Noch einfacher: `update.cmd` im Programmordner doppelklicken – es öffnet den Download im Browser und installiert ihn, sobald er angekommen ist. Speichert der Browser woanders, zieh die Zip-Datei einfach auf `update.cmd`. Kopplungscode, Einstellungen und ffmpeg bleiben erhalten.

**Mac:** Nichts zu tun. Bringt der Laptop eine neuere Version mit, fragt LaptopScreen beim Verbinden, ob es sich aktualisieren soll. Es baut die neue Version dann selbst (etwa eine Minute) und startet neu. Gut möglich, dass macOS danach noch einmal nach eingehenden Verbindungen und dem lokalen Netzwerk fragt: wieder beides erlauben.

## Grenzen

- **Sperrbildschirm:** Ist Windows gesperrt, musst du direkt am Laptop entsperren. Den Sperrbildschirm lässt Windows aus Sicherheitsgründen von keinem Programm fernsteuern. LaptopScreen zeigt dann „Der Laptop ist gesperrt“. Bei zugeklapptem Deckel heißt das: aufklappen, entsperren, wieder zuklappen. Solange du über den Mac auf dem Laptop arbeitest, zählt das als Eingabe – die automatische Sperre nach einer Weile ohne Eingabe greift dann nicht.
- **Programme mit Adminrechten** und Dialoge der Benutzerkontensteuerung nehmen keine Eingaben vom Mac an.
- **Kein Ton,** und die Zwischenablage teilt nur Text. Ton spielt weiter der Laptop.
- **Höchstens 4K:** Für 5K bräuchte es HEVC, und das haben Dell und HP bei manchen Modellen aus Lizenzgründen abgeschaltet. 4K wird auf einem 5K-iMac sauber hochskaliert.

## Wenn etwas nicht klappt

| Problem | Lösung |
|---|---|
| `imac-display.exe` findet den Mac nicht | Läuft LaptopScreen? Auf dem Mac unter Systemeinstellungen → Datenschutz & Sicherheit → Lokales Netzwerk LaptopScreen erlauben, und in der Firewall eingehende Verbindungen für LaptopScreen zulassen. Notfalls die Adresse direkt angeben: `imac-display.exe --host <IP>` – die IP-Adressen stehen im Wartebildschirm von LaptopScreen. |
| „Kopplungscode abgelehnt“ | Den Code so eingeben, wie er auf dem Mac steht; Groß- und Kleinschreibung sowie Bindestriche sind egal. |
| LaptopScreen meldet „kein HDMI-Dummy-Stecker“ | Den Stecker in den HDMI-Anschluss des Laptops stecken. Das Bild kommt dann von selbst. |
| Bild steht still | Ist Windows gesperrt? Dann am Laptop entsperren. |
| „Intel Quick Sync geht hier nicht“ | Der Laptop hat keine passende Intel-Grafik; dann kann `imac-display.exe` das Bild nicht übertragen. |
| Aktualisierung auf dem Mac schlägt fehl | Die Meldung nennt den Grund. Fehlen die Command Line Tools: im Terminal `xcode-select --install`. LaptopScreen läuft so lange in der alten Version weiter. |
| Text kommt in der Zwischenablage nicht an | Erst kopieren, dann wechseln: Der Text wandert beim Wechsel zu LaptopScreen bzw. weg davon mit. Ist die Zwischenablage abgeschaltet? `imac-display.exe --test` zeigt es. |
| Nach einem Absturz ist ein unsichtbarer Bildschirm aktiv | Win+P → „Nur PC-Bildschirm“ oder `imac-display.exe --restore`. |
| Genauer nachsehen | `imac-display.exe --test` zeigt Bildschirme, Dummy-Stecker, Deckel, ffmpeg, Intel Quick Sync und gefundene Macs. Das Log liegt unter `%LOCALAPPDATA%\imac-display\imac-display.log`. |

Auf dem Mac setzt das Menü „Neuen Kopplungscode erzeugen“ die Kopplung zurück; danach fragt der Laptop erneut nach dem Code.

## Sicherheit

- Auf dem Laptop läuft alles **ohne Adminrechte**. Geändert werden nur deine eigenen Anzeige-Einstellungen; installiert wird nichts außer einem Startmenü-Eintrag für dich.
- Der Laptop baut **nur ausgehende Verbindungen** zum Mac auf (TCP 47100 für das Bild, 47101 für die Steuerung). Auf dem Laptop öffnet sich kein Port.
- Beide Seiten weisen sich mit dem **Kopplungscode** aus (HMAC-SHA256). Ein fremdes Gerät im Netz kann weder Eingaben mitlesen noch ein Bild einschleusen; Video nimmt der Mac nur vom gekoppelten Laptop an.
- **Updates für den Mac** kommen nur vom gekoppelten Laptop, sind mit dem Kopplungscode signiert und werden erst nach deiner Zustimmung auf dem Mac gebaut.
- Das **Bild und die Zwischenablage sind nicht verschlüsselt.** Nutze deshalb das direkte Kabel oder dein Heimnetz, kein fremdes WLAN.
- Technisch ist das eine Fernsteuerung des Firmen-Laptops vom Mac aus, und die geteilte Zwischenablage trägt Text vom Firmen-Laptop auf deinen Mac. Stimme bitte mit der IT ab, ob das für dich in Ordnung ist.

## Für Entwickler

- `windows\build.cmd` baut `imac-display.exe` mit dem C#-Compiler, der in jedem Windows steckt (.NET Framework 4.x).
- `mac/build.sh` baut `LaptopScreen.app` mit den Command Line Tools, `mac/install.sh` installiert es.
- Aufbau, Protokoll, Update-Mechanismus, Messwerkzeuge und Fallstricke stehen in [CLAUDE.md](CLAUDE.md), Änderungen in [CHANGELOG.md](CHANGELOG.md).

## Lizenz

MIT, siehe [LICENSE](LICENSE). ffmpeg gehört nicht zu diesem Projekt: `imac-display.exe` lädt beim ersten Start den Windows-Build von [gyan.dev](https://www.gyan.dev/ffmpeg/builds/) herunter, der unter der GPL v3 steht.

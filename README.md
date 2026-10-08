# imac-display

<img width="826" height="288" alt="LaptopScreen2" src="https://github.com/user-attachments/assets/c85ac203-7045-4db8-af25-b5c85fc06d1e" />

Nutzt den Mac als zusätzlichen, großen Bildschirm für den Firmen-Laptop – ohne Adminrechte auf dem Laptop. Der Name „imac-display“ ist nicht mehr ganz passend, da mittlerweile auch MacBooks unterstützt werden.

Der Laptop bekommt einen zweiten Bildschirm in 4K, dessen Bild live auf den Mac übertragen wird. Dort läuft es im Vollbild auf einem eigenen Schreibtisch: Mit <kbd>ctrl</kbd> + <kbd>←</kbd> / <kbd>→</kbd> wechselst du zwischen macOS und dem Laptop. Solange das Laptop-Bild vorne ist, steuern Tastatur und Maus des Macs den Laptop, und kopierter Text wandert mit. Der Mac bleibt also ganz normal benutzbar, der Laptop kann daneben stehen oder sogar zugeklappt sein.

Erprobt mit einem Dell Pro 16 (Windows 11, ohne Adminrechte) und einem iMac Retina 5K (2020, macOS 15). In dem Fall werden die 4K sauber auf 5K hochskaliert.

**Warnung:** Mitunter gehen Daten unverschlüsselt übers WLAN (z. B. solange das LAN-Kabel nach dem Aufwachen nicht sofort verfügbar ist). Deshalb zur Sicherheit nur im eigenen WLAN (also zuhause im Heimbüro) benutzen.

## So funktioniert es

- **Ein HDMI-Dummy-Stecker** im Laptop gibt sich als 4K-Monitor aus. Ohne Adminrechte darf Windows keinen virtuellen Bildschirm anlegen – ein Stecker für rund 6 € löst das.
- **`imac-display.exe`** auf dem Laptop nimmt diesen Bildschirm auf, kodiert ihn mit der Intel-Grafik als H.264 und schickt ihn per LAN-Kabel oder WLAN an den Mac. Umgekehrt setzt es die Eingaben vom Mac um.
- **LaptopScreen** auf dem Mac zeigt das Bild ohne Zwischenpuffer an (Verzögerung etwa 50–100 ms) und schickt Tastatur und Maus zurück (egal die am Bluetooth oder am Kabel hängen).
- Beide finden sich von selbst und koppeln sich über einen **Kopplungscode**, den der Mac anzeigt. Alles bleibt in deinem Netz, es gibt keinen Cloud-Dienst und kein Konto.
- **Updates** holst du nur auf den Laptop; den Mac bringt der Laptop dann selbst auf den neuen Stand.

## Was du brauchst

| | |
|---|---|
| Laptop | Windows 10 oder 11 mit Intel-Grafik (Quick Sync), z. B. Dell Pro 16. Adminrechte sind nicht nötig, selbst heruntergeladene Programme müssen aber starten dürfen (manche Firmen sperren das). |
| Stecker | HDMI-Dummy-Stecker (auch „Display Emulator“ genannt) mit 4K bei 60 Hz, z. B. [von FUERAN](https://www.amazon.de/dp/B0FMG2CPL6/?th=1), ca. 6 €. |
| Mac | macOS 11 oder neuer. Die kostenlosen Command Line Tools von Apple, mit denen LaptopScreen gebaut wird, installiert die Einrichtung bei Bedarf selbst. |
| Verbindung | Am besten ein LAN-Kabel direkt zwischen Laptop und Mac (ohne jede Einrichtung), z. B. [ein Cat6](https://www.amazon.de/dp/B00N2VIALK/?th=1), ca. 4 €. Sonst geht es (ggf. etwas zäh) über das gemeinsame WLAN. |

## Einrichtung auf dem Mac (einmalig)

1. **Folge Zeile kopieren (Knopf rechts):**
   ```sh
   curl -fsSL https://raw.githubusercontent.com/mb73/imac-display/main/mac/install.sh | sh
   ```
2. **Im Programm „Terminal“** (<kbd>cmd</kbd> + Leertaste dann „terminal“ tippen) erst <kbd>cmd</kbd> + <kbd>V</kbd> und dann <kbd>↵</kbd> drücken. Obiger Befehl lädt dann dieses Projekt von GitHub und baut daraus LaptopScreen; was er genau tut, steht in [mac/install.sh](mac/install.sh).
3. Fehlen die Command Line Tools, erscheint ein Fenster von Apple: „Installieren“ wählen und warten, das dauert einige Minuten. Danach macht die Einrichtung von selbst weiter.
4. LaptopScreen landet im Programme-Ordner und startet. macOS fragt, ob es **eingehende Verbindungen** annehmen und das **lokale Netzwerk** nutzen darf: beides erlauben.

**Alternative ohne Terminal:** Dieses Projekt mit Klick auf https://github.com/mb73/imac-display/archive/refs/heads/main.zip herunterladen (Safari entpackt es meist von selbst in den Ordner „Downloads“) und im Ordner `imac-display-main/mac` die Datei `install.command` doppelklicken. Beim ersten Mal blockiert macOS das, weil die Datei aus dem Internet kommt und Apple sie nicht geprüft hat: „Fertig“ klicken, dann Systemeinstellungen → Datenschutz & Sicherheit öffnen, ganz nach unten scrollen und bei „install.command“ auf „Dennoch öffnen“ klicken. Nach der Bestätigung öffnet sich das Terminal, und es geht weiter wie oben ab Schritt 3.

Tipp: Rechtsklick auf das LaptopScreen-Symbol im Dock → Optionen → „Im Dock behalten“.

<img width="453" height="190" alt="Dock" src="https://github.com/user-attachments/assets/df2a7f27-caf1-438c-9ac6-2c188e9e5adc" />

## Einrichtung auf dem Laptop (einmalig)

1. **Dieses Projekt herunterladen** mit Klick auf https://github.com/mb73/imac-display/archive/refs/heads/main.zip.
2. **Vor dem Entpacken freigeben:** Rechtsklick auf die Zip-Datei → Eigenschaften → unten „Zulassen“ anhaken → OK. Sonst behandelt Windows die enthaltenen Programme als „aus dem Internet“ und blockiert sie.
3. **Entpacken,** z. B. nach `C:\Users\<dein Name>\imac-display`. Das Programm nicht direkt aus der Zip-Datei heraus starten.
4. **Den Dummy-Stecker** in den HDMI-Anschluss des Laptops stecken.
5. **`imac-display.exe` doppelklicken.** Beim ersten Start lädt es das freie Programm ffmpeg (ca. 110 MB, einmalig), prüft die Intel-Grafik, legt im Startmenü „iMac-Display“ an und fragt nach dem Kopplungscode vom Mac.

Tipp: Im Startmenü per Rechtsklick auf „iMac-Display“ → „An Taskleiste anheften“.

<img width="478" height="288" alt="DeckelZu" src="https://github.com/user-attachments/assets/04c6b168-0814-4453-be96-437103d4bc7c" />

### Vorbereitung fürs zugeklappte Benutzen

Soll der Laptop auch zugeklappt weiterlaufen, stell Windows einmal so ein, dass es ihn beim Zuklappen nicht in den Energiesparmodus oder Ruhezustand schickt – sonst bekommt der Mac kein Bild mehr, bis du den Laptop wieder aufklappst. Adminrechte brauchst du dafür nicht, solange die Firma diese Einstellungen nicht gesperrt hat.

1. <kbd>win</kbd> + <kbd>R</kbd> drücken, `powercfg.cpl` eingeben und <kbd>↵</kbd> drücken. Die „Energieoptionen“ öffnen sich.
2. Links auf „Auswählen, was beim Zuklappen des Computers geschehen soll“ klicken.
3. „Beim Zuklappen:“ in der Spalte „Netzbetrieb“ auf „Nichts unternehmen“ stellen und „Änderungen speichern“ klicken.

Soll er auch ohne Ladekabel zugeklappt laufen, in Schritt 3 auch die Spalte „Akku“ auf „Nichts unternehmen“ stellen. Dann läuft er allerdings auch in der Tasche weiter, wenn du ihn nur zuklappst – vor dem Einpacken also über Start → Ein/Aus „Energie sparen“ oder „Herunterfahren“ wählen.

In neueren Versionen von Windows 11 geht es auch über Einstellungen → System → Strom und Akku → „Deckel, Ein/Aus und Standbymodus“: „Wenn ich den Deckel schließe, wird mein PC“ in der Spalte „Eingesteckt“ auf „Keine Aktion ausführen“ stellen.

Den Energiesparmodus nach einer Weile ohne Eingabe musst du nicht abschalten: Solange das Bild zum Mac läuft, hält `imac-display.exe` den Laptop wach, auch wenn du gerade nur am Mac arbeitest. Ist Windows gesperrt oder die Verbindung getrennt, gelten wieder die Energiespareinstellungen von Windows.

## Benutzung

1. **Mac:** LaptopScreen starten. Es zeigt „Warte auf den Laptop …“ und den Kopplungscode.
2. **Laptop:** `imac-display.exe` starten. Beim ersten Mal fragt es nach dem Kopplungscode und merkt ihn sich. Das kleine Fenster zeigt, was gerade passiert; der Punkt davor und das Badge am Taskleisten-Symbol sind grün, wenn das Bild läuft, gelb beim Suchen oder bei gesperrtem Windows und rot bei einem Problem.
3. Nach wenigen Sekunden erscheint auf dem Mac ein zweiter Windows-Bildschirm mit eigener Taskleiste. Hauptbildschirm bleibt das Laptop-Display, denn nur dort zeigt Windows nach einer Sperre die Anmeldung; bei zugeklapptem Deckel ist der Mac der einzige und damit der Hauptbildschirm. Wie die beiden Bildschirme zueinander liegen, stellst du in Windows ein (siehe [Anordnung](#anordnung)).

Die Reihenfolge ist egal: Das Programm auf dem Laptop sucht so lange, bis der Mac da ist.

### Tastatur und Maus

Solange LaptopScreen vorne ist, gehen Mac-Tastatur und -Maus (egal ob Bluetooth oder verkabelte) an den Laptop. Gedacht ist es so, dass du tippen kannst wie auf dem Mac:

| Auf dem Mac im LaptopScreen | Wirkung in Windows |
|---|---|
| <kbd>cmd</kbd> + <kbd>C</kbd>, <kbd>V</kbd>, <kbd>X</kbd>, <kbd>Z</kbd>, <kbd>S</kbd>, <kbd>A</kbd>, <kbd>F</kbd> … | <kbd>ctrl</kbd> + <kbd>C</kbd>, <kbd>V</kbd>, <kbd>X</kbd>, <kbd>Z</kbd>, <kbd>S</kbd>, <kbd>A</kbd>, <kbd>F</kbd> … |
| <kbd>opt</kbd> + <kbd>←</kbd> / <kbd>→</kbd> | Wort zurück / vor |
| <kbd>cmd</kbd> + <kbd>←</kbd> / <kbd>→</kbd> | Zeilenanfang / Zeilenende |
| <kbd>cmd</kbd> + <kbd>↑</kbd> / <kbd>↓</kbd> | Dokumentanfang / Dokumentende |
| <kbd>opt</kbd> + <kbd>Backspace</kbd> | Wort löschen |
| <kbd>ctrl</kbd> + Taste | <kbd>win</kbd> + Taste, z. B. ctrl+E für den Explorer |
| <kbd>ctrl</kbd> + <kbd>shift</kbd> + <kbd>←</kbd> / <kbd>→</kbd> | aktives Fenster auf den anderen Bildschirm schieben |
| <kbd>ctrl</kbd> + Klick | Rechtsklick |
| <kbd>cmd</kbd> + Scrollen | Zoomen (<kbd>ctrl</kbd> + Mausrad) |
| Umlaute und Zeichen wie @ € { } [ ] \| ~ | kommen genau so an, wie sie am Mac getippt werden |
| <kbd>ctrl</kbd> +<kbd>←</kbd> / <kbd>→</kbd> oder Wischgeste | **zwischen macOS und Laptop wechseln** |
| <kbd>cmd</kbd> + <kbd>tab</kbd> | zu einem anderen Mac-Programm wechseln |
| <kbd>ctrl</kbd> + <kbd>cmd</kbd> + <kbd>F</kbd> | Vollbild ein/aus |
| <kbd>cmd</kbd> + <kbd>Q</kbd> | LaptopScreen beenden |

Ist der Deckel offen, kommst du mit der Maus vom Mac auch auf das Laptop-Display: Schieb sie über den Rand des Mac-Bildschirms, an dem in Windows das Laptop-Display liegt (siehe [Anordnung](#anordnung)). Der Zeiger läuft dann auf dem Laptop weiter, und über denselben Rand kommt er zurück. Die Tastatur schreibt immer in das aktive Fenster, auch wenn es auf dem Laptop-Display liegt.

### Zwischenablage

Kopierter Text wandert mit, und zwar in die Richtung, in die du wechselst:

- **Vom Mac zum Laptop:** Text in einem Mac-Programm kopieren, zu LaptopScreen wechseln, mit cmd+V einfügen.
- **Vom Laptop zum Mac:** Text auf dem Laptop kopieren, zu einem Mac-Programm wechseln, einfügen.

Übertragen wird nur Text, keine Bilder oder Dateien. Was du auf dem Laptop kopierst, während LaptopScreen nicht vorne ist, bleibt auf dem Laptop, und was Passwort-Manager als vertraulich markieren, bleibt immer, wo es ist. Wer die Zwischenablage nicht teilen möchte, nimmt im Fenster den Haken bei „Zwischenablage mit dem Mac teilen“ heraus; das gilt sofort und dauerhaft.

### Deckel zuklappen

Ist Windows dafür eingestellt (siehe [Zugeklappt benutzen](#zugeklappt-benutzen)), kannst du den Laptop zuklappen: Dann ist der Mac sein einziger Bildschirm, weiterhin in 4K. Klappst du ihn wieder auf, kommt das Laptop-Display zurück. Ohne Ladekabel geht das nur, wenn du die Einstellung auch für den Akku gesetzt hast; sonst schickt Windows den Laptop beim Zuklappen in den Energiesparmodus.

### Anordnung

Zunächst liegt das Laptop-Display links neben dem Mac-Bildschirm: Die Maus kommt am linken Rand des Mac-Bildschirms hinüber und am rechten Rand des Laptop-Displays zurück, mit der Maus vom Mac ebenso wie mit dem Touchpad des Laptops. Steht der Laptop zum Beispiel vor dem iMac, zieh den großen Bildschirm unter Einstellungen → System → Anzeige über das Laptop-Display – dann geht es am unteren Rand des Mac-Bildschirms hinüber. Stell es am besten so ein, wie die beiden wirklich stehen. Windows merkt sich die Anordnung, und `imac-display.exe` behält sie bei, auch nach dem Zu- und Aufklappen und beim nächsten Verbinden.

Wer auch bei aufgeklapten Laptop unbedingt den Mac als Hauptbildschirm braucht (mit Infobereich der Taskleiste und Benachrichtigungen dort), startet `imac-display.exe --mac-primary`. Warnung: Davon wird abgeraten, weil die Anmeldung nach einer Sperre auf dem Mac-Bildschirm landet, wo man sie nicht sieht (siehe [Grenzen](#grenzen)). Wer den Laptop dann aufklappt sieht nur einen schwarzen Schirm bis er den Plug zieht. 

### Skalierung

Voreingestellt sind 200 %: Die Arbeitsfläche entspricht dann Full HD (1920 × 1080), und die Schrift ist auf einem 27-Zoll-iMac angenehm groß und scharf. Wer mehr Platz braucht, nimmt 175 % oder 150 % (wie 2560 × 1440) – dann wird Windows-Schrift allerdings kleiner als die von macOS. Ändern kannst du das direkt im Fenster von iMac-Display unter „Skalierung“, solange die Verbindung steht. Jede Stufe nennt die Arbeitsfläche, die dabei herauskommt, und die Wahl gilt sofort. Wie gewohnt in Windows geht es auch: Einstellungen → System → Anzeige → den großen Bildschirm auswählen → Skalierung. `imac-display.exe` merkt sich deinen Wert in beiden Fällen.

Die Auflösung wählt `imac-display.exe` passend zum Bildschirm des Macs: 3840 × 2160 für einen iMac, 2560 × 1600 für ein MacBook. Stellst du an derselben Stelle eine andere Auflösung ein, merkt es sich die für diesen Mac.

Falls man bereits in den Mac-Systemeinstellungen (unter „Displays“) eine starke Skalierung ausgewählt hat (z. B. auf einem 5k-iMac den Regler ganz nach links bzw. wie „1600 × 900“), so wird in etwas geringer Auflösung übertragen. Darauf weißt die App iMac-Display hin. In meinem Fall habe ich für optimale Schärfe den Regler einen Schritt nach rechts bewegen müssen (wie „2048 × 1152“). „Standard“ (wie „2560 × 1440“) ginge ebenfalls.

<img width="470" height="366" alt="SchärferesBild2" src="https://github.com/user-attachments/assets/269ec441-0676-41af-a225-74fb1aa90e75" />

### Trennen und beenden

„Trennen und beenden“ im Fenster schaltet den Mac-Bildschirm ab und beendet das Programm: Der 4K-Bildschirm verschwindet, und alle Fenster wandern zurück auf das Laptop-Display. Genauso wirkt ein Rechtsklick auf das Symbol in der Taskleiste → „Fenster schließen“. Ist der Deckel zu, fragt das Programm vorher nach, denn ohne den Mac hat der Laptop dann keinen Bildschirm, bis du ihn aufklappst. Für die nächste Verbindung startest du iMac-Display einfach wieder. Das X oben rechts legt das Fenster dagegen nur in die Taskleiste, die Verbindung bleibt bestehen.

## Aktualisierung

**Laptop:** Automatisch. iMac-Display fragt beim Start und danach alle sechs Stunden bei GitHub nach, ob es eine neue Version gibt. Dann steht im Fenster „iMac-Display x.y.z ist da“, und „Aktualisieren …“ lädt sie, zeigt, was neu ist, installiert sie und startet neu. Läuft gerade die Verbindung zum Mac, bleibt der Mac-Bildschirm dabei erhalten. Klappt der Download nicht (etwa wegen eines Firmen-Proxys), lädt der Browser die Zip-Datei; sobald sie im Ordner „Downloads“ liegt, geht es im Fenster weiter. `update.cmd` sucht sofort nach einer neuen Version, und eine Zip-Datei, die du darauf ziehst, wird direkt installiert. Kopplungscode, Einstellungen und ffmpeg bleiben erhalten.

**Mac:** Automatisch. Bringt der Laptop eine neuere Version mit, fragt LaptopScreen beim Verbinden, ob es sich aktualisieren soll. Es baut die neue Version dann selbst (etwa eine Minute) und startet neu. Gut möglich, dass macOS danach noch einmal nach eingehenden Verbindungen und dem lokalen Netzwerk fragt: wieder beides erlauben.

## Grenzen

- **Sperrbildschirm:** Ist Windows gesperrt, musst du direkt am Laptop entsperren. Den Sperrbildschirm lässt Windows aus Sicherheitsgründen von keinem Programm fernsteuern. LaptopScreen zeigt dann „Der Laptop ist gesperrt“. Bei zugeklapptem Deckel heißt das: aufklappen, entsperren, wieder zuklappen. Windows zeigt die Anmeldung nur auf dem Hauptbildschirm, deshalb bleibt das bei offenem Deckel das Laptop-Display. Bleibt es beim Aufklappen trotzdem schwarz (etwa mit `--mac-primary`), den HDMI-Dummy-Stecker kurz ziehen (siehe unten): Im gesperrten Zustand lässt Windows kein Programm die Anzeige umschalten. Solange du über den Mac auf dem Laptop arbeitest, zählt das als Eingabe – die automatische Sperre nach einer Weile ohne Eingabe greift dann nicht.
- **Programme mit Adminrechten** und Dialoge der Benutzerkontensteuerung nehmen keine Eingaben vom Mac an.
- **Sicherheitsabfragen,** etwa beim Verbinden des VPN (Sicherheitsschlüssel, PIN oder Windows Hello), erledigst du direkt am Laptop.
- **Ton** spielt weiterhin (nur) der Laptop.
- **Die Zwischenablage** teilt nur Text. Keine Bilder oder Dateien.
- **Höchstens 4K:** Für 5K bräuchte es HEVC, und das haben Dell und HP bei manchen Modellen aus Lizenzgründen abgeschaltet. 4K wird auf einem 5K-iMac sauber hochskaliert.
- **MacBooks** haben Bildschirme im Format 16:10, die der Dummy-Stecker nicht pixelgenau kann (etwa 2880 × 1800). Sie bekommen 2560 × 1600, leicht hochskaliert und ohne schwarze Balken. Für ein Kabel brauchen sie einen USB-C-Ethernet-Adapter.

## Wenn etwas nicht klappt

| Problem | Lösung |
|---|---|
| `imac-display.exe` findet den Mac nicht | Läuft LaptopScreen? Auf dem Mac unter Systemeinstellungen → Datenschutz & Sicherheit → Lokales Netzwerk LaptopScreen erlauben, und in der Firewall eingehende Verbindungen für LaptopScreen zulassen. Notfalls die Adresse direkt angeben: `imac-display.exe --host <IP>` – die IP-Adressen stehen im Wartebildschirm von LaptopScreen. |
| „Kopplungscode abgelehnt“ | Den Code so eingeben, wie er auf dem Mac steht; Groß- und Kleinschreibung sowie Bindestriche sind egal. |
| LaptopScreen meldet „kein HDMI-Dummy-Stecker“ | Den Stecker in den HDMI-Anschluss des Laptops stecken. Das Bild kommt dann von selbst. |
| Bild steht still | Ist Windows gesperrt? Dann am Laptop entsperren. |
| Laptop gesperrt, Deckel war zu, beim Aufklappen bleibt das Display schwarz | Den HDMI-Dummy-Stecker kurz ziehen: Dann zeigt Windows die Anmeldung auf dem Laptop. Nach dem Entsperren wieder einstecken, das Bild auf dem Mac kommt von selbst. <kbd>win</kbd> + <kbd>P</kbd> geht auf dem Sperrbildschirm nicht. |
| „Intel Quick Sync geht hier nicht“ | Der Laptop hat keine passende Intel-Grafik; dann kann `imac-display.exe` das Bild nicht übertragen. |
| Aktualisierung auf dem Mac schlägt fehl | Die Meldung nennt den Grund. Fehlen die Command Line Tools: im Terminal `xcode-select --install`. LaptopScreen läuft so lange in der alten Version weiter. |
| Text kommt in der Zwischenablage nicht an | Erst kopieren, dann wechseln: Der Text wandert beim Wechsel zu LaptopScreen bzw. weg davon mit. Ist im Fenster der Haken bei „Zwischenablage mit dem Mac teilen“ gesetzt? |
| Nach einem Absturz ist ein unsichtbarer Bildschirm aktiv | <kbd>win</kbd> + <kbd>P</kbd> → „Nur PC-Bildschirm“ oder `imac-display.exe --restore`. |
| Genauer nachsehen | Im Fenster auf „Log“ klicken: Dort steht, was das Programm tut, und „Diagnose“ prüft Bildschirme, Dummy-Stecker, Deckel, ffmpeg und Intel Quick Sync und sucht den Mac. Die Logdatei liegt unter `%LOCALAPPDATA%\imac-display\imac-display.log`. |

Auf dem Mac setzt das Menü „Neuen Kopplungscode erzeugen“ die Kopplung zurück; danach fragt der Laptop erneut nach dem Code.

## Sicherheit

- Das **Bild und die Zwischenablage sind nicht verschlüsselt.** Nutze deshalb das direkte Kabel oder dein Heimnetz, **kein fremdes WLAN**.
- Auf dem Laptop läuft alles **ohne Adminrechte**. Geändert werden nur deine eigenen Anzeige-Einstellungen; installiert wird nichts außer einem Startmenü-Eintrag für dich.
- Der Laptop baut **nur ausgehende Verbindungen** zum Mac auf (TCP 47100 für das Bild, 47101 für die Steuerung). Auf dem Laptop öffnet sich kein Port.
- Beide Seiten weisen sich mit dem **Kopplungscode** aus (HMAC-SHA256). Ein fremdes Gerät im Netz kann weder Eingaben mitlesen noch ein Bild einschleusen; Video nimmt der Mac nur vom gekoppelten Laptop an.
- **Updates für den Mac** kommen nur vom gekoppelten Laptop, sind mit dem Kopplungscode signiert und werden erst nach deiner Zustimmung auf dem Mac gebaut.
- **Nach neuen Versionen** fragt der Laptop bei GitHub: Er lädt dafür beim Start und alle sechs Stunden die kleine Datei `VERSION`, ohne Angaben über dich. Die neue Version selbst lädt er nur auf deinen Klick.

## Für Entwickler

- `windows\build.cmd` baut `imac-display.exe` mit dem C#-Compiler, der in jedem Windows steckt (.NET Framework 4.x).
- `imac-display.exe --test | Out-String` gibt die Diagnose als Text aus; ohne Umleitung erscheint sie in einem Fenster.
- `mac/build.sh` baut `LaptopScreen.app` mit den Command Line Tools, `mac/install.sh` installiert es; per `curl … | sh` gestartet, lädt das Skript vorher den Stand von `main` von GitHub.
- Aufbau, Protokoll, Update-Mechanismus, Messwerkzeuge und Fallstricke stehen in [CLAUDE.md](CLAUDE.md), Änderungen in [CHANGELOG.md](CHANGELOG.md).

## Lizenz

MIT, siehe [LICENSE](LICENSE). ffmpeg gehört nicht zu diesem Projekt: `imac-display.exe` lädt beim ersten Start den Windows-Build von [gyan.dev](https://www.gyan.dev/ffmpeg/builds/) herunter, der unter der GPL v3 steht.

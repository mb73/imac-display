#!/bin/sh
# One-time setup of LaptopScreen on this Mac: installs Apple's Command Line Tools if needed,
# builds the app, puts it into the Applications folder and starts it. Later versions arrive
# from the laptop by themselves (LaptopScreen asks before updating).
# Usage in Terminal: type "sh ", drag this file into the window, press Return.
set -e
cd "$(dirname "$0")"

if ! xcode-select -p >/dev/null 2>&1; then
    echo "LaptopScreen wird auf diesem Mac gebaut und braucht dafür Apples kostenlose Command Line Tools."
    echo "Gleich erscheint ein Fenster. Dort bitte „Installieren“ wählen und warten, bis es fertig ist."
    xcode-select --install >/dev/null 2>&1 || true
    printf "Warte auf die Command Line Tools (Abbrechen mit Ctrl+C) …"
    until xcode-select -p >/dev/null 2>&1; do
        printf "."
        sleep 5
    done
    sleep 5
    echo " fertig."
fi

sh build.sh

if [ -w /Applications ]; then
    target=/Applications
else
    target="$HOME/Applications"
    mkdir -p "$target"
fi
if pgrep -x LaptopScreen >/dev/null 2>&1; then
    pkill -x LaptopScreen || true
    sleep 2
fi
rm -rf "$target/LaptopScreen.app"
ditto LaptopScreen.app "$target/LaptopScreen.app"
rm -rf LaptopScreen.app
echo
echo "LaptopScreen liegt jetzt in $target und startet gleich."
echo "Beim ersten Start fragt macOS, ob LaptopScreen eingehende Verbindungen annehmen und das"
echo "lokale Netzwerk nutzen darf. Bitte beides erlauben."
open "$target/LaptopScreen.app"

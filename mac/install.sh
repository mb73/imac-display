#!/bin/sh
# One-time setup of LaptopScreen on this Mac: installs Apple's Command Line Tools if needed,
# builds the app, puts it into the Applications folder and starts it. Later versions arrive
# from the laptop by themselves (LaptopScreen asks before updating).
# Usage in Terminal, without downloading anything first (fetches the main branch from GitHub):
#   curl -fsSL https://raw.githubusercontent.com/mb73/imac-display/main/mac/install.sh | sh
# Or from the downloaded zip: type "sh ", drag this file into the window, press Return.
# Piped, the shell reads this script from stdin: nothing in here may read stdin, and all of it
# sits in main, so a download that breaks off fails as a syntax error instead of running halfway.
set -e

main() {
    if [ -f "$0" ] && [ -f "$(dirname "$0")/build.sh" ]; then
        cd "$(dirname "$0")"
    else
        # piped: no sources next to this script, so fetch them
        tmp=$(mktemp -d "${TMPDIR:-/tmp}/LaptopScreen-install.XXXXXX")
        trap 'rm -rf "$tmp"' EXIT
        echo "Lade LaptopScreen von GitHub …"
        curl -fsSL -o "$tmp/main.tar.gz" https://github.com/mb73/imac-display/archive/refs/heads/main.tar.gz
        tar -xzf "$tmp/main.tar.gz" -C "$tmp" --strip-components 1
        cd "$tmp/mac"
    fi

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
}

main

#!/bin/sh
# Builds LaptopScreen.app from the Swift sources (needs the Xcode Command Line Tools).
set -e
cd "$(dirname "$0")"
APP="LaptopScreen.app"
VERSION=""
if [ -f ../VERSION ]; then VERSION="$(tr -d ' \r\n' < ../VERSION)"; fi
if [ -z "$VERSION" ]; then VERSION=0.0.0; fi
rm -rf "$APP"
tmp=$(mktemp -d "${TMPDIR:-/tmp}/LaptopScreen-build.XXXXXX")
trap 'rm -rf "$tmp"' EXIT

# Compiles into $tmp, so a failed build leaves no app without a program behind (Spotlight offers
# it, and opening it fails); swiftc's messages go to $tmp/log. Arguments: further swiftc options.
compile() {
    swiftc -O -swift-version 5 -target "$(uname -m)-apple-macos11.0" "$@" \
        -o "$tmp/LaptopScreen" Sources/*.swift >"$tmp/log" 2>&1
}

# The Command Line Tools may bring an SDK newer than their compiler (reported on macOS 27: SDK built
# with Swift 6.4, compiler 6.3.3), and swiftc then fails on the SDK's own Swift module. An older SDK
# usually lies next to it, and LaptopScreen only needs macOS 11: try those, newest first.
MISMATCH="SDK is not supported by the compiler"
if ! compile; then
    if ! grep -q "$MISMATCH" "$tmp/log"; then cat "$tmp/log"; exit 1; fi
    sdk=$(xcrun --show-sdk-path 2>/dev/null || true)
    failed=$(cd "$sdk" 2>/dev/null && pwd -P || true)
    echo "Das macOS-SDK der Command Line Tools ($(basename "$failed")) ist neuer als ihr Swift-Compiler."
    ls -d "$(dirname "$sdk")"/MacOSX*.sdk 2>/dev/null | LC_ALL=C sort -r >"$tmp/sdks"
    built=""
    while IFS= read -r candidate; do
        # symlinks are aliases of SDKs in the list
        if [ -L "$candidate" ] || [ "$(cd "$candidate" && pwd -P)" = "$failed" ]; then continue; fi
        echo "Versuche es mit $(basename "$candidate") …"
        if compile -sdk "$candidate"; then built=1; break; fi
        if ! grep -q "$MISMATCH" "$tmp/log"; then cat "$tmp/log"; exit 1; fi
    done <"$tmp/sdks"
    if [ -z "$built" ]; then
        echo
        echo "LaptopScreen lässt sich nicht bauen: Die Command Line Tools auf diesem Mac passen nicht zusammen."
        echo "Abhilfe: Systemeinstellungen → Allgemein → Softwareupdate, dort die Command Line Tools aktualisieren."
        echo "Gibt es kein Update, sie im Terminal neu installieren (sudo fragt nach dem Passwort des Macs):"
        echo "  sudo rm -rf /Library/Developer/CommandLineTools && xcode-select --install"
        echo "Danach noch einmal versuchen."
        exit 1
    fi
fi
cat "$tmp/log"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
mv "$tmp/LaptopScreen" "$APP/Contents/MacOS/LaptopScreen"
cp Info.plist "$APP/Contents/Info.plist"
if [ -f AppIcon.icns ]; then cp AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"; fi
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $VERSION" "$APP/Contents/Info.plist"
# files from a downloaded zip carry quarantine and other extended attributes, which codesign may reject
xattr -cr "$APP" 2>/dev/null || true
codesign --force --sign - "$APP"
echo "Fertig: $(pwd)/$APP ($VERSION)"

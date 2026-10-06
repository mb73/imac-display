#!/bin/sh
# Builds LaptopScreen.app from the Swift sources (needs the Xcode Command Line Tools).
set -e
cd "$(dirname "$0")"
APP="LaptopScreen.app"
VERSION=""
if [ -f ../VERSION ]; then VERSION="$(tr -d ' \r\n' < ../VERSION)"; fi
if [ -z "$VERSION" ]; then VERSION=0.0.0; fi
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
swiftc -O -swift-version 5 -target "$(uname -m)-apple-macos11.0" \
    -o "$APP/Contents/MacOS/LaptopScreen" Sources/*.swift
cp Info.plist "$APP/Contents/Info.plist"
if [ -f AppIcon.icns ]; then cp AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"; fi
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $VERSION" "$APP/Contents/Info.plist"
# files from a downloaded zip carry quarantine and other extended attributes, which codesign may reject
xattr -cr "$APP" 2>/dev/null || true
codesign --force --sign - "$APP"
echo "Fertig: $(pwd)/$APP ($VERSION)"

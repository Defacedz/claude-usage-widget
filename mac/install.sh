#!/bin/sh
# Builds ClaudeWidget.swift from source and installs it as a menu-bar app
# in ~/Applications. Needs the Xcode Command Line Tools (free):
#   xcode-select --install
set -e

HERE="$(cd "$(dirname "$0")" && pwd)"
APP="$HOME/Applications/ClaudeWidget.app"

if ! command -v swiftc >/dev/null 2>&1; then
    echo "swiftc not found. Install the Xcode Command Line Tools first:"
    echo "  xcode-select --install"
    exit 1
fi

echo "1/3 Building..."
TMP="$(mktemp -d)"
# -parse-as-library: a single-file build is otherwise treated as a script,
# which rejects the @main entry point
swiftc -O -parse-as-library -o "$TMP/ClaudeWidget" "$HERE/ClaudeWidget.swift"

echo "2/3 Installing to $APP ..."
# stop a running instance so the binary can be replaced
pkill -x ClaudeWidget 2>/dev/null || true
mkdir -p "$APP/Contents/MacOS"
cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleExecutable</key><string>ClaudeWidget</string>
  <key>CFBundleIdentifier</key><string>com.defacedz.claudewidget</string>
  <key>CFBundleName</key><string>ClaudeWidget</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSUIElement</key><true/>
</dict></plist>
PLIST
cp "$TMP/ClaudeWidget" "$APP/Contents/MacOS/ClaudeWidget"
rm -rf "$TMP"

echo "3/3 Starting..."
open "$APP"

echo ""
echo "[OK] ClaudeWidget is running - look for the * percentages in the menu bar."
echo "The first keychain access will ask you to allow reading the Claude Code credentials: click 'Always Allow'."

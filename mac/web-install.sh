#!/bin/sh
# ============================================================
#  One-line installer for Claude Usage Widget on macOS.
#
#    curl -fsSL https://raw.githubusercontent.com/Defacedz/claude-usage-widget/main/mac/web-install.sh | sh
#
#  Downloads this repository to a temporary folder and runs mac/install.sh,
#  which builds the widget from source on your machine. Read the README
#  before running it - the widget reads and writes your Claude Code
#  credentials in the keychain.
# ============================================================
set -e

REPO_TARBALL="https://github.com/Defacedz/claude-usage-widget/archive/refs/heads/main.tar.gz"

echo ""
echo "  Claude Usage Widget - macOS installer"
echo "  Source: https://github.com/Defacedz/claude-usage-widget"
echo ""

if ! command -v swiftc >/dev/null 2>&1; then
    echo "swiftc not found. Install the Xcode Command Line Tools first:"
    echo "  xcode-select --install"
    exit 1
fi

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

echo "1/2 Downloading the source..."
curl -fsSL "$REPO_TARBALL" | tar xz -C "$WORK"

echo "2/2 Building and installing..."
sh "$WORK"/claude-usage-widget-*/mac/install.sh

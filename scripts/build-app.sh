#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
ROOT="$(pwd)"
cd Mac
swift build -c release
APP="$ROOT/dist/ScreenTranslator.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS"
cp ".build/release/ScreenTranslator" "$APP/Contents/MacOS/ScreenTranslator"
cp "Support/Info.plist" "$APP/Contents/Info.plist"
codesign --force --sign - "$APP"
echo "Built $APP"

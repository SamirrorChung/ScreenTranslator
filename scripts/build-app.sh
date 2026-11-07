#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
swift build -c release
APP="dist/ScreenTranslator.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS"
cp ".build/release/ScreenTranslator" "$APP/Contents/MacOS/ScreenTranslator"
cp "Support/Info.plist" "$APP/Contents/Info.plist"
codesign --force --sign - "$APP"
echo "Built $APP"

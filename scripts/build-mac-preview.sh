#!/bin/sh
set -eu
export AVALONIA_TELEMETRY_OPTOUT=1
task_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
bundle="${MAC_BUNDLE_ROOT:-$task_root/dist/macos-arm64/雾锁存档助手.app}"
mkdir -p "$bundle/Contents/MacOS" "$bundle/Contents/Resources"
"${DOTNET:-dotnet}" publish "$task_root/client/SaveDesktop/SaveDesktop.csproj" -c Release -r osx-arm64 --self-contained true -o "$bundle/Contents/MacOS" --nologo
(
  cd "$task_root/server"
  CGO_ENABLED=0 GOOS=darwin GOARCH=arm64 go build -trimpath -ldflags='-s -w' -o "$bundle/Contents/MacOS/save-server" ./cmd/save-server
)
cat > "$bundle/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleName</key><string>雾锁存档助手</string>
<key>CFBundleDisplayName</key><string>雾锁存档助手</string>
<key>CFBundleIdentifier</key><string>local.enshrouded.save-preview</string>
<key>CFBundleExecutable</key><string>EnshroudedSaveManager</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleVersion</key><string>1.1.0</string>
<key>CFBundleShortVersionString</key><string>1.1.0</string>
<key>NSHighResolutionCapable</key><true/>
<key>LSMinimumSystemVersion</key><string>12.0</string>
</dict></plist>
PLIST
if command -v codesign >/dev/null 2>&1; then
  codesign --force --deep --sign - "$bundle"
fi

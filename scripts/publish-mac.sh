#!/bin/bash
# Build an unsigned, self-contained Hugoer.app and a compressed DMG.
# Usage: ./scripts/publish-mac.sh [version]
set -euo pipefail

VERSION="${1:-1.9.0}"
if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+([-+][0-9A-Za-z.-]+)?$ ]]; then
  echo "Version 必須是語意版本（例如 1.9.0）：$VERSION" >&2
  exit 1
fi

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
RID="osx-arm64"
DIST="$ROOT/dist"
PUBLISH="$DIST/publish/$RID"
STAGE="$DIST/dmg-src"
APP="$STAGE/Hugoer.app"
RELEASE="$DIST/releases"
DMG="$RELEASE/Hugoer-$VERSION-$RID.dmg"

export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"

rm -rf "$PUBLISH" "$STAGE"
mkdir -p "$PUBLISH" "$RELEASE" "$APP/Contents/MacOS" "$APP/Contents/Resources"

echo "==> dotnet publish ($RID) v$VERSION"
dotnet publish "$ROOT/Hugoer.csproj" \
  -c Release \
  -r "$RID" \
  --self-contained true \
  -p:PublishSingleFile=false \
  -p:Version="$VERSION" \
  -p:AssemblyVersion="$VERSION.0" \
  -p:FileVersion="$VERSION.0" \
  -p:InformationalVersion="$VERSION" \
  -p:ContinuousIntegrationBuild=true \
  -p:DebugType=embedded \
  -o "$PUBLISH"

if [[ ! -x "$PUBLISH/Hugoer" && ! -f "$PUBLISH/Hugoer" ]]; then
  echo "找不到 $PUBLISH/Hugoer" >&2
  exit 1
fi

cp -R "$PUBLISH"/. "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/Hugoer"
cp "$ROOT/branding/hugoer.icns" "$APP/Contents/Resources/hugoer.icns"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>
  <string>Hugoer</string>
  <key>CFBundleDisplayName</key>
  <string>Hugoer</string>
  <key>CFBundleIdentifier</key>
  <string>com.huang1988pioneer.hugoer</string>
  <key>CFBundleVersion</key>
  <string>$VERSION</string>
  <key>CFBundleShortVersionString</key>
  <string>$VERSION</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleSignature</key>
  <string>????</string>
  <key>CFBundleExecutable</key>
  <string>Hugoer</string>
  <key>CFBundleIconFile</key>
  <string>hugoer</string>
  <key>CFBundleInfoDictionaryVersion</key>
  <string>6.0</string>
  <key>LSMinimumSystemVersion</key>
  <string>12.0</string>
  <key>NSHighResolutionCapable</key>
  <true/>
  <key>NSPrincipalClass</key>
  <string>NSApplication</string>
</dict>
</plist>
PLIST

ln -sfn /Applications "$STAGE/Applications"
rm -f "$DMG"
hdiutil create \
  -volname "Hugoer $VERSION" \
  -srcfolder "$STAGE" \
  -ov \
  -format UDZO \
  "$DMG"

echo "DMG: $DMG"

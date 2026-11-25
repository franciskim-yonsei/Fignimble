#!/bin/bash
# Build Fignimble.app next to this script (after Manimble's build_app.sh). FIGNIMBLE_ARCHS="arm64 x86_64" for a universal app.
set -euo pipefail
cd "$(dirname "$0")"
ROOT="$PWD"
if [[ -z "${DEVELOPER_DIR:-}" && -d /Applications/Xcode.app/Contents/Developer ]]; then
  export DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer
fi
mkdir -p "$ROOT/.build/module-cache"
export CLANG_MODULE_CACHE_PATH="$ROOT/.build/module-cache"
FIGNIMBLE_ARCHS="${FIGNIMBLE_ARCHS:-$(uname -m)}"
SLICES=()
for arch in $FIGNIMBLE_ARCHS; do
  ARGS=(-c release --triple "$arch-apple-macosx13.0" --force-resolved-versions)
  swift build "${ARGS[@]}"
  SLICES+=("$(swift build "${ARGS[@]}" --show-bin-path)/Fignimble")
done
mkdir -p "$ROOT/.build/universal"
lipo -create "${SLICES[@]}" -output "$ROOT/.build/universal/Fignimble"
VERSION="$(cat "$ROOT/../../VERSION")"
APP="$ROOT/Fignimble.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources/bin"
cp "$ROOT/.build/universal/Fignimble" "$APP/Contents/MacOS/Fignimble"
rsync -a --exclude __pycache__ "$ROOT/../../engine/" "$APP/Contents/Resources/engine/"
install -m 755 "$ROOT/../../bin/fignimble" "$APP/Contents/Resources/bin/fignimble"
rsync -a "$ROOT/../../skills/" "$APP/Contents/Resources/skills/"   # agent skills, for `fignimble install-skills`
cp "$ROOT/assets/biocartoon.png" "$ROOT/assets/biocartoon-mono.png" "$APP/Contents/Resources/"   # BioCartoon's logos (from Manimble)
cp "$ROOT/assets/Fignimble.icns" "$APP/Contents/Resources/fignimble.icns"   # the app icon (tools/make_icon.swift)
install -m 755 "$ROOT/setup_python.sh" "$APP/Contents/Resources/setup_python.sh"   # the first-launch Python setup
cat > "$APP/Contents/Info.plist" <<PL
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleName</key><string>Fignimble</string>
<key>CFBundleDisplayName</key><string>Fignimble</string>
<key>CFBundleIdentifier</key><string>org.pachterlab.Fignimble</string>
<key>CFBundleExecutable</key><string>Fignimble</string>
<key>CFBundleIconFile</key><string>fignimble</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleVersion</key><string>$VERSION</string>
<key>CFBundleShortVersionString</key><string>$VERSION</string>
<key>LSMinimumSystemVersion</key><string>13.0</string>
<key>NSHighResolutionCapable</key><true/>
<key>CFBundleDocumentTypes</key><array>
  <dict><key>CFBundleTypeName</key><string>Fignimble Project Folder</string><key>CFBundleTypeRole</key><string>Editor</string>
    <key>LSHandlerRank</key><string>Alternate</string><key>LSItemContentTypes</key><array><string>public.folder</string></array></dict>
  <dict><key>CFBundleTypeName</key><string>Figure Script</string><key>CFBundleTypeRole</key><string>Editor</string>
    <key>LSHandlerRank</key><string>Alternate</string><key>LSItemContentTypes</key><array><string>public.python-script</string></array></dict>
</array>
</dict></plist>
PL
# A "Developer ID Application" identity (release builds, FIGNIMBLE_SIGN_IDENTITY) signs with the hardened runtime and a
# secure timestamp, as notarization requires; a failure then stops the build. Otherwise an ad-hoc signature.
SIGN_ID="${FIGNIMBLE_SIGN_IDENTITY:-}"
if [[ "$SIGN_ID" == "Developer ID Application"* ]]; then
  codesign -s "$SIGN_ID" --force --options runtime --timestamp --entitlements "$ROOT/Fignimble.entitlements" "$APP"
  codesign --verify --strict --verbose=2 "$APP"
else
  codesign -s - --force "$APP" >/dev/null 2>&1 || true
fi
echo "built $APP ($(lipo -archs "$APP/Contents/MacOS/Fignimble"))"

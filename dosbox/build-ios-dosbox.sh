#!/bin/bash
# Build a one-tap "Ultima V in DOSBox" app for your iPhone/iPad and install it,
# with YOUR copy of the game.
#
# This is the *complete* Ultima V (the real DOS game running in DOSBox), unlike
# the native front-end in this repo. It's built on litchie/dospad (the open-source
# iOS DOSBox, GPLv2) — cloned + patched at build time, not re-hosted here — and
# your own Ultima V data (never committed).
#
# Prereqs:
#   - Xcode (signed in with the Apple ID that owns your team).
#   - iPhone/iPad connected (cable or same Tailscale/Wi-Fi), unlocked, "Trust"
#     accepted, Developer Mode on.
#   - Your Ultima V game folder (the one with ULTIMA.EXE + TILES.16). On a Mac GOG
#     install that's usually /Applications/Ultima V™.app/Contents/Resources/game
#
# Usage: dosbox/build-ios-dosbox.sh <AppleTeamID> [/path/to/ultimaV/gamedata]
#   e.g. dosbox/build-ios-dosbox.sh ABCDE12345
#
# Find your Team ID: security find-identity -v -p codesigning (the code in parens).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
TEAM="${1:?Usage: build-ios-dosbox.sh <AppleTeamID> [ultimaV-data-dir]}"
U5_SRC="${2:-/Applications/Ultima V™.app/Contents/Resources/game}"
BUNDLE_ID="${U5DOS_BUNDLE_ID:-info.u5redux.u5dos}"
WORK="${HOME}/Library/Caches/u5-dosbox"
DOSPAD="$WORK/dospad"

if ! [[ "$TEAM" =~ ^[A-Za-z0-9]{10}$ ]]; then
  echo "ERROR: '$TEAM' is not a valid Apple Team ID (10 letters/digits)." >&2
  exit 1
fi
if [ ! -f "$U5_SRC/ULTIMA.EXE" ] && [ ! -f "$U5_SRC/ultima.exe" ]; then
  echo "ERROR: no Ultima V data at:" >&2
  echo "  $U5_SRC" >&2
  echo "Pass the folder with ULTIMA.EXE + TILES.16 as the 2nd argument." >&2
  exit 1
fi

mkdir -p "$WORK"

# 1. Clone the open-source iOS DOSBox (GPLv2).
if [ ! -d "$DOSPAD/.git" ]; then
  echo "Cloning dospad (iOS DOSBox, litchie) ..."
  git clone --depth 1 https://github.com/litchie/dospad.git "$DOSPAD"
fi
PROJ="$DOSPAD/dospad.xcodeproj/project.pbxproj"

# 2. Rebrand the bundle id to yours (covers the app + its thumbnail extension).
if grep -q "com.litchie.idos3" "$PROJ"; then
  sed -i '' "s/com\.litchie\.idos3/$BUNDLE_ID/g" "$PROJ"
fi

# 3. Patch: auto-run ULTIMA.EXE from the C-drive root on launch (one-tap boot).
EMU="$DOSPAD/dospad/Main/DOSPadEmulator.m"
if ! grep -q "Ultima V one-tap" "$EMU"; then
  echo "Patching dospad to auto-run Ultima V ..."
  python3 - "$EMU" <<'PY'
import sys
p = sys.argv[1]
s = open(p).read()
marker = '[self.commandList addObject:@"REM END AUTOMOUNT"];'
inject = marker + '''

    // Ultima V one-tap boot: if ULTIMA.EXE is present at the C drive root, run it
    // directly (dedicated-app behaviour, independent of package-type detection).
    {
        DPDrive *cDrive = [self.package findDrive:'C'];
        if (cDrive) {
            NSString *u5exe = [cDrive.sourceUrl.path stringByAppendingPathComponent:@"ULTIMA.EXE"];
            if ([[NSFileManager defaultManager] fileExistsAtPath:u5exe]) {
                [self.commandList addObject:@"C:"];
                [self.commandList addObject:@"ULTIMA.EXE"];
            }
        }
    }'''
assert marker in s, "anchor not found in DOSPadEmulator.m"
open(p, "w").write(s.replace(marker, inject, 1))
print("patched")
PY
fi

# 3b. Ultima V branding: swap the app icon (gold ankh) and rename to "Ultima V".
MASTER="$SCRIPT_DIR/ultima5-icon.png"
ICON_SET="$DOSPAD/Resources/Assets.xcassets/AppIcon.appiconset"
if [ -f "$MASTER" ] && [ -d "$ICON_SET" ]; then
  echo "Applying the Ultima V app icon ..."
  for f in "$ICON_SET"/icon-*.png; do
    n="$(basename "$f" .png | sed 's/icon-//')"          # pixel size from filename
    [[ "$n" =~ ^[0-9]+$ ]] && sips -s format png -z "$n" "$n" "$MASTER" --out "$f" >/dev/null 2>&1 || true
  done
  [ -f "$ICON_SET/iTunesArtwork@2x.png" ] && \
    sips -s format png -z 1024 1024 "$MASTER" --out "$ICON_SET/iTunesArtwork@2x.png" >/dev/null 2>&1 || true
fi
plutil -replace CFBundleDisplayName -string "Ultima V" "$DOSPAD/Resources/iDOS-Info.plist" 2>/dev/null || true

# 3c. Enable sound. dospad's stock config comes up silent for U5's music; turn on the
#     mixer (44.1 kHz), Sound Blaster Pro + OPL, and the PC speaker so the intro music
#     and in-game effects play. machine stays svga_s3 (NOT tandy — tandy silences audio
#     in dospad's iOS build). Edits dospad's bundled config in place, preserving its
#     [gamepad.keybinding] section (the on-screen controls). dospad copies this config
#     into the app's Documents on first launch, so a fresh install has sound automatically.
CFGSRC="$DOSPAD/Resources/configs/dospad.cfg"
if [ -f "$CFGSRC" ]; then
  echo "Enabling sound in the DOSBox config ..."
  python3 - "$CFGSRC" <<'PY'
import sys, re
p = sys.argv[1]; s = open(p).read()
s = re.sub(r'\[dosbox\]\n', '[dosbox]\nmachine=svga_s3\nmemsize=16\n', s, count=1)
if '[mixer]' not in s:
    s = s.replace('[sblaster]', '[mixer]\nnosound=false\nrate=44100\n[sblaster]', 1)
s = re.sub(r'\[sblaster\]\n', '[sblaster]\nsbtype=sbpro1\noplmode=auto\n', s, count=1)
s = re.sub(r'\[speaker\]\n', '[speaker]\npcspeaker=true\n', s, count=1)
open(p, 'w').write(s)
print("  sound enabled (svga_s3, SB Pro + OPL, PC speaker, mixer 44.1 kHz)")
PY
fi

# 4. Build + sign.
echo "Building (this takes a few minutes the first time) ..."
xattr -cr "$DOSPAD" 2>/dev/null || true
xcodebuild -project "$DOSPAD/dospad.xcodeproj" -scheme iDOS -configuration Release \
  -sdk iphoneos -destination 'generic/platform=iOS' -derivedDataPath "$DOSPAD/dd" \
  -allowProvisioningUpdates DEVELOPMENT_TEAM="$TEAM" CODE_SIGN_STYLE=Automatic build
APP="$(find "$DOSPAD/dd/Build/Products" -name 'iDOS.app' -type d | head -1)"
xattr -cr "$APP" 2>/dev/null || true
echo "Signed app: $APP"

# 5. Find the device.
DEVICE_ID="$(xcrun devicectl list devices 2>/dev/null \
  | grep -iE 'iPhone|iPad' | grep -i 'available' | grep -vi 'unavailable' \
  | grep -oiE '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}' | head -1)"
if [ -z "${DEVICE_ID:-}" ]; then
  echo "No available device found. Connect+unlock your iPhone (accept Trust) and re-run." >&2
  exit 1
fi

# 6. Install the app.
echo "Installing to $DEVICE_ID ..."
xcrun devicectl device install app --device "$DEVICE_ID" "$APP"

# 7. Stage YOUR U5 data (+ an idos.json) and push it into the app's Documents
#    (which DOSBox mounts as drive C). None of this is committed to the repo.
STAGE="$WORK/u5boot"
rm -rf "$STAGE"; mkdir -p "$STAGE"
cp "$U5_SRC"/* "$STAGE/" 2>/dev/null || find "$U5_SRC" -maxdepth 1 -type f -exec cp {} "$STAGE/" \;
rm -f "$STAGE/manual.pdf" "$STAGE"/dosbox*.conf 2>/dev/null || true
printf '{\n  "name": "Ultima V",\n  "autorun": "ULTIMA.EXE"\n}\n' > "$STAGE/idos.json"
echo "Copying your Ultima V data onto the device ..."
xcrun devicectl device copy to --device "$DEVICE_ID" --user mobile \
  --domain-type appDataContainer --domain-identifier "$BUNDLE_ID" \
  --source "$STAGE" --destination "Documents" || \
  echo "  (Data copy reported an issue — if the app boots to C:\\ , re-run this step.)"

# 8. Launch — boots straight into Ultima V.
xcrun devicectl device process launch --terminate-existing --device "$DEVICE_ID" "$BUNDLE_ID" || true
echo
echo "Done. First run: trust the developer once under Settings > General >"
echo "  VPN & Device Management, then reopen. It boots straight into Ultima V."

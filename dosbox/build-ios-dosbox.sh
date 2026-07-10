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
# Team ID is optional — if omitted, auto-detect it from your installed Apple Development
# certificate so most people can just run `dosbox/build-ios-dosbox.sh`.
TEAM="${1:-}"
if [ -z "$TEAM" ]; then
  TEAM="$(security find-identity -v -p codesigning 2>/dev/null \
          | sed -nE 's/.*Apple Development:.*\(([A-Z0-9]{10})\).*/\1/p' | head -1)"
  if [ -n "$TEAM" ]; then
    echo "Auto-detected Apple Team ID: $TEAM  (pass one as the 1st argument to override)"
  else
    echo "ERROR: no Apple Team ID given and none could be auto-detected." >&2
    echo "Pass your 10-char Team ID:  dosbox/build-ios-dosbox.sh <TeamID>" >&2
    echo "Find it:  security find-identity -v -p codesigning   (the code in parentheses)" >&2
    exit 1
  fi
fi
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

# 2b. Strip the Thumbnail app-extension so a FREE Apple ID only has to sign ONE target.
#     dospad bundles an "iDOSThumbnail" app-extension (it draws Files thumbnails). A paid
#     account signs it fine, but a free "Personal Team" must provision *every* target
#     separately — and doing that from the command line isn't supported, which is exactly
#     what makes people think they need the $99 Developer Program. Removing the extension
#     from the app's build graph (its embed phase + target dependencies; the extension
#     target itself is just left orphaned/unbuilt) lets the app build & sign on its own.
#     Set U5DOS_KEEP_THUMBNAIL=1 to keep it (e.g. you have a paid account and want the
#     Files thumbnails). This only edits references, and aborts untouched if anything
#     unexpected is found, so it's safe.
if [ -z "${U5DOS_KEEP_THUMBNAIL:-}" ]; then
  echo "Stripping the Thumbnail app-extension (so a free Apple ID signs just one target) ..."
  python3 - "$PROJ" <<'PY'
import re, sys
p = sys.argv[1]; s = open(p).read()

def sec(name):
    m = re.search(r'/\* Begin %s section \*/(.*?)/\* End %s section \*/' % (name, name), s, re.S)
    return m.group(1) if m else ""

OBJ = r'\n\t\t([0-9A-F]{24}) /\* (.*?) \*/ = \{(.*?)\n\t\t\};'   # one object, boundary-respecting

ext_targets = {m.group(1) for m in re.finditer(OBJ, sec("PBXNativeTarget"), re.S)
               if 'product-type.app-extension' in m.group(3)}
if not ext_targets:
    print("  no app-extension target present — nothing to strip"); sys.exit(0)
ext_dep_ids = {m.group(1) for m in re.finditer(OBJ, sec("PBXTargetDependency"), re.S)
               if any(t in m.group(3) for t in ext_targets)}
embed_ids = {m.group(1) for m in re.finditer(OBJ, sec("PBXCopyFilesBuildPhase"), re.S)
             if 'dstSubfolderSpec = 13' in m.group(3) and '.appex' in m.group(3)}
kill = ext_dep_ids | embed_ids

nm = re.search(r'/\* Begin PBXNativeTarget section \*/(.*?)/\* End PBXNativeTarget section \*/', s, re.S)
found = [0]; removed = [0]
def fix(mo):
    obj = mo.group(0)
    if 'product-type.application"' not in obj:      # only the main app target
        return obj
    found[0] += 1; out = []
    for line in obj.split("\n"):
        rid = re.search(r'([0-9A-F]{24})', line)
        if rid and rid.group(1) in kill and ('/* PBXTargetDependency */' in line
                                              or 'Embed App Extensions */,' in line):
            removed[0] += 1; continue            # drop this reference line only
        out.append(line)
    return "\n".join(out)
new = re.sub(OBJ, fix, nm.group(1), flags=re.S)
if found[0] != 1:
    sys.stderr.write("  WARN: expected 1 application target, found %d — leaving project untouched\n" % found[0]); sys.exit(0)
s = s[:nm.start(1)] + new + s[nm.end(1):]
if s.count('{') != s.count('}'):
    sys.stderr.write("  WARN: brace mismatch after edit — leaving project untouched\n"); sys.exit(0)
open(p, "w").write(s)
print("  removed %d extension reference(s) from the app target" % removed[0] if removed[0]
      else "  Thumbnail extension already stripped")
PY
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
if 'machine=svga_s3' in s:            # idempotent: already patched, leave as-is
    print("  sound already enabled"); sys.exit(0)
s = re.sub(r'\[dosbox\]\n', '[dosbox]\nmachine=svga_s3\nmemsize=16\n', s, count=1)
if '[mixer]' not in s:
    s = s.replace('[sblaster]', '[mixer]\nnosound=false\nrate=44100\n[sblaster]', 1)
s = re.sub(r'\[sblaster\]\n', '[sblaster]\nsbtype=sbpro1\noplmode=auto\n', s, count=1)
s = re.sub(r'\[speaker\]\n', '[speaker]\npcspeaker=true\n', s, count=1)
open(p, 'w').write(s)
print("  sound enabled (svga_s3, SB Pro + OPL, PC speaker, mixer 44.1 kHz)")
PY
fi

# 3d. Ultima-optimized touch keyboard. dospad's stock on-screen keyboard is a cramped
#     47-key QWERTY. Ship a custom layout instead: a big movement D-pad + the common
#     U5 command keys (Attack/Talk/Open/Get/Jimmy/Look/Cast/Klimb/Board) + a utility row
#     (⌨/Esc/↵/Pass/Yes/No). The ⌨ key (key-fn) flips to a full QWERTY variant for
#     conversations, and back. Copy the layouts in and point the landscape scenes at them.
KBD_SRC="$SCRIPT_DIR/keyboard"
if [ -d "$KBD_SRC" ] && [ -f "$KBD_SRC/kbdultima_land.json" ]; then
  echo "Installing the Ultima touch keyboard + removing the DOSBox toolbar ..."
  cp "$KBD_SRC/kbdultima_land.json" "$KBD_SRC/kbdultima_land_fn.json" "$DOSPAD/Resources/configs/"
  SCENES="$DOSPAD/Resources/default.idostheme/scenes"
  python3 - "$SCENES" <<'PY'
import sys, os, json, glob
scenes = sys.argv[1]
# The iPhone/iPad landscape scenes are clean JSON (gamepad-* use trailing commas — skip).
for path in glob.glob(os.path.join(scenes, "iphone-landscape-*.json")) + \
            [os.path.join(scenes, "ipad-landscape.json")]:
    if not os.path.exists(path):
        continue
    try:
        d = json.load(open(path))
    except Exception as e:
        print("  skip %s (%s)" % (os.path.basename(path), e)); continue
    d["keyboard"] = "kbdultima_land"                 # use the Ultima command layout
    nodes = d.get("nodes", [])
    # Drop the "landbar"/"portbar" toolbar (power button, cycles readout, mount/HDD icon).
    # (The game screen is shrunk to sit above the keyboard in code — see the updateScreen
    # patch in 3f — because landscape ignores the scene's screen-node frame.)
    kept = [n for n in nodes if n.get("type") not in ("landbar", "portbar")]
    d["nodes"] = kept
    json.dump(d, open(path, "w"), indent=2)
    print("  %s: keyboard=kbdultima_land, toolbar removed (%d->%d nodes)"
          % (os.path.basename(path), len(nodes), len(kept)))
PY
fi

# 3e. Auto-show the on-screen keyboard in landscape. With the toolbar removed (3d), the
#     keyboard toggle button is gone too — so show the Ultima keyboard automatically when
#     a landscape scene is built, giving a clean "game + controls" UI with no DOS chrome.
VC="$DOSPAD/dospad/Main/DPEmulatorViewController.m"
if [ -f "$VC" ] && ! grep -q "Ultima auto-keyboard" "$VC"; then
  echo "Patching dospad to auto-show the Ultima keyboard ..."
  python3 - "$VC" <<'PY'
import sys
p = sys.argv[1]; s = open(p).read()
anchor = "\t_rootContainer = [self createSceneView:_currentScene frame:viewRect];\n\t[self.view addSubview:_rootContainer];\n\t[self updateScreen];"
inject = anchor + """
\t// Ultima auto-keyboard: the DOSBox toolbar (with its keyboard toggle) is removed for a
\t// clean game UI, so bring up the on-screen keyboard automatically in landscape, then
\t// re-run updateScreen so the game shrinks to sit above it (see reserve-keyboard patch).
\tif (!_currentScene.isPortrait) {
\t\t[self createFloatingInput:TAG_INPUT_KEYBOARD];
\t\t[self updateScreen];
\t}"""
assert anchor in s, "auto-keyboard anchor not found"
open(p, "w").write(s.replace(anchor, inject, 1))
print("  auto-keyboard patched")
PY
fi

# 3f. Lock the app to landscape. Ultima V is a 4:3 landscape game, and only the landscape
#     scenes get the clean Ultima UI (3d/3e). Forcing landscape means the player always
#     gets the D-pad + command keyboard with no DOS toolbar, regardless of how they hold
#     the phone. Patch the VC's supportedInterfaceOrientations + the Info.plist.
VC="$DOSPAD/dospad/Main/DPEmulatorViewController.m"
if [ -f "$VC" ]; then
  # supportedInterfaceOrientations returns MaskAll -> MaskLandscape (in this VC only).
  perl -0pi -e 's/(- \(UIInterfaceOrientationMask\)supportedInterfaceOrientations\s*\{\s*\n\s*return )UIInterfaceOrientationMaskAll;/${1}UIInterfaceOrientationMaskLandscape;/' "$VC"
  # updateScreen: when the on-screen keyboard is present, shrink the DOS screen to the
  # area ABOVE it (reserve its height) so the game stays fully visible — the U6 behavior
  # where the game lifts + resizes instead of being covered. Landscape ignores the scene's
  # screen frame and scales to the full bounds, so this must be done here.
  if ! grep -q "Ultima reserve-keyboard" "$VC"; then
    python3 - "$VC" <<'PY'
import sys
p = sys.argv[1]; s = open(p).read()
anchor = "\telse\n\t{\n\t\tif (shouldShrinkScreen)"
inject = ("\telse\n\t{\n"
          "\t\t// Ultima reserve-keyboard: keep the game above the always-on keyboard.\n"
          "\t\tif ([self findInputView:TAG_INPUT_KEYBOARD] != nil) {\n"
          "\t\t\tviewRect.size.height -= (ISIPAD() ? 250 : 175);\n"
          "\t\t\t[self putScreen:viewRect scaleMode:DPScreenScaleModeAspectFit4x3];\n"
          "\t\t\treturn;\n"
          "\t\t}\n"
          "\t\tif (shouldShrinkScreen)")
assert anchor in s, "reserve-keyboard anchor not found"
open(p, "w").write(s.replace(anchor, inject, 1))
print("  reserve-keyboard patched")
PY
  fi
fi
plutil -replace UISupportedInterfaceOrientations -json '["UIInterfaceOrientationLandscapeLeft","UIInterfaceOrientationLandscapeRight"]' "$DOSPAD/Resources/iDOS-Info.plist" 2>/dev/null || true

# 4. Build + sign.
echo "Building (this takes a few minutes the first time) ..."
xattr -cr "$DOSPAD" 2>/dev/null || true
if ! xcodebuild -project "$DOSPAD/dospad.xcodeproj" -scheme iDOS -configuration Release \
  -sdk iphoneos -destination 'generic/platform=iOS' -derivedDataPath "$DOSPAD/dd" \
  -allowProvisioningUpdates DEVELOPMENT_TEAM="$TEAM" CODE_SIGN_STYLE=Automatic build; then
  cat >&2 <<EOF

──────────────────────────────────────────────────────────────────────
The command-line build/sign failed. This is almost always a FREE Apple ID:
free "Personal Team" accounts can't create signing profiles from the
terminal — but they CAN, for free, from the Xcode app. You do NOT need the
\$99 Developer Program. Do this once (takes 2 minutes):

  1. Open the project:
       open "$DOSPAD/dospad.xcodeproj"
  2. Xcode ▸ Settings ▸ Accounts → add your free Apple ID.
  3. Select the "iDOS" target ▸ Signing & Capabilities ▸ set Team to your
     Personal Team. (The Thumbnail extension is already stripped, so there's
     just this ONE target to sign — the usual free-account snag is gone.)
  4. Plug in your iPhone/iPad (unlocked, Developer Mode on) and press Run ▶.
     Xcode registers the device for FREE and installs the app.
  5. Then re-run this script to copy your Ultima V data onto the device
     (or just relaunch the app — it boots straight into the game).
──────────────────────────────────────────────────────────────────────
EOF
  exit 1
fi
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

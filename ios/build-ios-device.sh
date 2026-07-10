#!/bin/bash
# Build + sign Ultima V (MonoGame front-end on the MIT Ultima5Redux logic library)
# for a physical iPhone/iPad and install it.
#
# Ultima V is NOT free — you must own it. This script copies YOUR OWN copy of the
# game data into the app; none is included in this repo. The MIT logic library
# (bradhannah/Ultima5Redux) is cloned at build time, not re-hosted here.
#
# Prereqs:
#   - Xcode 15.4 (iOS 17.5 SDK), signed in with the Apple ID that owns your team.
#   - .NET 8 SDK + the iOS 17 workload (the script installs the SDK if missing and
#     tells you how to add the workload).
#   - iPhone connected (cable or same Tailscale/Wi-Fi), unlocked, "Trust" accepted,
#     Developer Mode on (Settings > Privacy & Security > Developer Mode).
#   - Your Ultima V game folder (the one with TILES.16 + DATA.OVL). On a Mac GOG
#     install that's usually:  /Applications/Ultima V™.app/Contents/Resources/game
#
# Usage: ios/build-ios-device.sh <AppleTeamID> [/path/to/ultimaV/gamedata]
#   e.g. ios/build-ios-device.sh ABCDE12345
#
# Your Team ID is a 10-character code (e.g. ABCDE12345), NOT your name. Find it at
# https://developer.apple.com/account -> Membership details -> Team ID, or run
#   security find-identity -v -p codesigning   (it's the code in parentheses).
set -euo pipefail

REPO="$(cd "$(dirname "$0")/.." && pwd)"
TEAM="${1:?Usage: build-ios-device.sh <AppleTeamID> [ultimaV-data-dir]}"
U5_SRC="${2:-/Applications/Ultima V™.app/Contents/Resources/game}"

BUNDLE_ID="${U5_IOS_BUNDLE_ID:-info.u5redux.ultima5}"
REDUX_REPO="https://github.com/bradhannah/Ultima5Redux.git"
REDUX_COMMIT="a55fab9"                 # pinned; the modernized .csproj matches this
WORK="${HOME}/Library/Caches/u5-build" # non-iCloud (iCloud FinderInfo breaks codesign)

if ! [[ "$TEAM" =~ ^[A-Za-z0-9]{10}$ ]]; then
  echo "ERROR: '$TEAM' is not a valid Apple Team ID (10 letters/digits)." >&2
  echo "  Pass ONLY the code, not your name (security find-identity -v -p codesigning)." >&2
  exit 1
fi

# 0. .NET 8 SDK (user-local; the brew cask needs sudo).
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"
if ! command -v dotnet >/dev/null 2>&1 || [ ! -x "$DOTNET_ROOT/dotnet" ]; then
  echo "Installing the .NET 8 SDK into $DOTNET_ROOT ..."
  curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 --install-dir "$DOTNET_ROOT"
fi
if ! dotnet workload list 2>/dev/null | grep -qi '^ios'; then
  echo "Installing the .NET iOS workload..."
  dotnet workload install ios || true
fi
# Match the workload to the installed Xcode. Xcode 15.x ships the iOS 17.x SDK and
# needs the iOS 17 workload; the current default workload targets iOS 18 / Xcode 16
# and fails to build against Xcode 15.4. Pin it when we detect Xcode 15.
XCODE_VER="$(xcodebuild -version 2>/dev/null | awk '/^Xcode/{print $2}')"
if [[ "$XCODE_VER" == 15.* ]] && ! dotnet workload list 2>/dev/null | grep -qi '17\.0\.8478'; then
  echo "Xcode $XCODE_VER detected -> pinning the .NET iOS 17 workload (17.0.8478)."
  RB="$(mktemp -t ios17rollback).json"
  printf '{"microsoft.net.sdk.ios":"17.0.8478/8.0.100"}' > "$RB"
  dotnet workload update --from-rollback-file "$RB" || \
    echo "  (Could not auto-pin; if the build fails, see the README toolchain notes.)"
  rm -f "$RB"
fi

# 1. The MIT logic library (cloned + pinned; drop in the net8 project file).
if [ ! -d "$REPO/u5redux/.git" ]; then
  echo "Cloning the Ultima5Redux logic library (MIT, bradhannah) ..."
  git clone "$REDUX_REPO" "$REPO/u5redux"
  git -C "$REPO/u5redux" checkout "$REDUX_COMMIT"
fi
cp "$REPO/ios/Ultima5Redux.net8.csproj" "$REPO/u5redux/Ultima5Redux/Ultima5Redux.csproj"

# 2. Your Ultima V game data (copied from YOUR copy; never committed).
if [ ! -f "$U5_SRC/TILES.16" ] && [ ! -f "$U5_SRC/tiles.16" ]; then
  echo "ERROR: no Ultima V data at:" >&2
  echo "  $U5_SRC" >&2
  echo "Pass the folder that contains TILES.16 + DATA.OVL as the 2nd argument." >&2
  exit 1
fi
mkdir -p "$REPO/u5data"
cp -f "$U5_SRC"/* "$REPO/u5data/" 2>/dev/null || true
if [ ! -f "$REPO/u5data/TILES.16" ] && [ ! -f "$REPO/u5data/tiles.16" ]; then
  # case-insensitive copy fallback
  find "$U5_SRC" -maxdepth 1 -type f -exec cp -f {} "$REPO/u5data/" \;
fi
[ -f "$REPO/u5data/TILES.16" ] || { echo "ERROR: TILES.16 missing after copy." >&2; exit 1; }

# 3. Copy to the non-iCloud work dir and strip extended attributes.
echo "Staging build in $WORK ..."
mkdir -p "$WORK"
rsync -a --delete --exclude bin --exclude obj \
  "$REPO/U5Desktop" "$REPO/U5iOS" "$REPO/u5redux" "$REPO/u5data" "$WORK/"
xattr -cr "$WORK" 2>/dev/null || true

# 4. Build + sign.
CODESIGN_KEY="$(security find-identity -v -p codesigning | grep -m1 'Apple Development' | sed -E 's/.*"(.*)"/\1/')"
[ -n "$CODESIGN_KEY" ] || { echo "ERROR: no 'Apple Development' signing identity found. Sign in to Xcode first." >&2; exit 1; }
echo "Signing with: $CODESIGN_KEY  (team $TEAM)"
cd "$WORK/U5iOS"
dotnet build -c Release -r ios-arm64 \
  -p:CodesignKey="$CODESIGN_KEY" -p:CodesignProvision=Automatic -p:CodesignTeam="$TEAM"

APP="$(find "$WORK/U5iOS/bin/Release" -name 'U5iOS.app' -type d | head -1)"
echo; echo "Signed app: $APP"

# 5. Install + launch on the first available device.
DEVICE_ID="$(xcrun devicectl list devices 2>/dev/null \
  | awk '/available/ && /iPhone|iPad/ {print $3; exit}')"
if [ -n "${DEVICE_ID:-}" ]; then
  echo "Installing to $DEVICE_ID ..."
  xcrun devicectl device install app --device "$DEVICE_ID" "$APP"
  xcrun devicectl device process launch --terminate-existing --device "$DEVICE_ID" "$BUNDLE_ID" || \
    echo "Installed. (Launch failed — unlock the phone and tap the Ultima V icon.)"
  echo "First run: trust the developer once under Settings > General >"
  echo "  VPN & Device Management, then hold the phone in landscape."
else
  echo "No available device found. Connect+unlock your iPhone (and accept Trust) and re-run."
fi

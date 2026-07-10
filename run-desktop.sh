#!/bin/bash
# Run the Ultima V desktop build (Mac). Sets up the .NET SDK, clones the MIT logic
# library, and imports YOUR Ultima V data on first run.
#
# Usage: ./run-desktop.sh [/path/to/ultimaV/gamedata]
#   default data source: /Applications/Ultima V™.app/Contents/Resources/game
set -euo pipefail
REPO="$(cd "$(dirname "$0")" && pwd)"
U5_SRC="${1:-/Applications/Ultima V™.app/Contents/Resources/game}"
REDUX_COMMIT="a55fab9"

export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"; export PATH="$DOTNET_ROOT:$PATH"
if [ ! -x "$DOTNET_ROOT/dotnet" ]; then
  echo "Installing the .NET 8 SDK into $DOTNET_ROOT ..."
  curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 --install-dir "$DOTNET_ROOT"
fi

if [ ! -d "$REPO/u5redux/.git" ]; then
  echo "Cloning the Ultima5Redux logic library (MIT, bradhannah) ..."
  git clone https://github.com/bradhannah/Ultima5Redux.git "$REPO/u5redux"
  git -C "$REPO/u5redux" checkout "$REDUX_COMMIT"
fi
cp "$REPO/ios/Ultima5Redux.net8.csproj" "$REPO/u5redux/Ultima5Redux/Ultima5Redux.csproj"

if [ ! -f "$REPO/u5data/TILES.16" ]; then
  [ -f "$U5_SRC/TILES.16" ] || { echo "ERROR: no Ultima V data at '$U5_SRC' (need TILES.16 + DATA.OVL)." >&2; exit 1; }
  mkdir -p "$REPO/u5data"; cp -f "$U5_SRC"/* "$REPO/u5data/" 2>/dev/null || \
    find "$U5_SRC" -maxdepth 1 -type f -exec cp -f {} "$REPO/u5data/" \;
fi

export U5_DATA="$REPO/u5data"
cd "$REPO/U5Desktop"
dotnet run -c Release

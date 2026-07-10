# Ultima V on iOS

**Play Ultima V: Warriors of Destiny on your iPhone or iPad.** Two ways to do it:

| | What it is | Completeness | Feel |
|---|---|---|---|
| 🟢 **DOSBox** (recommended) | The **real DOS game** in DOSBox | **100% — the whole game** | On-screen keyboard |
| 🔵 **Native front-end** | A [MonoGame](https://monogame.net) UI on the MIT [Ultima5Redux](https://github.com/bradhannah/Ultima5Redux) engine | Partial (movement, towns, talk, combat, save…) | Custom touch UI |

> **You must own Ultima V.** It isn't free, so **no game data is included in this
> repo** — the build scripts copy your own copy in. The DOSBox app (litchie/dospad,
> GPLv2) and the MIT logic library are both cloned + patched at build time, not
> re-hosted here.

## 🟢 DOSBox — the complete game (recommended)

The fastest path to a **fully playable Ultima V** (combat, magic, shops, dungeons —
everything, because it *is* the real game). One command builds a one-tap app that
boots straight into your U5:

```sh
git clone https://github.com/dmaynard51/ultima5-ios.git
cd ultima5-ios
dosbox/build-ios-dosbox.sh ABCDE12345          # your 10-char Apple Team ID
```

It clones the iOS DOSBox, patches it to auto-run U5, builds/signs, installs, and
copies your Ultima V data onto the device. First run: trust the developer once under
**Settings ▸ General ▸ VPN & Device Management**, then reopen — it boots into the game.
Point it at your data folder with a 2nd argument if it isn't at the default GOG path.

## 🔵 Native front-end (work in progress)

A from-scratch touch UI on the MIT logic library — the authentic U5 screen (bordered
map window, party/stats console, on-screen D-pad + QWERTY keyboard). Movement, towns,
castles, ladders, NPC conversations, combat, and save/load work; magic, shops, and
dungeons don't yet.

### Install

Requires a **Mac** with **Xcode 15.4** and **git**. The scripts install the .NET 8
SDK for you (into `~/.dotnet`); you may need to add the iOS 17 workload once (see
*Toolchain* below).

**Desktop (Mac), to try it quickly:**

```sh
git clone https://github.com/dmaynard51/ultima5-ios.git
cd ultima5-ios
./run-desktop.sh                       # clones the library, imports your U5 data, runs
```

**On your iPhone/iPad** (needs a free Apple ID; pass your 10-char Team ID):

```sh
ios/build-ios-device.sh ABCDE12345
```

That clones the logic library, copies your Ultima V data, builds, signs, installs,
and launches. First run on the phone: trust the app once under **Settings ▸ General
▸ VPN & Device Management**, then hold it in **landscape**.

By default the scripts read your U5 data from
`/Applications/Ultima V™.app/Contents/Resources/game` (a Mac GOG install). If yours
is elsewhere, pass the folder (the one with `TILES.16` + `DATA.OVL`) as the last
argument:

```sh
ios/build-ios-device.sh ABCDE12345 "/path/to/your/ultimaV/game"
```

(Find your Team ID: `security find-identity -v -p codesigning` — the code in parentheses.)

## 🎮 Controls

- **D-pad** (right bezel) — move; also aims directional commands
- **KEYS** — open the on-screen QWERTY keyboard; **HIDE**/**ESC** closes it
- Commands: **L**ook, **O**pen, **S**earch, **G**et, **E**nter, **Z**stats;
  **SPACE** passes a turn; **ESC** cancels a half-entered command
- The whole game scales to fit above the keyboard — nothing is ever cropped

## 🧰 Toolchain notes

- Xcode **15.4** → iOS **17.5** SDK → .NET iOS workload **17.0.8478/8.0.100** +
  **MonoGame.Framework.iOS 3.8.1.303**. Newer MonoGame/workloads need Xcode 16.
- If `dotnet workload install ios` pulls an iOS 18 build Xcode 15.4 rejects, pin it:
  `dotnet workload update --from-rollback-file <(echo '{"microsoft.net.sdk.ios":"17.0.8478/8.0.100"}')`
- The library is generics-heavy, so the iOS head uses the Mono interpreter
  (`<UseInterpreter>` + `<MtouchInterpreter>all</MtouchInterpreter>`).
- The **iOS Simulator** doesn't work (an Apple-Silicon OpenGLES-on-Metal bug in
  MonoGame) — use a real device.

## ☕ Support this port

This iOS port is a free, open-source labor of love — building the renderer, decoding
the tile graphics, adding touch controls and the command UI, and keeping it working
takes real time. If it let you play Ultima V on your phone and you'd like to say
thanks, a coffee is hugely appreciated (and completely optional):

- ☕ **[Buy me a coffee (Ko-fi)](https://ko-fi.com/dmaynard)**
- 💜 **[GitHub Sponsors](https://github.com/sponsors/dmaynard51)**

## 🙏 Credits & license

- DOSBox app: **[dospad](https://github.com/litchie/dospad)** by litchie (GPLv2) —
  the iOS DOSBox behind the recommended route; cloned + patched at build time.
- Game logic + data parsing (native route): **[Ultima5Redux](https://github.com/bradhannah/Ultima5Redux)**
  by Brad Hannah (MIT) — cloned at build time.
- Font: **[font8x8](https://github.com/dhepper/font8x8)** (public domain / CC0).
- This front-end (the `U5Desktop` / `U5iOS` code and build scripts): MIT — see
  [LICENSE](LICENSE).
- *Ultima V* and its data are © Origin Systems / Electronic Arts. This project ships
  none of it; you bring your own legally-owned copy.

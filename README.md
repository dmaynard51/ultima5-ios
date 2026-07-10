# Ultima V on iOS

**Play Ultima V: Warriors of Destiny natively on your iPhone or iPad, with touch
controls** — a from-scratch [MonoGame](https://monogame.net) front-end built on the
MIT [Ultima5Redux](https://github.com/bradhannah/Ultima5Redux) logic library.

It recreates the authentic Ultima V screen: a bordered map window with the Avatar
dead-centre, a console panel (party / stats / command scroll), on-screen D-pad and a
QWERTY keyboard for commands.

> **You must own Ultima V.** Unlike Ultima IV, U5 is *not* free, so **no game data
> is included in this repo** — the build copies your own copy in. The MIT logic
> library is cloned from source at build time, not re-hosted here.

## 🚀 Install

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

- Game logic + data parsing: **[Ultima5Redux](https://github.com/bradhannah/Ultima5Redux)**
  by Brad Hannah (MIT) — cloned at build time.
- Font: **[font8x8](https://github.com/dhepper/font8x8)** (public domain / CC0).
- This front-end (the `U5Desktop` / `U5iOS` code and build scripts): MIT — see
  [LICENSE](LICENSE).
- *Ultima V* and its data are © Origin Systems / Electronic Arts. This project ships
  none of it; you bring your own legally-owned copy.

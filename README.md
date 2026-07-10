# Ultima V on iOS

**Play Ultima V: Warriors of Destiny on your iPhone or iPad** — the complete, original
DOS game, running in DOSBox, booting straight into the game with a gold-ankh icon.

It's built on [dospad](https://github.com/litchie/dospad) (the open-source iOS DOSBox,
GPLv2), cloned and patched at build time, plus **your own** copy of Ultima V.

> **You must own Ultima V.** It isn't free, so **no game data is included in this repo** —
> the build copies your own copy onto the device. The DOSBox app is cloned + patched at
> build time, not re-hosted here.

## 🚀 Install

Requires a **Mac** with **Xcode** and **git**.

**On your iPhone/iPad** (needs a free Apple ID; pass your 10-char Team ID):

```sh
git clone https://github.com/dmaynard51/ultima5-ios.git
cd ultima5-ios
dosbox/build-ios-dosbox.sh ABCDE12345
```

That's it — it clones the iOS DOSBox, patches it to auto-run Ultima V, brands it with
the ankh icon and the name "Ultima V", builds, signs, installs, and copies your game
data onto the device. First run on the phone: **trust the app once** under **Settings ▸
General ▸ VPN & Device Management**, then reopen — it boots straight into the game.

By default it reads your U5 data from `/Applications/Ultima V™.app/Contents/Resources/game`
(a Mac GOG install). If yours is elsewhere, pass the folder (the one with `ULTIMA.EXE`
+ `TILES.16`) as the last argument:

```sh
dosbox/build-ios-dosbox.sh ABCDE12345 "/path/to/your/ultimaV/game"
```

(Find your Team ID: `security find-identity -v -p codesigning` — the code in parentheses.)

## 🎮 Playing

Ultima V is keyboard-driven. dospad gives you an **on-screen keyboard** and gamepad
overlay — tap the keyboard toggle to type commands. Hold the phone in **landscape**.

## ☕ Support this port

Getting Ultima V running one-tap on your phone — the DOSBox patching, the ankh icon,
the auto-boot, and keeping it working — takes real time. If it let you play U5 on your
phone and you'd like to say thanks, a coffee is hugely appreciated (and optional):

- ☕ **[Buy me a coffee (Ko-fi)](https://ko-fi.com/dmaynard)**
- 💜 **[GitHub Sponsors](https://github.com/sponsors/dmaynard51)**

## 🙏 Credits & license

- iOS DOSBox: **[dospad](https://github.com/litchie/dospad)** by litchie (GPLv2) —
  cloned + patched at build time.
- Ankh app icon and the build script: MIT — see [LICENSE](LICENSE).
- *Ultima V* and its data are © Origin Systems / Electronic Arts. This project ships
  none of it; you bring your own legally-owned copy.

# Ultima V on iOS

**Play Ultima V: Warriors of Destiny on your iPhone or iPad** — the complete, original
DOS game, running in DOSBox, booting straight into the game with a gold-ankh icon.

It's built on [dospad](https://github.com/litchie/dospad) (the open-source iOS DOSBox,
GPLv2), cloned and patched at build time, plus **your own** copy of Ultima V.

> **You must own Ultima V.** It isn't free, so **no game data is included in this repo** —
> the build copies your own copy onto the device. The DOSBox app is cloned + patched at
> build time, not re-hosted here.

## 📸 Screenshots

The real Ultima V running in DOSBox on an iPhone — exploring Britannia and doing battle,
with the on-screen D-pad and command buttons:

<p align="center">
  <img src="screenshots/overworld.png" width="300" alt="Exploring Britannia" />
  &nbsp;&nbsp;
  <img src="screenshots/combat.png" width="300" alt="Combat" />
</p>

<p align="center">
  <img src="screenshots/combat-detail.png" width="480" alt="In combat with giant rats" />
</p>

## 🚀 Install

Requires a **Mac** with **Xcode** and **git** (no Mac? see [💻 No Mac?](#-no-mac-sideload-the-prebuilt-app) below). **No paid Apple Developer account ($99/yr)
is needed** — see [💸 No paid Apple account needed](#-no-paid-apple-account-needed) below.

**On your iPhone/iPad** (needs just a free Apple ID — one command):

```sh
git clone https://github.com/dmaynard51/ultima5-ios.git
cd ultima5-ios
dosbox/build-ios-dosbox.sh
```

That's it — it auto-detects your Apple Team ID, clones the iOS DOSBox, patches it to
auto-run Ultima V, brands it with the ankh icon and the name "Ultima V", **strips the
extra app-extension so a free Apple ID has just one target to sign**, builds, signs,
installs, and copies your game data onto the device. It boots straight into the game with
an on-screen D-pad + Ultima command keyboard and sound. First run on the phone: **trust
the app once** under **Settings ▸ General ▸ VPN & Device Management**, then reopen.

If auto-detect can't find your team, pass your 10-char Team ID as the first argument
(`security find-identity -v -p codesigning` — the code in parentheses):

```sh
dosbox/build-ios-dosbox.sh ABCDE12345
```

By default it reads your U5 data from `/Applications/Ultima V™.app/Contents/Resources/game`
(a Mac GOG install). If yours is elsewhere, pass the folder (the one with `ULTIMA.EXE`
+ `TILES.16`) as the last argument:

```sh
dosbox/build-ios-dosbox.sh ABCDE12345 "/path/to/your/ultimaV/game"
```

> **Hit a "$99 / register a device" wall in Xcode?** You don't need the paid program —
> that's just the command-line signer failing on a free account. See
> [💸 No paid Apple account needed](#-no-paid-apple-account-needed) below; the script now
> prints the exact free Xcode steps if CLI signing fails.

## 💸 No paid Apple account needed

**You do _not_ need the $99/year Apple Developer Program.** Installing an app you built
onto **your own** device is free with any Apple ID — this is Apple's "free provisioning."
If Xcode ever pushes you toward a paid membership to *"register a device,"* that's the
**command-line** signing path failing on a free account, **not** a real requirement: a
free Apple ID can't mint provisioning profiles from the terminal, only through the Xcode
app. Do the signing once in the **Xcode GUI** and it's completely free:

1. Run the build script once — it still clones + patches DOSBox even if signing fails
   (or just `git clone https://github.com/litchie/dospad.git`).
2. Open the project in Xcode:
   `~/Library/Caches/u5-dosbox/dospad/dospad.xcodeproj`
3. **Xcode ▸ Settings ▸ Accounts** → add your **free Apple ID**.
4. In **Signing & Capabilities** for the **iDOS** target, set **Team** to your *Personal
   Team* and give it a **unique Bundle Identifier** (e.g. `com.yourname.u5dos`). *(The
   build script removes dospad's Thumbnail app-extension by default, so there's only this
   one target to sign — the usual free-account snag. Pass `U5DOS_KEEP_THUMBNAIL=1` to keep
   it if you have a paid account.)*
5. Plug in your iPhone/iPad (unlocked, **Developer Mode on**) and press **Run ▶**. Xcode
   registers the device **for free** and installs the app.
6. On the device, trust the certificate under **Settings ▸ General ▸ VPN & Device
   Management**, then reopen — it boots straight into Ultima V.

> ⚠️ **The one real catch with a free Apple ID:** the app's signature **expires after 7
> days** — just press **Run ▶** again to re-install and renew it. (A paid account lasts a
> year; that's the only difference that matters here.) Free accounts are also capped at 3
> sideloaded apps at a time.

**Don't want to re-sign weekly?** Sideload with **[AltStore](https://altstore.io)** or
**[SideStore](https://sidestore.io)** — they sign with your free Apple ID and
auto-refresh the 7-day certificate in the background, no Mac needed after setup.

## 💻 No Mac? Install the prebuilt IPA

No Mac or Xcode needed — download the prebuilt app and sideload it from **Windows, Linux,
or Mac** with a **free Apple ID**.

**1. Get the app.** Download **[Ultima V.ipa](https://github.com/dmaynard51/ultima5-ios/releases/latest)** from Releases.

**2. Install it** (pick one):
- **[Sideloadly](https://sideloadly.io)** (Win/Mac/Linux) — plug in the phone, drag in the
  `.ipa`, enter your **free Apple ID**, click **Start**. Then trust the cert on the phone
  under **Settings ▸ General ▸ VPN & Device Management** and open the app.
- **[AltStore](https://altstore.io)** — same idea, and it auto-refreshes the 7-day signature
  over Wi-Fi so you don't have to re-sign weekly.
- **TrollStore** (no computer, *if* your iOS supports it) — just open the `.ipa` in the Files
  app and it installs permanently. In the EU, **AltStore PAL** also needs no computer.

**3. Add your own game files** (the app ships with **none**). You need your legally-owned
Ultima V DOS files — the folder that contains **`ULTIMA.EXE`** (e.g. from your GOG copy). Copy them
onto the phone by either:
- **Files app → On My iPhone → Ultima V** → paste your game files in, **or**
- **iTunes / Finder → phone → File Sharing → Ultima V** → drag the files into its Documents box.

> ⚠️ **Put the files LOOSE at the root — not inside a folder.** That Ultima V folder *is* the
> game's `C:\` drive, and the app auto-runs `ULTIMA.EXE` from `C:\`. If you drag a *folder* in
> (so the files land in `C:\YourFolder\`), the app opens to a bare **`C:\>`** prompt instead
> of the game. Fix: in the **Files** app, open that subfolder → **•••  ▸ Select ▸ Select All**
> → **Move** → up one level into the **Ultima V** folder.

**4. Open Ultima V** — it boots straight into the game (landscape, D-pad + command keyboard, sound).


## 🎮 Playing

The app runs in **landscape** and gives you a purpose-built Ultima control layout (no
fiddly DOS keyboard or toolbar):

- a **movement D-pad** on the left,
- the frequent **U5 commands** as labelled buttons — Attack, Talk, Open, Get, Jimmy,
  Look, Cast, Klimb, Enter, Board, Use, Ztats,
- a utility row: **⌨** (full keyboard), **Esc**, **↵**, **Pass**, **Yes**, **No**.

The game screen sits above the controls (nothing is covered), and there's **sound**. When
you need to type — NPC conversation keywords, quantities, your character's name — tap **⌨**
for a full QWERTY, and **⌨▸CMDS** flips back to the command pad.

## 🤖 Play on Android

There's no custom Android app, but Ultima V runs well on Android through an existing DOSBox
front-end plus **your own** game files. Two good options:

### RetroArch + DOSBox-Pure (free)
1. Install **[RetroArch](https://www.retroarch.com)** (Play Store, F-Droid, or the site).
2. In RetroArch: **Online Updater → Core Downloader → "DOSBox-Pure"**.
3. Put your Ultima V game folder on the phone — the one with **`ULTIMA.EXE`** — or zip that folder.
4. **Load Content** → pick the folder (or the `.zip`) → run it with **DOSBox-Pure**. It
   mounts your files as `C:` and shows a start menu — choose **`ULTIMA.EXE`**.
5. DOSBox-Pure gives you an on-screen keyboard, a gamepad overlay, and mouse emulation.
   Hold the phone in **landscape**.

### Magic DOSBox (paid, ~$4 — best touch controls)
1. Install **Magic DOSBox** from the Play Store.
2. New game → set the **`C:`** mount to your Ultima V folder and the run command to **`ULTIMA.EXE`**.
3. It has fully customizable on-screen controls, so you can build a D-pad + command-button
   layout much like the iOS version here.

> **Bring your own game files** (no game data is distributed here).

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

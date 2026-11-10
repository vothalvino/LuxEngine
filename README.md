<p align="center">
  <img src="src/LuxEngine.App/Assets/LuxEngine.png" width="96" alt="LuxEngine logo" />
</p>

<h1 align="center">LuxEngine</h1>

<p align="center">
  Free, open-source DMX lighting control for Windows.<br/>
  Patch your lights, build looks, save them as presets, combine presets into cues and press GO.
</p>

<p align="center">
  <a href="https://github.com/vothalvino/LuxEngine/releases/latest"><b>⬇ Download LuxEngine for Windows</b></a>
</p>

<p align="center">
  <img src="docs/screenshots/cues.png" width="800" alt="LuxEngine cue list: each cue combines several presets" />
</p>

---

## Features

- **Presets in folders.** The left panel holds your saved looks in folders. Click a preset to see exactly that look, Ctrl+click to add it on top. Presets name themselves ("Par 1–4 · Blue") until you rename them.
- **Control and Cue in one place.** The area under the stage switches between **Control** (dimmer, color, pan/tilt pad, effects, movement, beam channels) and **Cue** (cue list and cue buttons).
- **Cues made of several presets.** A cue can combine, for example, "Blue wash" + "Spots center" + "Slow rainbow", with its own fade in, fade out, delay and auto-follow.
- **Cue buttons.** A grid of buttons for busking: toggle or flash, each with its own presets and fades, on top of the cue list. Buttons that touch form a group where only one is on.
- **Hundreds of fixtures built in.** More than 500 fixtures from the [Open Fixture Library](https://open-fixture-library.org) (Chauvet, ADJ, Eurolite, Stairville, Cameo, Martin, Robe, Showtec, generic pars and more). If your light isn't there, create it in a simple form, or edit a fixture type after adding it.
- **Fixture groups.** Save a selection ("Front", "Movers left") and get it back with one click or your own key.
- **Live stage view.** See every light's color, brightness and strobe. Select by clicking or dragging, and arrange lights to match your rig.
- **Effects.** Rainbow, chase, wave and strobe, with speed, size and spread. They run across the lights in stage order.
- **Movement for moving heads.** Circle, figure 8, sweeps and your own drawn shapes. Give each head an **area** (for example "only on the back wall") and movements never leave it. Invert pan or tilt per light.
- **Keyboard control.** Shift+letter sets a parameter of the selected lights (Shift+D, 50, Enter = dimmer 50%), plus keys for selection, playback and your own keys for presets, groups and cue buttons. Press **F1** for the full list.
- **Tap tempo and fan.** Tap the beat (T) and let effects and movements follow it; fan the dimmer, a rainbow, pan or tilt across the selected lights.
- **Undo and redo.** Ctrl+Z takes back changes to the show: deleted presets, moved lights, edited cues.
- **Outputs.** Art-Net, sACN (E1.31), Enttec DMX USB Pro and Open DMX USB, with as many universes as you need.
- **Phone remote.** GO, blackout, master, tap tempo and your cue buttons on a phone or tablet, in the browser: nothing to install, protected by a PIN.
- **External control.** MIDI controllers (learn mode) and sound-to-light from music playing on the PC or a microphone.
- **Safe for live use.** Blackout, master fader, autosave with crash recovery, and keeps Windows awake during a show.
- **Light and dark theme**, following Windows or your choice.

📖 **[User manual](docs/manual.md)** · 📝 **[What's new](CHANGELOG.md)**

## Screenshots

| Live control, with a fixture group | Cue buttons |
|---|---|
| ![Control tab with four pars selected, dark theme](docs/screenshots/control.png) | ![Cue buttons, two of them on](docs/screenshots/buttons.png) |

| Moving heads | Fixture library |
|---|---|
| ![Position pad of a moving head](docs/screenshots/movement.png) | ![Searching the built-in fixture library](docs/screenshots/fixture-library.png) |

These screenshots are taken automatically by the CI smoke test on Windows, using the bundled demo show (*Help → Open demo show*).

## Install

1. Download `LuxEngine-Setup.exe` from the [latest release](https://github.com/vothalvino/LuxEngine/releases/latest).
2. Run it. No admin rights are needed when you install "for me only".
3. Windows SmartScreen may warn that the app is unrecognized, because the installer isn't code-signed yet. Click **More info → Run anyway**.

Requires Windows 10 (1809) or newer, 64-bit. Windows 11 recommended.

**USB interfaces:** Enttec and compatible USB-DMX interfaces need the [FTDI VCP driver](https://ftdichip.com/drivers/vcp-drivers/). After installing it, the interface appears as a COM port under *Output → DMX outputs*.

## Quick start

1. **Output → DMX outputs**: choose Art-Net, sACN or your USB interface.
2. **Fixtures → Add**: search the library and add your lights. Their DMX addresses are filled in automatically.
3. Select lights, then set dimmer, color and position in the **Control** tab.
4. Click **+ Preset** to save the look into a folder.
5. In the **Cue** tab, add cues and drag presets onto them.
6. Press **GO** (or Space).

The most used shortcuts (press **F1** in the app for all of them):

| Shortcut | Action |
|---|---|
| Space / Shift+Space | GO / Back |
| B | Blackout |
| T | Tap tempo |
| Ctrl+Z / Ctrl+Y | Undo / Redo |
| Shift+D, then a number and Enter | Dimmer of the selected lights (other letters: see F1) |
| Ctrl+A / Ctrl+I / Esc | Select all / invert selection / deselect |
| [ and ] | Previous / next light |
| H | Highlight the selected lights |
| Ctrl+G | Save the selected lights as a group |
| Ctrl+R | Save the current look as a preset |
| F2 / F3 / F4 | Control / Cue list / Cue buttons |
| Ctrl+N / O / S | New / Open / Save |
| F11 | Full screen |

The [user manual](docs/manual.md) explains each part step by step.

## Updates

LuxEngine asks GitHub once at start-up whether a newer version exists and shows a small **Update available** button if so. Nothing about you or your show is sent. Turn it off under *Help → Check for updates at start-up*. To update, install the new version over the old one; shows and settings are kept.

## Building from source

You need Windows 10/11 and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). Visual Studio 2022 with the *Windows application development* workload is the easiest way to work on the UI.

```powershell
git clone https://github.com/vothalvino/LuxEngine.git
cd LuxEngine
dotnet test tests/LuxEngine.Core.Tests
dotnet test tests/LuxEngine.Output.Tests
dotnet run --project src/LuxEngine.App -p:Platform=x64
```

To build the installer, install [Inno Setup 6](https://jrsoftware.org/isinfo.php) and run:

```powershell
dotnet publish src/LuxEngine.App -c Release -r win-x64 -p:Platform=x64 -o publish
iscc installer\LuxEngine.iss
```

GitHub Actions builds, tests and creates the installer for every push. Pushing a tag such as `v1.0.0` publishes a release.

### Project layout

| Folder | What's inside |
|---|---|
| `src/LuxEngine.Core` | Fixtures, patch, presets and folders, cues, effects, the mixing engine and show files. No UI, fully unit-tested. |
| `src/LuxEngine.Output` | DMX output thread plus the Art-Net, sACN, Enttec Pro and Open DMX drivers. |
| `src/LuxEngine.Control` | MIDI input, sound-to-light and the phone remote (a small built-in web server). |
| `src/LuxEngine.App` | The WinUI 3 desktop app. |
| `fixtures/` | Bundled fixture library (JSON). |
| `tools/FixtureImporter` | Script that converts the Open Fixture Library into `fixtures/`. |
| `installer/` | Inno Setup script. |

### Updating the fixture library

```bash
git clone --depth 1 https://github.com/OpenLightingProject/open-fixture-library.git ofl
python3 tools/FixtureImporter/ofl_import.py ofl/fixtures fixtures
```

## Contributing

Bug reports, ideas and pull requests are welcome: [open an issue](https://github.com/vothalvino/LuxEngine/issues/new/choose) or see [CONTRIBUTING.md](CONTRIBUTING.md).

## License

LuxEngine is released under the [MIT License](LICENSE). Fixture data comes from the Open Fixture Library (MIT). See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

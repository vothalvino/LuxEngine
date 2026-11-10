# Changelog

What changed in each version of Lumeshow. Downloads are on the [releases page](https://github.com/vothalvino/Lumeshow/releases).

Lumeshow was called **LuxEngine** up to version 1.8.0.

## 2.0.0

### LuxEngine is now Lumeshow
The software has a new name, because several other programs are already called LuxEngine. It is the same program with the same features; nothing was removed.

- **Updating:** install Lumeshow over LuxEngine. The installer replaces the old program, so you have one app, with one entry in the Start menu. Close LuxEngine first.
- **Your settings** and your own fixture types move to the new folders by themselves the first time Lumeshow starts (`Documents\Lumeshow\Fixtures` for fixtures).
- **Your shows keep working.** New shows are saved as `.lumeshow` files. Existing `.luxshow` files open as before, by double-click too, and keep their name when you save them. Use *Save as* if you want a `.lumeshow` copy.
- Shows you saved yourself stay where you saved them, also inside `Documents\LuxEngine`.
- The project's address is now [github.com/vothalvino/Lumeshow](https://github.com/vothalvino/Lumeshow). Old links are forwarded.

Going back to LuxEngine 1.8 afterwards is possible, but it starts with fresh settings, and `.lumeshow` files have to be renamed to `.luxshow` for it.

## 1.8.0

### New
- **Phone remote.** Run the show from a phone or tablet: GO, Back, Pause, Release, Blackout, the master, tap tempo and your cue buttons (flash buttons work while you hold them). Switch it on under **Output → Phone remote…** and open the address shown there in the phone's browser. Nothing to install on the phone, no internet needed, and a PIN keeps other people on the Wi-Fi out. It is off until you switch it on.
- **To the beat when editing an effect.** The edit dialog of a running or saved effect now also has **To the beat** and the number of beats.

## 1.7.0

### New
- **Tap tempo.** Tap the new **BPM** button at the top (or press **T**) along with the music. Right-click it to type the tempo, or to halve or double it.
- **Effects and movements to the beat.** Tick **To the beat** in the Effects or Movement card and choose how many beats one round takes (¼ to 16). The effect then follows the tapped tempo, also when it is saved in a preset or cue.
- **Fan.** The new Fan card spreads the dimmer, a rainbow of colors, pan or tilt across the selected lights, from the first to the last as they stand on the stage.

## 1.6.0

### New
- **Undo and redo** (Ctrl+Z, Ctrl+Y or Ctrl+Shift+Z, and the new **Edit** menu) for changes to the show: lights, groups, presets, cues, cue buttons, shapes, areas and outputs. Undo never interrupts the output.
- **Fixture groups.** Save the selected lights as a group (Ctrl+G) and select them again with one click or your own key.
- **Update notice.** An *Update available* button appears when a newer version is out. Can be switched off under *Help*.
- **User manual** (`docs/manual.md`), this changelog, and forms for bug reports and ideas.

### Fixed
- *Back* after cues were deleted from the end of the list no longer fails.

## 1.5.1
- *File → Exit* asks about unsaved changes, like the close button.
- No more crashes from files that cannot be written, or from MIDI and audio devices that fail.
- A show that fails to save keeps its old name.
- A MIDI preset button works like clicking the preset.

## 1.5.0
- **Modern Windows look** in light and dark; the accent color follows Windows.
- Control cards wrap instead of scrolling sideways; the Control / Cue area can be resized.
- New dimmer fader, clearer pan/tilt pad with a live dot, color chip, cue list with fade progress and "Next", nicer cue buttons.
- The stage shows a light's strobe.
- Fixed menus that could stop reacting to clicks.

## 1.4.1
- Arrow keys and Enter no longer open a menu after a menu was used.

## 1.4.0
- **Keyboard control:** Shift+letter parameter bar (dimmer, zoom, gobo, position…), selection keys, highlight (H), playback keys, a searchable shortcut list (F1).
- **Own keys** for presets and cue buttons.

## 1.3.1
- Clicking a preset shows only that preset; Ctrl+click adds it on top.
- Cues and toggle buttons fade each other out instead of mixing.

## 1.3.0
- **Movement for moving heads** with a shape library and **areas** that movements never leave.
- **Invert pan / tilt** per light.
- **Automatic preset names.**
- Effects run in stage order. Support for warm/cold white lights.
- Fixed: picking a color no longer turns the dimmer on.

## 1.2.0
- While editing a preset, an *In this preset* card lists every value.
- The color wheel is no longer covered by the quick colors.
- Fixture editing from the *Fixtures* menu.

## 1.1.1
- **Edit fixture types** after adding them, and **edit presets**.
- *+ Preset* is disabled when there is nothing to save.

## 1.1.0
- Fixture editor: what each DMX value range does, and default values.
- **Cue buttons:** a grid of toggle and flash buttons on top of the cue list.

## 1.0.0
- First release: fixture library, presets in folders, cues made of several presets, effects, stage view, Art-Net, sACN and USB DMX output, MIDI and sound-to-light.

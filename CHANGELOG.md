# Changelog

What changed in each version of LuxEngine. Downloads are on the [releases page](https://github.com/vothalvino/LuxEngine/releases).

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

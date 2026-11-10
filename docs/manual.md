# Lumeshow user manual

A short guide to every part of Lumeshow. New here? Start with [the first ten minutes](#the-first-ten-minutes). In the app, **Help → Quick start guide** and **F1** (all shortcuts) are always one click away.

- [The window](#the-window)
- [The first ten minutes](#the-first-ten-minutes)
- [Lights: adding, selecting, groups](#lights-adding-selecting-groups)
- [Control: dimmer, color, position](#control-dimmer-color-position)
- [Effects and strobe](#effects-and-strobe)
- [Fan](#fan)
- [Movement, shapes and areas](#movement-shapes-and-areas)
- [Presets](#presets)
- [Cue list](#cue-list)
- [Cue buttons](#cue-buttons)
- [Keyboard](#keyboard)
- [Undo and redo](#undo-and-redo)
- [Outputs, MIDI and sound](#outputs-midi-and-sound)
- [Phone remote](#phone-remote)
- [Files, autosave and updates](#files-autosave-and-updates)
- [Problems and answers](#problems-and-answers)

## The window

| Where | What |
|---|---|
| Left | **Presets** in folders: your saved looks. |
| Middle, top | The **stage**: every light with its live color and brightness. |
| Middle, bottom | **Control** (set the selected lights) and **Cue** (cue list and cue buttons). Drag the line above it to make it taller or shorter. |
| Right | **Fixtures**: your lights and your groups. |
| Top bar | Running cue, output status, **Master** fader and **Blackout**. |

## The first ten minutes

1. **Output → DMX outputs…**: choose Art-Net, sACN or your USB interface. (No interface yet? Skip this; the stage shows everything.)
2. **Fixtures → Add fixture from library…**: search your light, choose the mode that matches the light's own menu, the number of lights and the first address.
3. Click a light on the stage or in the list. In **Control**, raise the dimmer and pick a color.
4. Click **+ Preset** to save the look.
5. Open the **Cue** tab, add a cue and drag presets onto it.
6. Press **GO** (or Space).

**Help → Open demo show** loads an example with pars, moving heads, presets, cues and buttons to play with.

## Lights: adding, selecting, groups

**Adding.** The library has more than 500 fixtures. If yours is missing, use **Fixtures → Create new fixture type…** and list its channels in order. To correct a type later (channels, what each DMX range does), right-click a light → **Edit fixture type**; all lights of that type follow.

**Addresses.** New lights get free addresses automatically. A red address means two lights overlap: right-click → **Edit name and address…**.

**Selecting.**

- Click a light; Ctrl+click adds; drag a box on the stage.
- **Ctrl+A** all, **Ctrl+Shift+A** all of the same type, **Ctrl+I** invert, **[** and **]** previous/next, **Esc** none.
- **H** highlights the selected lights (full, white, centred) so you can find them in the rig. It is never saved.

**Groups.** Lights you often use together can be one click:

1. Select them.
2. Press **Ctrl+G** (or the group button above the fixture list) and give the group a name.
3. Click the group to select its lights again. Ctrl+click adds them to the selection.

Right-click a group to rename it, replace its lights with the current selection, delete it, or **set a key**: then pressing that key selects the group.

**Arranging the stage.** Drag lights to where they hang. Effects run across the lights in stage order (left to right, then top to bottom), so the stage layout decides which way a chase runs.

**Invert pan / tilt.** If a moving head turns the wrong way (for example it hangs facing the other direction), right-click it → **Invert pan** or **Invert tilt**.

## Control: dimmer, color, position

Everything you change in Control applies to the selected lights and is live at once.

- **Dimmer:** drag the fader, use the mouse wheel, or double-click the number to type a value. **Off** and **Full** are below it.
- **Color:** the wheel, the quick colors, or the sliders. Lights with only warm and cold white get warm/cold sliders instead.
- **Position** (moving heads): drag the dot in the pad. While a movement runs, a faint dot shows where the head really points.
- **Beam** (zoom, focus, gobo, prism…): one slider per channel the light has. Named ranges ("Gobo 3", "Strobe slow → fast") are shown.

Values you set by hand stay until you clear them (**Clear**, or **Ctrl+Backspace** to release everything).

## Effects and strobe

Select lights, then click an effect: **Rainbow**, **Chase**, **Wave** or **Strobe**. Set **speed**, **size** and **spread** (how far the lights are apart in the pattern: 0 = all together). Click the effect again to stop it.

**To the beat.** Tap the **BPM** button at the top of the window along with the music (or press **T**); four taps are enough. The dot on the button blinks on the beat. Right-click the button to type the tempo, or to halve or double it. Then tick **To the beat** in the Effects card and choose how many beats one round of the effect takes: a chase on 4 lights with *4 beats* steps once per beat. The Speed slider is off while the effect follows the beat. The same works for movements (Movement card). The tempo is live: it is not saved in the show, but "to the beat" is saved with the effect in presets and cues.

**Strobe, two ways:**

- The **Strobe effect** flashes any light, also ones without a strobe channel.
- A light's own **strobe / shutter channel** (in the beam card, or **Shift+S**) uses the strobe built into the light. It is usually faster and sharper. The stage shows the flicker.

## Fan

The **Fan** card spreads a value across the selected lights instead of giving them all the same. Select at least two lights, choose what to fan (**dimmer**, **color**, **pan** or **tilt**), and move the **First** and **Last** sliders: the first light gets the first value, the last light the last one, and the lights in between are spread evenly. "First" and "last" follow the stage: left to right, then top to bottom, in whatever order you selected.

- Dimmer from 0% to 100%: a ramp from dark to bright.
- Color: a rainbow across the lights.
- Pan from 25% to 75% on moving heads: the beams open like a fan. **Swap** turns it around.

The result is ordinary values, so you can save it as a preset. Lights without the chosen channel are skipped.

## Movement, shapes and areas

For moving heads, the **Movement** card makes them move by themselves.

1. Select the heads.
2. Choose a shape: circle, figure 8, pan sweep, tilt sweep, or one you drew.
3. Set speed and size (and width/height), **spread** to make the heads follow each other, **reverse** to turn the other way.
4. **Start movement**.

**Your own shapes.** **Fixtures → Movement shapes…**: start from a template, drag the points, choose closed or open and smooth or straight, and try it on the heads live.

**Areas: "only on the wall".** An area is the part of the room a head may reach.

1. **Fixtures → Movement areas…**
2. Choose a head. Aim it at one corner of the area (for example the bottom left of the back wall) and capture it, then the opposite corner. You can also draw the box or type the values.
3. Give the area a name ("Wall"). Copying the box to the other heads gives them a start; adjust each one, because heads in different places need different pan/tilt for the same wall.
4. In the Movement card, pick the area. The movement is now centred in each head's box and never leaves it.

## Presets

A preset is a saved look: values and effects for some lights.

- **Save:** set the look, then **+ Preset** (or **Ctrl+R**). Only the lights you touched are saved.
- **Names:** presets name themselves from what is in them ("Par 1–4 · Blue"). Rename to keep your own name; right-click → **Use automatic name** switches back.
- **Use:** click a preset to see exactly that look. **Ctrl+click** adds it on top of what is live.
- **Edit:** double-click (or right-click → **Edit preset…**). The lights in it get a ring, and the **In this preset** card lists every value. Change things, add or remove lights, then **Save** (Ctrl+Enter) or **Cancel** (Esc).
- **Organize:** drag presets into folders. Right-click for rename, color, **set a key**, delete.

## Cue list

A cue combines one or more presets with timing. The list plays from top to bottom.

- **Add a cue**, then drag presets onto it (or use its edit dialog).
- **Fade in / fade out / delay** per cue. **Auto-follow** starts the next cue by itself.
- **GO** (Space) starts the next cue; **Back** (Shift+Space) goes to the one before; **P** pauses a fade.
- The running cue fills up as it fades, and the next one is marked **Next**.

Brightness mixes "highest wins" between sources; color, position and other values use the latest.

## Cue buttons

In the Cue tab, switch to **Buttons** (F4): a grid of buttons for playing live without a fixed order.

- A button holds one or more presets, with its own fade in and out.
- **Toggle** buttons switch on and off. **Flash** buttons are on only while you hold them.
- **Buttons that touch** (side by side, not diagonal) form a group where only one is on: put your color buttons in a row and they replace each other. Leave a gap to make buttons independent.
- Give a button a **key** and play it from the keyboard.
- Starting a cue from the list fades the toggle buttons out, and switching a toggle button on fades the running cue out, so the two never fight.
- The grid has no edge: drag the background to move, Ctrl+mouse wheel to zoom.

## Keyboard

**F1** (or **?**) shows every shortcut, with search. The idea is like a lighting console: select lights, then **Shift + a letter** for the parameter:

| Keys | Parameter |
|---|---|
| Shift+D | Dimmer |
| Shift+P | Position |
| Shift+Z / F / I / X | Zoom / Focus / Iris / Frost |
| Shift+G / R | Gobo / Gobo rotation |
| Shift+C / M | Color wheel / Prism |
| Shift+S | Strobe |
| Shift+W / A / U / K | White / Amber / UV / Warm–cold |
| Shift+V | Speed of the running effect or movement |

A bar appears: use the arrow keys or mouse wheel, or type a number and press Enter. Esc closes it. These keys only work with lights selected.

**Your own keys.** Presets, groups and cue buttons can each have a key (right-click → set a key). Lumeshow tells you when a key is already taken.

**T** taps the tempo. If you gave T to a preset, group or cue button, your own key wins.

Shortcuts are off while you type in a text box.

## Undo and redo

**Ctrl+Z** takes back the last change to the show, **Ctrl+Y** (or Ctrl+Shift+Z) puts it back. Also under **Edit**.

- Covered: lights (added, removed, moved, addresses), groups, presets and folders, cues, cue buttons, shapes, areas, outputs.
- Not covered: what is live right now (fader moves that are not saved in a preset, which cue is running). Undo never interrupts the output.
- The last 100 steps are kept until you open another show or close Lumeshow.
- While you edit a preset, save or cancel it first.

## Outputs, MIDI and sound

- **Output → DMX outputs…**: add Art-Net, sACN (E1.31), Enttec DMX USB Pro or Open DMX USB, one per universe. USB interfaces need the [FTDI VCP driver](https://ftdichip.com/drivers/vcp-drivers/) and then appear as a COM port.
- **Output → Universe monitor…** shows the DMX values being sent: useful to check whether a problem is in Lumeshow or in the cable.
- **Output → MIDI controllers…**: click **learn**, move a fader or press a pad on your controller, and choose what it does (master, GO, a preset, a cue button…).
- **Sound to light** (a card in Control): switch it to *Listening* and the lights react to the music playing on the PC, or to a microphone / line in. *Strength* sets how much.

## Phone remote

Walk around the room with the show in your hand: a phone or tablet can press GO, Back, Pause and Release, switch Blackout, move the master, tap the tempo and play your cue buttons. Nothing is installed on the phone, and no internet is needed.

1. Connect the phone to the **same network** (Wi-Fi) as the PC.
2. In Lumeshow, open **Output → Phone remote…** and switch it **On**. The first time, Windows may ask whether Lumeshow may be reached from the network: choose **Allow**.
3. The dialog shows an address such as `http://192.168.1.20:8080`. Type it into the phone's browser.
4. Enter the **PIN** from the dialog. The phone remembers it.

Tip: use the browser's **Add to Home screen** to get the remote as an icon.

- **Cue buttons** look and work like in Lumeshow: toggle buttons switch on and off, flash buttons are on while you hold them, and buttons that touch replace each other.
- **Release** asks first, because it fades out the cue list and all cue buttons.
- The **PIN** keeps other people on the same Wi-Fi out. *New PIN* makes another one (phones then ask again); an empty PIN switches the protection off, which is only sensible on a network of your own. After five wrong PINs the remote waits half a minute.
- The remote stays on the next time you start Lumeshow, until you switch it off. It is only reachable inside your network, not from the internet.
- If port 8080 is used by another program, choose another **port**; the address changes with it.

Several phones can use the remote at the same time.

## Files, autosave and updates

- Shows are single `.luxshow` files: copy them to another PC freely.
- Lumeshow keeps a recovery copy of unsaved work and offers it after a crash or power cut.
- Your own fixture types are in `Documents\Lumeshow\Fixtures` (**Fixtures → Open my fixtures folder**) and can be shared.
- **Updates:** at start-up Lumeshow asks GitHub whether a newer version exists and shows **Update available** in the top bar. Nothing about you or your show is sent. Switch it off under **Help → Check for updates at start-up**. Install a new version over the old one; shows and settings are kept.

## Problems and answers

**The stage shows light, but the real lights stay dark.** Check the output status in the top bar and **Output → DMX outputs…**. For Art-Net/sACN the PC must be in the same network as the node, and the universe numbers must match. For USB, install the FTDI driver and pick the right COM port. Check **Blackout** and **Master**.

**A light reacts, but wrongly** (wrong color, moves instead of dims). The light is set to another mode or address than in Lumeshow. Compare the light's own menu with right-click → **Edit name and address…**, and the channel count of the mode.

**The keys do nothing.** Click the stage once: shortcuts are off while a text box has the cursor. Parameter keys need lights selected.

**A moving head goes left when the others go right.** Right-click → **Invert pan** (or tilt).

**The phone cannot open the remote.** Check that the phone is on the same Wi-Fi as the PC (not on mobile data, and not on a "guest" Wi-Fi, which often keeps devices apart). Type the address exactly, starting with `http://`. If Windows asked about network access and you chose Cancel, allow Lumeshow under *Windows Security → Firewall & network protection → Allow an app through firewall*. If the PC has several addresses in the dialog, try each.

**I used LuxEngine. Where are my things?** Lumeshow is the new name of LuxEngine (since version 2.0). Installing it replaces LuxEngine. Settings and your own fixture types are brought over the first time Lumeshow starts; your show files are not moved or changed.

**Windows says the installer is unrecognized.** The installer is not code-signed yet. Click **More info → Run anyway**.

**Something else?** [Open an issue](https://github.com/vothalvino/Lumeshow/issues/new/choose). If Lumeshow reported an error, add the file `%LOCALAPPDATA%\Lumeshow\errors.log`.

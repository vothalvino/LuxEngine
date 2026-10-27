#!/usr/bin/env python3
"""Convert Open Fixture Library (OFL) fixtures into LuxEngine's fixture library format.

Usage:
    git clone --depth 1 https://github.com/OpenLightingProject/open-fixture-library.git ofl
    python3 tools/FixtureImporter/ofl_import.py ofl/fixtures fixtures

Writes one JSON file per manufacturer (an array of fixtures) into the output folder.
Matrix modes and modes that need switching channels LuxEngine cannot represent are skipped.
OFL is MIT licensed: https://github.com/OpenLightingProject/open-fixture-library
"""

import json
import re
import sys
from pathlib import Path

COLOR_TYPES = {
    "Red": "Red", "Green": "Green", "Blue": "Blue", "White": "White", "Amber": "Amber",
    "Lime": "Lime", "Indigo": "Indigo", "UV": "UV", "Cyan": "Cyan", "Magenta": "Magenta",
    "Yellow": "Yellow", "Warm White": "WarmWhite", "Cold White": "ColdWhite",
}

SIMPLE_TYPES = {
    "Intensity": "Intensity",
    "ColorPreset": "ColorWheel",
    "Pan": "Pan", "PanContinuous": "Pan",
    "Tilt": "Tilt", "TiltContinuous": "Tilt",
    "Prism": "Prism", "PrismRotation": "Prism",
    "StrobeSpeed": "Strobe", "StrobeDuration": "Strobe",
    "Focus": "Focus",
    "Zoom": "Zoom", "BeamAngle": "Zoom",
    "Iris": "Iris", "IrisEffect": "Iris",
    "Frost": "Frost", "FrostEffect": "Frost",
    "ColorTemperature": "ColorTemperature",
    "Speed": "Speed", "PanTiltSpeed": "Speed", "EffectSpeed": "Speed", "Time": "Speed", "EffectDuration": "Speed",
    "Effect": "Effect", "EffectParameter": "Effect", "SoundSensitivity": "Effect", "Rotation": "Effect",
    "BladeInsertion": "Effect", "BladeRotation": "Effect", "BladeSystemRotation": "Effect", "BeamPosition": "Effect",
    "Maintenance": "Maintenance",
}


def camel(name):
    """Match .NET JsonNamingPolicy.CamelCase for our enum names."""
    if name.isupper():
        return name.lower()
    return name[0].lower() + name[1:]


def capabilities_of(channel):
    if "capabilities" in channel:
        return channel["capabilities"]
    if "capability" in channel:
        cap = dict(channel["capability"])
        cap.setdefault("dmxRange", [0, 255])
        return [cap]
    return []


def wheel_kind(fixture, wheel_name):
    wheel = fixture.get("wheels", {}).get(wheel_name)
    if not wheel:
        return "Gobo" if "gobo" in wheel_name.lower() else "ColorWheel"
    types = {slot.get("type") for slot in wheel.get("slots", [])}
    if "Color" in types:
        return "ColorWheel"
    if types & {"Gobo", "AnimationGoboStart", "AnimationGoboEnd"}:
        return "Gobo"
    if "Prism" in types:
        return "Prism"
    return "Effect"


def cap_type(fixture, channel_name, cap):
    t = cap.get("type")
    if t == "ColorIntensity":
        return COLOR_TYPES.get(cap.get("color"), "Generic")
    if t in ("WheelSlot", "WheelShake", "WheelRotation", "WheelSlotRotation"):
        wheel = cap.get("wheel") or channel_name
        if isinstance(wheel, list):
            wheel = wheel[0]
        kind = wheel_kind(fixture, wheel)
        if kind == "Gobo" and t in ("WheelRotation", "WheelSlotRotation"):
            return "GoboRotation"
        return kind
    if t == "ShutterStrobe":
        return "Shutter" if cap.get("shutterEffect") in ("Open", "Closed") else "Strobe"
    return SIMPLE_TYPES.get(t, "Generic")


def channel_type(fixture, name, channel):
    caps = capabilities_of(channel)
    if not caps:
        return "Generic"
    types = [cap_type(fixture, name, c) for c in caps]
    # A shutter channel that also strobes is a strobe channel.
    if "Strobe" in types and "Shutter" in types:
        return "Strobe"
    # A gobo wheel that can also scroll is still a gobo selector.
    if "Gobo" in types:
        return "Gobo"
    meaningful = [(c, ty) for c, ty in zip(caps, types) if c.get("type") != "NoFunction"]
    if not meaningful:
        return "Generic"
    # The capability covering most of the DMX range describes the channel best.
    best = max(meaningful, key=lambda ct: ct[0]["dmxRange"][1] - ct[0]["dmxRange"][0])
    return best[1]


def to_dmx(value, default=0):
    if isinstance(value, (int, float)):
        return max(0, min(255, int(value)))
    if isinstance(value, str) and value.endswith("%"):
        try:
            return max(0, min(255, round(float(value[:-1]) * 255 / 100)))
        except ValueError:
            return default
    return default


def default_value(ctype, channel):
    if "defaultValue" in channel:
        return to_dmx(channel["defaultValue"])
    if ctype in ("Pan", "Tilt"):
        return 128
    if ctype in ("Shutter", "Strobe"):
        for cap in capabilities_of(channel):
            if cap.get("type") == "ShutterStrobe" and cap.get("shutterEffect") == "Open":
                return cap["dmxRange"][0]
    return 0


def label(fixture, channel_name, cap):
    if cap.get("comment"):
        return cap["comment"]
    t = cap.get("type", "")
    if t == "WheelSlot":
        wheel_name = cap.get("wheel") or channel_name
        wheel = fixture.get("wheels", {}).get(wheel_name if isinstance(wheel_name, str) else wheel_name[0], {})
        slot = cap.get("slotNumber")
        slots = wheel.get("slots", [])
        if isinstance(slot, (int, float)) and 1 <= slot <= len(slots):
            s = slots[int(slot) - 1]
            return s.get("name") or s.get("type", "Slot")
        return f"Slot {slot}"
    for key in ("effectName", "shutterEffect", "color", "effectPreset"):
        if cap.get(key):
            extra = cap[key]
            break
    else:
        extra = ""
    if cap.get("colors"):
        extra = " ".join(cap["colors"])
    for start, end in (("speedStart", "speedEnd"), ("brightnessStart", "brightnessEnd"), ("angleStart", "angleEnd")):
        if start in cap:
            extra = f"{extra} {cap[start]} → {cap[end]}".strip()
    words = re.sub(r"(?<!^)(?=[A-Z])", " ", t)
    return f"{words} {extra}".strip() if extra and extra != t else words


def convert_fixture(manufacturer_key, manufacturer_name, fixture_key, fixture):
    available = fixture.get("availableChannels", {})
    channels = {}  # name -> channel definition

    def add_channel(name, ctype, source):
        caps = []
        for cap in capabilities_of(source):
            rng = cap.get("dmxRange", [0, 255])
            caps.append({"from": rng[0], "to": rng[1], "label": label(fixture, name, cap)})
        if len(caps) == 1 and caps[0]["from"] == 0 and caps[0]["to"] == 255:
            caps = []
        ch = {"name": name, "type": camel(ctype), "default": default_value(ctype, source)}
        if caps:
            ch["capabilities"] = caps
        channels[name] = ch

    aliases = {}
    for name, ch in available.items():
        ctype = channel_type(fixture, name, ch)
        add_channel(name, ctype, ch)
        for fine in ch.get("fineChannelAliases", []):
            aliases[fine] = {"Pan": "PanFine", "Tilt": "TiltFine"}.get(ctype, "Generic")
        for switch_alias, switched in ch.get("switchChannels", {}).items():
            aliases[switch_alias] = switched  # name of the default switched channel

    modes = []
    for mode in fixture.get("modes", []):
        names = []
        ok = True
        for entry in mode.get("channels", []):
            if entry is None:
                names.append(None)
                continue
            if not isinstance(entry, str):
                ok = False  # matrix insert block
                break
            if entry not in channels:
                target = aliases.get(entry)
                if target is None:
                    ok = False
                    break
                if target in channels:  # switching channel: behaves like its default channel
                    source = available.get(target, {})
                    add_channel(entry, channel_type(fixture, target, source), source)
                else:
                    channels[entry] = {"name": entry, "type": camel(target), "default": 0}
            names.append(entry)
        if ok and names:
            modes.append({"name": mode.get("name", f"{len(names)}-channel"), "channels": names})

    if not modes:
        return None
    used = {n for m in modes for n in m["channels"] if n}
    return {
        "id": f"{manufacturer_key}/{fixture_key}",
        "manufacturer": manufacturer_name,
        "model": fixture.get("name", fixture_key),
        "category": (fixture.get("categories") or ["Other"])[0],
        "channels": [ch for name, ch in channels.items() if name in used],
        "modes": modes,
    }


def main():
    if len(sys.argv) != 3:
        print(__doc__)
        return 1
    source, target = Path(sys.argv[1]), Path(sys.argv[2])
    manufacturers = json.loads((source / "manufacturers.json").read_text(encoding="utf-8"))
    target.mkdir(parents=True, exist_ok=True)
    total = skipped = 0

    for folder in sorted(p for p in source.iterdir() if p.is_dir()):
        name = manufacturers.get(folder.name, {}).get("name", folder.name)
        fixtures = []
        for file in sorted(folder.glob("*.json")):
            data = json.loads(file.read_text(encoding="utf-8"))
            if "redirectTo" in data:
                continue
            converted = convert_fixture(folder.name, name, file.stem, data)
            if converted is None:
                skipped += 1
                continue
            fixtures.append(converted)
        if fixtures:
            out = target / f"{folder.name}.json"
            out.write_text(json.dumps(fixtures, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
            total += len(fixtures)

    print(f"Imported {total} fixtures, skipped {skipped} (matrix-only or unsupported).")
    return 0


if __name__ == "__main__":
    sys.exit(main())

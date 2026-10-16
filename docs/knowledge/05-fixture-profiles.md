# Fixture Profiles & Dynamic Channel Mapping

> Core rule from the spec: **never hardcode "Red = channel 1"**. The selected profile + mode defines everything.

## 1. Concepts

| Term          | Meaning                                                                                    |
|---------------|--------------------------------------------------------------------------------------------|
| Profile       | Definition of a fixture model: manufacturer, model, channels, capabilities, modes          |
| Mode          | A channel layout of the same fixture (e.g., 4ch "RGBW", 8ch "Full"). Channel count per mode |
| Channel       | One DMX slot in the mode: offset, name, **type**, default, ranges                          |
| Capability    | A value range on a channel with a meaning (e.g., 10–255 = strobe slow→fast)                |
| Fixture       | A *patched instance*: profile + mode + universe + start address + name + groups            |
| Verified flag | Whether the channel map was confirmed against the real light / manual                    |

## 2. Channel types (v1)

`Red`, `Green`, `Blue`, `White`, `Amber`, `UV`, `Dimmer` (master), `Strobe`, `ColorMacro`, `Program`, `Speed`,
`Mode`, `Generic` (unknown/custom), and `Fine` (16-bit low byte; `coarseKey` names its partner).
Implemented in `src/DmxPro.Core/Profiles/ChannelType.cs`. Built-in profiles live in `profiles/`.

UI generation rules:
- Show a **color picker** only if the mode has R, G and B (optionally W/A/UV).
- Show a **white slider** only if a White channel exists.
- Show **dimmer** only if a Dimmer channel exists; otherwise "software dimming" scales RGB(W).
- Show **strobe/macro/speed** controls only if declared, using the **capability ranges** as labeled presets.
- Always offer a **raw channel view** (0–255 per channel with names) for diagnostics.
- `Generic` / unverified channels show a ⚠️ "unverified" badge.

## 3. Proposed JSON profile format (`*.dmxprofile.json`)

```json
{
  "schemaVersion": 1,
  "id": "generic-rgbw-par",
  "manufacturer": "Generic",
  "model": "RGBW LED PAR",
  "type": "ColorChanger",
  "verified": false,
  "source": "Manufacturer manual v1.2, page 6",
  "channels": [
    { "key": "dimmer", "name": "Dimmer", "type": "Dimmer", "default": 255 },
    { "key": "red",    "name": "Red",    "type": "Red",   "default": 0 },
    { "key": "green",  "name": "Green",  "type": "Green", "default": 0 },
    { "key": "blue",   "name": "Blue",   "type": "Blue",  "default": 0 },
    { "key": "white",  "name": "White",  "type": "White", "default": 0 },
    { "key": "strobe", "name": "Strobe", "type": "Strobe", "default": 0,
      "capabilities": [
        { "min": 0,  "max": 9,   "label": "No strobe", "safe": true },
        { "min": 10, "max": 255, "label": "Strobe slow → fast" }
      ] },
    { "key": "macro",  "name": "Color macro", "type": "ColorMacro", "default": 0,
      "capabilities": [
        { "min": 0,  "max": 50,  "label": "Manual (RGBW channels active)", "safe": true },
        { "min": 51, "max": 255, "label": "Built-in programs" }
      ] },
    { "key": "speed",  "name": "Speed", "type": "Speed", "default": 0 }
  ],
  "modes": [
    { "name": "4ch RGBW", "channels": ["red", "green", "blue", "white"] },
    { "name": "8ch Full", "channels": ["dimmer", "red", "green", "blue", "white", "strobe", "macro", "speed"] }
  ]
}
```

## 4. Color handling (RGB → RGBW)

The spec says: do **not** assume one conversion is best. Offer per-fixture/project policy:

| Policy                  | White channel behavior                                     |
|-------------------------|------------------------------------------------------------|
| `RgbOnly`               | W = 0; mix all colors with RGB                             |
| `ExtractWhite` (default)| W = min(R,G,B); R,G,B reduced by W → better whites, same hue |
| `ManualWhite`           | User sets W independently                                  |

Pure Red/Green/Blue are unaffected by `ExtractWhite` since min = 0.

## 5. Brightness / dimming pipeline (per fixture, per frame)

```
function values (HTP/LTP merged)
   → color policy (RGB→RGBW)
   → fixture dimmer: hardware Dimmer channel if present, else scale R,G,B,W
   → Grand Master (scales intensity channels only)
   → Blackout (intensity → 0)
   → clamp 0..255 → universe buffer
```

## 6. Imports (candidate features)

- **QLC+ `.qxf`** (XML, Apache-2.0 library) → map `Group`/`Preset` attributes to our channel types.
- **Open Fixture Library** (open-fixture-library.org, MIT) JSON.
- Manual **profile editor** in-app (generic channel patch editor for unknown fixtures, as required by spec §9).

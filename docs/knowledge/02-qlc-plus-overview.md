# QLC+ (Q Light Controller Plus) — How It Works

> QLC+ is the main reference product for DMX Pro. We study its concepts; we do **not** copy its code.
> QLC+ is open source under the **Apache 2.0** license (Qt / C++, Windows/macOS/Linux/Raspberry Pi),
> maintained by Massimo Callegari, forked from Q Light Controller by Heikki Junnila.

## 1. Big picture

```
 Fixture definitions (.qxf)      Workspace / project (.qxw, XML)
          │                                   │
          ▼                                   ▼
   ┌─────────────┐   ┌──────────────┐   ┌─────────────────┐   ┌──────────────────┐
   │  Fixtures   │──▶│  Functions   │──▶│ Virtual Console │──▶│  DMX engine      │
   │ (patch)     │   │ scene/chaser │   │ (live buttons)  │   │ (merge HTP/LTP,  │
   └─────────────┘   │ EFX/matrix…  │   └─────────────────┘   │  grand master)   │
                     └──────────────┘                         └────────┬─────────┘
                                                                       ▼
                                                    Input/Output plugins (USB-DMX, Art-Net, sACN…)
```

## 2. Main sections of the QLC+ (v4) interface

| Section            | What it does                                                                      | DMX Pro equivalent |
|--------------------|-----------------------------------------------------------------------------------|--------------------|
| **Fixtures**       | Add fixtures from the library, choose mode, universe, address, groups             | Tab 1 Fixtures     |
| **Functions**      | Create/edit all functions (scenes, chasers, effects…)                             | Tab 2 Functions + Tab 3 Scene Creator |
| **Shows**          | Timeline (multi-track) to arrange functions & audio over time                     | Music / Show tab (our extension) |
| **Virtual Console**| Build a custom live control surface                                               | Tab 4 Virtual Console |
| **Simple Desk**    | Raw faders per DMX channel, like a basic hardware desk                            | Tab 5 Test & Preview |
| **Inputs/Outputs** | Map each universe to output plugins, input devices (MIDI/OSC), and feedback       | Tab 6 Input/Output |

QLC+ also has **Design mode** vs **Operate mode** — editing is locked during a show (our "operator lock").

## 3. Function types in QLC+

| Function       | Description                                                                                         |
|----------------|-----------------------------------------------------------------------------------------------------|
| **Scene**      | Static set of channel values for selected fixtures, with fade in / fade out                         |
| **Chaser**     | Ordered list of *other functions* (steps). Each step: fade in, hold, fade out, duration             |
| **Sequence**   | A chaser whose steps are scene-like value snapshots bound to one scene's fixtures                   |
| **EFX**        | Movement patterns (circle, eight, line…) for pan/tilt — **out of scope v1** (moving heads)          |
| **Collection** | Runs several functions at once                                                                      |
| **RGB Matrix** | Pixel effects across a grid of RGB fixtures (rainbow, chase, plasma, text, scripted JS patterns)    |
| **Script**     | Small scripted actions (start/stop functions, wait, set channel)                                    |
| **Show**       | Timeline with tracks; items are functions/audio/video placed in time                                |
| **Audio/Video**| Play a media file (can be placed in Shows)                                                          |

Chaser run order: **Loop**, **Single shot**, **Ping-pong**, **Random**. Direction: forward/backward.
Speed modes: *Default* (from function), *Common* (one value for all steps), *Per step*.

## 4. Virtual Console widgets

| Widget              | Purpose                                                                  |
|---------------------|--------------------------------------------------------------------------|
| Button              | Toggle / flash (while held) / stop-all / blackout a function             |
| Slider              | Level (channels), Playback (function intensity), Submaster (scale a frame)|
| Knob                | Same as slider, round                                                     |
| Cue List            | Step through a chaser (Go / Back), crossfade                              |
| Speed Dial          | Tap-tempo / BPM / time setting applied to functions                       |
| XY Pad              | Pan/tilt control (moving heads)                                           |
| Frame / Solo Frame  | Grouping; in a *Solo* frame only one function runs at a time              |
| Audio Triggers      | Spectrum analyzer: frequency bands trigger functions/channels/widgets     |
| Animation (Matrix)  | Live control of an RGB Matrix (colors, pattern)                           |
| Label / Clock       | Info, scheduling                                                          |

Widgets have **pages**, can map to **external input** (MIDI/OSC/keyboard) and send **feedback** to controllers.

## 5. The DMX engine in QLC+

- A **master timer** ticks at a fixed rate (default 50 Hz). On each tick every running function writes its values.
- Values are **merged**: intensity channels **HTP**, others **LTP**.
- **Grand Master** scales intensity channels (or all channels) — modes *Reduce* or *Limit*.
- **Blackout** forces intensity to 0 without stopping functions.
- The resulting universe buffers are handed to output plugins.

## 6. Inputs / Outputs (plugins)

Each universe can have: **1 output**, **1 input**, **1 feedback** line, plus *passthrough*.

Common plugins: DMX USB (Enttec Pro / Open DMX / FTDI devices), **Art-Net**, **E1.31 (sACN)**, OSC, MIDI, HID,
OLA, uDMX, Peperoni, Velleman, **Loopback** (internal routing), GPIO.

## 7. Files

| File      | Format | Content                                                     |
|-----------|--------|-------------------------------------------------------------|
| `.qxf`    | XML    | Fixture definition: manufacturer, model, channels (groups, capabilities with value ranges), modes, physical info |
| `.qxw`    | XML    | Workspace: patched fixtures, groups, functions, VC layout, I/O mapping |
| `.qxi`    | XML    | Input profile (MIDI controller layouts)                      |

The QLC+ fixture library (hundreds of `.qxf` files) is Apache-2.0 — **importing `.qxf`** is a strong candidate
feature for DMX Pro (with attribution).

## 8. What users complain about in QLC+ (our opportunity)

- Dated Qt Widgets UI, many nested dialogs, steep learning curve.
- Scene → Chaser → VC button needs many clicks.
- Music sync is limited (Audio Triggers are reactive only; Show timeline has no auto beat-mapping).
- Profile editor is a separate application.

**DMX Pro goals:** modern Fluent UI, fewer clicks, drag & drop, built-in profile editor, music upload with
automatic beat/energy analysis that generates an editable light show.

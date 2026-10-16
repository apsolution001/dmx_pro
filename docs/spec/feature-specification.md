# RGB DMX Windows Controller — Feature Specification

> Markdown conversion of `RGB_DMX_Windows_Controller_Feature_Specification.pdf` (17 pages).
> This is the **baseline requirement**. Additions agreed with the product owner are in `product-requirements.md`.

**Initial scope:** Windows desktop application • RGB and RGBW DMX fixtures • Dynamic channel profiles • Six primary tabs.

**Purpose:** defines a first version of a Windows DMX lighting application focused on conventional RGB/RGBW fixtures:
pages, controls, data model, safety behavior, and build order. It is a requirements guide, not a claim that hardware
has been tested or that any example channel map matches a particular light.

> ### Core rule
> Do not hardcode "Red = channel 1" or "Strobe = channel 6" for every fixture. Channel assignments vary by fixture
> model and DMX mode. The software must read the selected fixture profile and build the controls from that profile.

---

## 1. Product scope and design principles

- **Platform:** Windows desktop application.
- **Initial fixture types:** conventional RGB and RGBW DMX fixtures; some may have dimmer, strobe, macro, program, or speed channels.
- **Not in initial scope:** moving heads, pan/tilt, gobos, prisms, focus, zoom, fixture-specific beam mechanics.
- **Core workflow:** patch fixtures → configure profiles → create scenes/functions → arrange live controls → test → configure physical DMX output.
- **Extensible design:** add future fixture types by creating profiles and capability handlers rather than rewriting the UI.

**Design principles**
- **Profile-driven:** the fixture profile is the source of truth for channel names, ranges, defaults, capabilities.
- **Separate programming from output:** edit scenes and preview without necessarily sending DMX to hardware.
- **Reusable functions:** scenes and chases can be used in the console without duplicating their data.
- **Clear status:** show whether output is disabled, connected, sending, or in error.
- **Safe defaults:** start with output disabled until the user deliberately enables it.

## 2. Main navigation and recommended workflow

| Order | Tab             | Main purpose                              | Typical actions                                         |
|-------|-----------------|-------------------------------------------|---------------------------------------------------------|
| 01    | Fixtures        | Define the lights and their DMX addresses | Add fixture, choose profile, patch address, create groups |
| 02    | Functions       | Manage reusable lighting behaviors        | Create/edit chases, fades, color cycles, pulses, sequences |
| 03    | Scene Creator   | Design and save static lighting looks     | Select fixtures, set RGBW values, fade times, save scene |
| 04    | Virtual Console | Operate a show live                       | Trigger scenes, start chases, adjust master brightness  |
| 05    | Test & Preview  | Verify configuration and troubleshoot     | Test RGB channels, inspect values, check patching       |
| 06    | Input / Output  | Configure real hardware and protocols     | Select DMX interface, universe, protocol, enable output  |

**Recommended workflow**
1. Create or select a fixture profile that matches the light's manual.
2. Add the physical fixture and assign its universe and start address.
3. Check the channel map and test each relevant channel at low intensity.
4. Create a few static scenes such as Red, Green, Blue, White, and Blackout.
5. Build functions such as a color chase or fade using the saved scenes.
6. Create Virtual Console buttons and sliders to trigger the functions.
7. Configure the output interface and enable DMX output only when ready.
8. Run the test page again and save the project.

## 3. Tab 1 — Fixtures

Purpose: describe every light connected to the show. Fixture records refer to a profile; they must not contain
assumptions about channel meanings that differ from the profile.

**Fixture list**
- List name, manufacturer/model, fixture type, DMX mode, universe, start address, channel count, group, enabled state.
- Search and filter; sort by name, address, type, or group.
- Add, edit, duplicate, rename, delete, and group fixtures.
- Warn about overlapping DMX addresses in the same universe.
- Show the address footprint (8 channels at S occupy S … S+7).

**Add/edit fixture form**

| Field             | Purpose / validation                                       |
|-------------------|------------------------------------------------------------|
| Fixture name      | User-friendly label, e.g. Stage Left PAR                   |
| Profile           | Select an existing profile or create/import a profile      |
| DMX mode          | A mode supported by the fixture; modes can have different channel maps |
| Universe          | Output universe the fixture belongs to                     |
| Start address     | 1–512 for a conventional DMX512 universe                   |
| Channel footprint | Derived from the selected profile and mode; do not guess   |
| Group             | Optional group such as Front Wash, Stage Left, All PARs    |
| Notes             | Optional user notes and physical location                  |

**RGB/RGBW capabilities**
- RGB channels: red, green, blue intensity 0–255.
- White channel: only if the profile declares one.
- Master dimmer: optional.
- Strobe: optional; range and behavior from the fixture's DMX chart.
- Color macro/program/speed: optional; ranges may select preset modes rather than direct RGB intensity.
- Channel order: allow RGB, RBG, GRB, or other profile-defined orders.

**Fixture groups:** organizational/operational selections ("All RGBW", "Dance Floor", "Stage Left"). A group references
fixture IDs, not copies. Scenes can target fixtures, groups, or all compatible fixtures.

## 4. Tab 2 — Functions

Purpose: reusable behaviors played from the Virtual Console. A function references scenes or fixture/channel targets.

| Function type          | Description                                     | Initial-scope notes                                   |
|------------------------|-------------------------------------------------|-------------------------------------------------------|
| Scene                  | Saved static look                               | Core; editable from Scene Creator                     |
| Chaser                 | Plays scenes/steps in order                     | Step duration, fade, direction, loop settings         |
| Color cycle            | Transitions through a set of colors             | Only on fixtures with compatible color capabilities   |
| Fade / transition      | Moves from one set of values to another         | Profile-aware channels, configurable duration         |
| Pulse                  | Repeating brightness rises and falls            | Needs a dimmer or safe RGB scaling                    |
| Flash / temporary look | Activates a scene while held/triggered          | Define release behavior clearly                       |
| Strobe effect          | Hardware strobe channel or software timing      | Hardware strobe only if profile supports it           |
| Sequence               | Ordered list of steps with timing and values    | May be the generalized engine used by chasers         |

**Library controls:** create, edit, rename, duplicate, delete, search, organize; show type, duration, loop state,
targets, playback status; preview before assigning to the console; define ownership/conflict behavior when two
functions control the same channels; stop, release, pause, resume, restart.

**Timing:** a step has hold time and fade time. UI distinguishes ms/s from BPM. v1 time-based; beat sync later.

> **Important:** a color cycle must not assume a color-wheel/macro channel. For RGB/RGBW it interpolates R, G, B and
> optional W from the profile. Macro-only color modes need a separate profile-defined implementation.

## 5. Tab 3 — Scene Creator

Purpose: design a static look and save it. A scene is a stored state, not a light type.

- **Name and notes** ("Warm White", "Blue Wash", "Blackout").
- **Target selection:** fixtures, groups, or all compatible fixtures.
- **Color picker:** RGB; for RGBW optionally use white per a defined conversion policy.
- **Channel values:** precise 0–255 controls for profile-defined channels.
- **Brightness:** overall intensity multiplier, respecting whether a fixture has a master dimmer.
- **Fade in / fade out.**
- **Preview:** on-screen look without implying real hardware receives DMX.
- **Save actions:** Save, Save As, Duplicate, Revert, Delete (confirm destructive actions).

**Starter scenes:** Blackout (all 0 where safe), Red, Green, Blue, White (RGB + optional W), Cyan/Magenta/Yellow.

**RGBW:** white by mixing RGB, dedicated W, or a configurable combination; provide a setting such as
"Use dedicated white channel for white colors" and document it.

## 6. Tab 4 — Virtual Console

Purpose: operator-friendly live dashboard triggering saved functions (not a second scene database).

| Widget                | Purpose                         | Essential behavior                                         |
|-----------------------|---------------------------------|------------------------------------------------------------|
| Scene button          | Trigger a saved scene           | Selected/active state; toggle, latch, or release defined   |
| Color buttons         | Quick RGB/RGBW presets          | Target fixture/group, respect profile                      |
| Chaser control        | Start/stop/pause/restart chase  | Running status and speed                                   |
| Master brightness     | Reduce overall intensity        | Consistent scaling; documented interaction with dimmers    |
| Fixture/group slider  | Control a light or group        | Dimmer slider only if supported or software scaling set    |
| Flash button          | Look while held                 | Release reliably on button up or input disconnect          |
| Blackout / release all| Stop or suppress output         | Distinguish blackout vs disconnect vs stopping functions   |
| Speed control         | Change chase/effect speed       | Only for functions that support speed                      |

**Editing:** multiple named pages; add/move/resize/duplicate/rename/delete widgets; labels, colors, grid snapping;
locked operator mode; layout saved in project.

**Suggested first page:** top row Blackout, Release All, Master; middle Red, Green, Blue, White, Cyan, Magenta, Yellow;
bottom start/stop for saved chasers; group selectors once groups exist.

## 7. Tab 5 — Test & Preview

**Tools:** fixture/group selector; red/green/blue-only tests; white test (RGBW); raw 0–255 per profile channel; live DMX
monitor (universe, address, channel name, value); address-footprint view; safe-intensity color test sequence;
connection status, last error, diagnostics; warnings for unverified profile, output disabled, overlapping address.

**Workflow:** select fixture & confirm profile/mode → confirm universe/address match physical settings → enable output at
low intensity → test R, G, B, W separately → fix profile if mapping differs → test dimmer/strobe/macro/speed only when
confirmed → disable output or restore a safe scene.

> Preview is a software representation; it cannot guarantee physical color/brightness. Real output needs a supported
> interface/protocol and a correct DMX path. Output state must be visible.

## 8. Tab 6 — Input / Output

**Output:** enable/disable toggle with clear indicator; choose USB-DMX interface or network protocol; map project
universe → port/protocol universe; device status, errors, activity; validate addresses fit and don't overlap; test-output
and disconnect actions; hide/disable unsupported interfaces.

**Input (optional v1):** external devices only when implemented; map to scene trigger, chase start/stop, master; show
device/mapping status; inputs must not silently override critical output state.

**Protocol scope:** start with one output path tested end-to-end (e.g., USB-DMX). Add Art-Net/sACN only when implemented
and tested — a menu entry is not support.

## 9. Fixture profiles and dynamic channel mapping

| Profile field            | Example / meaning                                                         |
|--------------------------|---------------------------------------------------------------------------|
| Profile identity         | Manufacturer, model, profile name, version, DMX mode                      |
| Channel count            | DMX slots used in this mode                                               |
| Channel index            | Position in footprint, 1…N                                                |
| Channel name             | Red, Green, Blue, White, Dimmer, Strobe, Color Macro, Speed…              |
| Capability/type          | RGB intensity, dimmer, strobe, macro, program, speed, unknown/custom      |
| Value ranges             | 0–255 or documented subranges                                             |
| Default/safe value       | Initial state and release behavior                                        |
| Capability flags         | Supports RGB, white, dimmer, strobe, macro, speed…                        |
| Documentation            | Source/manual reference, verified vs assumed                              |

Illustrative 8-ch RGBW (**NOT universal**): CH1 Red, CH2 Green, CH3 Blue, CH4 White, CH5 Master dimmer, CH6 Strobe,
CH7 Color macro, CH8 Speed.

**Dynamic UI:** generate controls from the profile; color picker only with color capability; white only if declared;
strobe/macro/speed only if declared; never show pan/tilt/gobo/prism/focus in v1; advanced raw view keeps profile names;
unverified definitions marked and must be confirmed.

> **Unknown profiles:** offer a generic channel patch editor (labels and ranges). Never silently guess dimmer/strobe/color.
> Clearly state the mapping is user-defined and needs testing.

## 10. DMX basics

Universe = 512 slots, 8-bit values 0–255; meaning comes from the DMX chart and mode. A fixture with N channels at S
occupies S … S+N−1 (8-ch at 1 → 1–8; at 101 → 101–108). No overlaps unless intentional.

| Color intent    | R   | G   | B   | W (if used) |
|-----------------|-----|-----|-----|-------------|
| Black/off       | 0   | 0   | 0   | 0           |
| Red             | 255 | 0   | 0   | 0           |
| Green           | 0   | 255 | 0   | 0           |
| Blue            | 0   | 0   | 255 | 0           |
| Cyan            | 0   | 255 | 255 | 0           |
| Magenta         | 255 | 0   | 255 | 0           |
| Yellow          | 255 | 255 | 0   | 0           |
| White via RGB   | 255 | 255 | 255 | 0           |
| Dedicated white | 0 or profile-defined ×3 | | | 255   |

Not every channel is linear intensity — represent documented ranges.

## 11. Suggested data model

| Entity            | Suggested fields                                                                   |
|-------------------|------------------------------------------------------------------------------------|
| FixtureProfile    | id, manufacturer, model, mode, channelCount, channels[], capabilities, sourceNotes, verifiedStatus |
| ChannelDefinition | offset, name, type, defaultValue, ranges[], unit, description                      |
| FixtureInstance   | id, name, profileId, universeId, startAddress, groupIds[], notes, enabled          |
| FixtureGroup      | id, name, fixtureIds[]                                                             |
| Scene             | id, name, targetFixtureIds/groupIds, channelValuesByFixture, fadeInMs, fadeOutMs, notes |
| Function          | id, name, type, targetIds, steps[], timing, loopMode, playbackSettings             |
| ConsolePage       | id, name, widgetIds[], locked                                                      |
| ConsoleWidget     | id, pageId, type, x, y, width, height, targetFunctionId/fixtureIds, settings       |
| OutputConfig      | id, protocol, interfaceId, universeMappings[], enabled, settings                   |
| Project           | id, name, version, profiles/refs, fixtures, scenes, functions, console, output     |

**Persistence:** versioned project format; validate references on open (report missing profiles/functions); version
profiles; backups/autosave; never save output-enabled as if hardware is active (runtime state only).

## 12. Validation, safety, error handling

- Address in range and footprint fits the universe.
- Overlaps detected and explained.
- Profile channel count agrees with mode.
- Scene values clamped/rejected outside range.
- Functions targeting deleted fixtures show a visible warning.
- Output cannot be enabled without valid configuration.
- Disconnected/failed output is visible.
- Unknown channel meanings never presented as confirmed.
- Stop/release deterministic when a console button is released or the app closes.

> **Blackout ≠ output disabled.** Blackout sends a dark state. Disabling output stops transmitting; fixtures may hold
> their last state. Describe what the selected interface and fixtures do.

## 13. Implementation roadmap

| Phase              | Build                                                    | Done when                                                  |
|--------------------|----------------------------------------------------------|------------------------------------------------------------|
| 1. Foundation      | Project model, navigation, save/open, fixture profiles   | Create a project and RGB fixture without hardcoded channels|
| 2. Fixture patching| List, address calculator, overlap validation, groups     | Instances patched correctly, conflicts visible             |
| 3. Scene Creator   | RGB/RGBW editor, values, fades, save/duplicate           | Scenes persist and reload                                  |
| 4. Functions       | Chases, color cycle, fade, pulse, timing, playback state | Functions reuse scenes/profile capabilities                |
| 5. Virtual Console | Buttons, sliders, pages, layout save/load                | Operators trigger without editing                          |
| 6. Test & Preview  | Raw test, RGB tests, address monitor, simulated preview  | Verify mapping before live output                          |
| 7. Hardware output | One interface end-to-end                                 | Real DMX validated with a known fixture                    |
| 8. Reliability     | Error handling, autosave, validation, operator lock      | Common failures visible and recoverable                    |

**Acceptance checklist**
- [ ] Add an RGB or RGBW fixture using a profile.
- [ ] Define an arbitrary channel map without changing source code.
- [ ] UI only shows capabilities declared by the profile.
- [ ] Save a scene and use it in a function and a console widget.
- [ ] Test raw DMX values and see correct channel labels.
- [ ] Address conflicts and missing profiles are reported.
- [ ] Preview without sending real output.
- [ ] Output state is visible and intentionally enabled/disabled.
- [ ] Project can be saved, closed, reopened without losing configuration.

## 14. Glossary

| Term              | Explanation                                                          |
|-------------------|----------------------------------------------------------------------|
| DMX               | Control protocol sending lighting values to fixtures                 |
| Universe          | Up to 512 DMX address slots                                          |
| Address           | Slot where a fixture's footprint begins                              |
| Channel           | One value slot; meaning defined by the fixture profile               |
| RGB / RGBW        | Red, Green, Blue (+ White) color channels                            |
| Fixture           | Physical light controlled by DMX                                     |
| Fixture profile   | Definition of a fixture's mode, channels, ranges, capabilities       |
| Scene             | Saved static set of channel values                                   |
| Chaser            | Sequence playing steps/scenes in order                               |
| Virtual Console   | Customizable live dashboard                                          |
| Master brightness | Scales overall output intensity                                      |
| DMX chart         | Manufacturer table of channels and value ranges                      |
| Art-Net / sACN    | Network protocols carrying DMX                                       |
| Patch             | Assigning a profile and address to a fixture                         |

> **Final recommendation:** build around fixture profiles first. Profile-driven mapping is the foundation; treat the
> 8-channel RGBW layout as an example only.

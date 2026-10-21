# DMX Pro — UI Plan (top menu + every screen)

> The blueprint for the whole app UI. Build screens to match this; update this file when a screen changes.
> **Look:** native Windows desktop app laid out like QLC+ (see `docs/standards/ui-ux-guidelines.md`).
> **Behavior reference:** `docs/prototype/index.html` shows what every screen does (its web-style visuals are superseded).
> Phase numbers refer to `docs/roadmap.md`. Items not built yet are shown **disabled** in the app with "Coming in phase N".

## 1. Main window layout

Implemented (QLC+ structure):

```
┌ Title bar ─────────────────────────────────────────────────────────────────────── ─ ☐ ✕ ┐
│ File  Edit  View  Fixtures  Functions  Show  Output  Tools  Help                         │
│ [New][Open][Save][Save as] | [Undo][Redo] | [Monitor][Stop all][Blackout] | [Full screen] │ main toolbar
├──────┬───────────────────────────────────────────────────────────────────────────────────┤
│  💡  │ [+ Add fixture] [🗑][⧉][✨] | [Group]                          [search…]             │ page toolbar
│Fixtur│ ┌ fixture table ───────────────────────┐║┌──────────────────────────────┐         │
│  📚  │ │ ✓ Name  Profile  Mode  U  Address  Ch│║│ Name / Profile / Mode /       │         │
│Librar│ │ ...                                  │║│ Universe / Address / Notes    │         │
│  ⚡  │ └──────────────────────────────────────┘║│ Channel map                   │         │
│Funct.│ ═════════════════ (splitter) ═══════════║└──────────────────────────────┘         │
│  …   │ ┌ Address map  [Universe 1 ▾] ─────────┐║┌ Groups [+][→][🗑] ───────────┐         │
│      │ │ ■■■■■■■■□□□□ … 32 × 16               │║│ Front: Stage L, Stage R …     │         │
├──────┴───────────────────────────────────────────────────────────────────────────────────┤
│ ● DMX output disabled │ 6 fixtures · 2 universe(s) · 2 groups        C:\…\show.dmxpro    │ status bar
└───────────────────────────────────────────────────────────────────────────────────────────┘
```

Earlier web-style sketch (superseded look, same content):

```
┌──────────────────────────────────────────────────────────────────────────────────────────────┐
│ ◆ DMX Pro — Wedding show •                                                     ─  ☐  ✕      │ Title bar (project name, • = unsaved)
├──────────────────────────────────────────────────────────────────────────────────────────────┤
│ File  Edit  View  Fixtures  Functions  Show  Output  Tools  Help                              │ Top menu bar
├───────────────┬──────────────────────────────────────────────────────────────────────────────┤
│ [+][📂][💾]   │  Page title                                          [page actions →]        │ Page header
│               │  One-line explanation of the page                                            │
│ 💡 Fixtures   │ ┌──────────────────────────────────────────────────────────────────────────┐ │
│ 📚 Library    │ │ ⓘ Info bar (saved / warnings / errors)                              ✕   │ │
│ ⚡ Functions  │ └──────────────────────────────────────────────────────────────────────────┘ │
│ 🎨 Scenes     │                                                                              │
│ 🎛 Console    │                         PAGE CONTENT                                         │
│ 🎵 Show       │                                                                              │
│ 🧪 Test       │                                                                              │
│ 🔌 Output     │                                                                              │
│ 🎭 Stage      │                                                                              │
│               │                                                                              │
│ ⚙ Settings    │                                                                              │
├───────────────┴──────────────────────────────────────────────────────────────────────────────┤
│ ● Output disabled │ Universe 1 · 0 fps │ Master ▮▮▮▮▮▮▯ 85% │ [BLACKOUT] [RELEASE ALL] │ 120 BPM │ Status bar
└──────────────────────────────────────────────────────────────────────────────────────────────┘
```

- **Left navigation rail** (Ctrl+1 … Ctrl+8): the main workspaces in workflow order.
- **Status bar** is always visible, on every page: output state, Grand Master, Blackout, Release All, BPM.
- **Full screen** (F11) hides the title bar for shows; **Operator mode** (F12) locks editing.
- **Main toolbar, right side:** **Stage window** (Ctrl+Shift+8) opens the live stage window (section 3.10). The future
  **Go Live** button goes in the same right-hand panel, left of it.
- **Info bar** (above the page): success messages ("Saved show.dmxpro.", "Opened …") hide themselves after about 3 s;
  warnings and errors stay until closed (safety rule 10). Closing the bar clears the message, so the same message (a
  second save) shows again; a newer message replaces the old one and restarts the timer. Import results stay until
  closed (they carry the "unverified profile" caution).

## 2. Top menu bar

| Menu | Item | Shortcut | What it does | Phase |
|------|------|----------|--------------|-------|
| **File** | New project | Ctrl+N | Start an empty project (asks to save changes) | ✅ 1 |
| | Open project… | Ctrl+O | Open a `.dmxpro` file | ✅ 1 |
| | Open recent ▸ | | Last 10 projects | 9 |
| | Save | Ctrl+S | Save to the current file | ✅ 1 |
| | Save as… | Ctrl+Shift+S | Save to a new file | ✅ 1 |
| | Import QLC+ workspace (.qxw)… | | Bring fixtures/scenes from QLC+ | later |
| | Export show report (PDF)… | | Patch sheet: fixtures, addresses, groups | later |
| | Exit | Alt+F4 | Close (stops functions, safe frame, stops output) | ✅ 1 |
| **Edit** | Undo / Redo | Ctrl+Z / Ctrl+Y | All edits | 9 |
| | Cut / Copy / Paste | Ctrl+X / C / V | Fixtures, functions, scenes, widgets, timeline items | 4 |
| | Duplicate | Ctrl+D | Copy with new name | 2 |
| | Delete | Del | With undo | 2 |
| | Rename | F2 | | 2 |
| | Select all | Ctrl+A | | 2 |
| | Search everything… | Ctrl+K | Command palette: pages, fixtures, functions, actions | 9 |
| **View** | Fixtures … Settings (Settings = Ctrl+7) | Ctrl+1 … Ctrl+7 | Go to a page | ✅ 1 |
| | Stage | Ctrl+8 | Virtual Stage page (section 3.10) | ✅ |
| | DMX monitor | Ctrl+M | Live values of every channel | 6 |
| | Dark / Light theme | | Switch theme | ✅ 1 |
| | Full screen | F11 | | ✅ 1 |
| **Fixtures** | Add fixture… | Ctrl+Shift+A | Profile → mode → universe → address (auto free address) | 2 |
| | Add group… | | New group from selection | 2 |
| | Auto-patch selected | | Re-address selected fixtures one after another | 2 |
| | Import fixture profile (.qxf / .json)… | | Into the fixture library | ✅ 1 |
| | New fixture profile… | | Profile editor for any light | 2 |
| | Fixture library | | Go to library | ✅ 1 |
| **Functions** | New scene | | Opens Scene Creator | 3 |
| | New chaser / sequence / color cycle / fade / pulse / flash / strobe | | Opens the function editor | 4 |
| | Create starter scenes | | Blackout, Red, Green, Blue, White, Cyan, Magenta, Yellow | 3 |
| | Stop all functions | Shift+Esc | | 4 |
| **Show** | New show (timeline)… | | | 8 |
| | Add audio track… | | MP3 / WAV | 8 |
| | Add function track | | | 8 |
| | Play / Pause | Space | | 8 |
| | Stop & return to start | Home | | 8 |
| | Analyze beats | | Beat/bar markers for snapping | 8 |
| **Output** | Enable DMX output | Ctrl+E | Only with a valid configuration; starts disabled every launch | ✅ 7 |
| | Disable DMX output | Ctrl+Shift+E | Stops sending (lights may hold last state) | ✅ 7 |
| | Blackout | B | Send dark state (≠ disable) | 4 |
| | Release all | Esc | Stop all functions, return to defaults | 4 |
| | Configure outputs… | | Go to Input / Output | ✅ 7 |
| **Tools** | Test fixture… | | Go to Test & Preview | ✅ 1 |
| | Profile editor | | | 2 |
| | Operator mode (lock editing) | F12 | | 5 |
| | Settings | Ctrl+, | Theme, safe frame on exit, strobe safety, folders (§3.9) | ✅ 7 |
| **Help** | Getting started | F1 | First-run guide | 9 |
| | Keyboard shortcuts | | List of all shortcuts | ✅ 1 |
| | DMX basics | | Built-in explanation of DMX, addresses, universes | later |
| | About DMX Pro | | Version, licences (QLC+ fixture data Apache-2.0) | ✅ 1 |

## 3. Screens

### 3.1 Fixtures (phase 2)

```
[+ Add fixture] [+ Group] [Duplicate] [Delete]                              🔍 search   Universe ▾
┌ Fixture list ─────────────────────────────────────────┐ ┌──────────────────────────────────────┐
│ ● Name          Profile        Mode     U  Address    │ │ Name      [Stage Left PAR        ]  │
│ ● Stage L PAR   Generic RGBW   8ch Full 1  1–8        │ │ Profile   [Generic RGBW ▾] ⚠ unverif.│
│ ● Stage R PAR   Generic RGBW   8ch Full 1  9–16       │ │ Mode      [8ch Full ▾]  8 channels   │
│ ⚠ Back bar      LED Bar 12     12ch     1  14–25  ⚠   │ │ Universe  [1 ▾]  Address [ 1 ] [Auto]│
│                                                       │ │ Groups    [Front ✕] [+]              │
│                                                       │ │ Notes     [...]                       │
└───────────────────────────────────────────────────────┘ │ Channel map: 1 Red · 2 Green · …     │
┌ Address map — Universe 1 (512 cells, colored by fixture, red = overlap) ───────────────────────┐
│ ■■■■■■■■□□□□■■■■■■■■■■■■□□□□□□□□ …                                                             │
└───────────────────────────────────────────────────────────────────────────────────────────────┘
Groups panel: list of groups with member chips; drag fixtures onto a group.
```

**Add Fixture dialog** (built): profile and mode pickers, count, universe, address, overlap check. Built-in generic
profiles are templates: choosing one shows "Check the channel order matches your light". Buttons: **New layout…**,
**Edit layout…** (custom profiles only), **Edit as new layout…** (copy of a built-in/imported profile).
**Fixtures page**: the same actions are in the toolbar, the context menu, the Properties panel button and **Ctrl+L**.

#### Channel layout editor (built 2026-10-19, `Views/ChannelLayoutWindow.xaml`)

Modal dialog: Name, Manufacturer, **Start from template** picker, channel count, and a row table
(# / Function / Name / Default 0–255) with insert, delete, move up/down and duplicate; "Order" chips show the resulting
channel order. Live validation lists the `LAYOUT_*` codes (errors block Save, warnings do not). Footer: "unverified".
When opened from a built-in profile with lights using it: **Use it for** selected lights / all lights of that profile / no lights.

### 3.2 Fixture Library (built in phase 1; page removed from navigation 2026-10-17)

Left: import button, search, profile list with ✓ verified / ⚠ unverified. Right: name, type, source, mode picker, channel
table (Ch, Name, Type, Default, Ranges). The profile editor was replaced by the channel layout editor (§3.1); ranges and **Mark as verified** are not built.

### 3.3 Functions (phase 4)

```
[+ New ▾] [Duplicate] [Delete] [▶ Preview]                                   🔍 search  Type ▾
┌───────────────────────────────┐ ┌ Editor (changes with type) ────────────────────────────────────┐
│ 🎨 Red          Scene    0.0s │ │ Chaser "Party colors"   Run: Loop ▾  Direction: Forward ▾       │
│ 🎨 Blue wash    Scene    1.0s │ │ ┌ Steps ──────────────────────────────────────────────────────┐ │
│ 🔁 Party colors Chaser   8.0s │ │ │ # Function     Fade in  Hold   Fade out                     │ │
│ 🌈 Rainbow      Color cyc 6s  │ │ │ 1 Red          0.5s     1.0s   0.5s                         │ │
│ 💓 Pulse front  Pulse    2.0s │ │ │ 2 Blue wash    0.5s     1.0s   0.5s                         │ │
│                               │ │ └ drag scenes here from the list ─────────────────────────────┘ │
│                               │ │ Speed [1.0×]   Targets: Front, Stage L PAR                        │
└───────────────────────────────┘ └──────────────────────────────────────────────────────────────────┘
```
Types: Scene, Chaser, Sequence, Color cycle, Fade, Pulse, Flash, Strobe. Copy/paste and duplicate everywhere.

> **Status 2026-10-18:** the engine and the Core model support all eight types; the Functions page has the scene editor
> (§3.4). Every other type has its own editor in the same panel (`Views/Editors/`, `ViewModels/Editors/`): chaser and sequence step grids, colour cycle, fade, pulse, flash and strobe forms; Preview stays disabled for now.

**Select ≠ edit ≠ preview ≠ play** (decided 2026-10-17 — unlike QLC+, selecting or editing never changes the lights):

| User action | Result | Lights change? |
|---|---|---|
| Single click | Select; right panel shows summary + on-screen stage preview | No |
| Double-click / Enter / **Edit** button | Open the editor in the right panel | No |
| **▶ Preview** button / Space | Run the selected function on the lights for checking; button becomes **■ Stop preview** | Yes, until stopped |
| Esc / **■ Stop preview** | Stop the preview; lights return to their previous state | Back to previous |
| Right-click | Edit · Preview · Rename (F2) · Duplicate (Ctrl+D) · Copy / Paste · Add to Virtual Console · Delete | — |

- **Editor:** edits are on-screen only by default. A **"Live on lights"** switch streams edits to the real fixtures; it turns
  off automatically when the editor closes, another function is selected, or the page changes. Step-based editors
  (chaser, sequence) have **"Preview from this step"**. Closing with unsaved changes asks Save / Don't save / Cancel.
- **Playing for the show** happens only from the Virtual Console or the Show Timeline, never by clicking in this page.
- **Preview rules:** one preview at a time (a new one replaces it); preview is layered **on top** of running functions
  (it wins on the channels it touches) and never stops them; it never enables output — with output disabled it is
  on-screen only and says "Output is off — showing on screen only"; Grand Master, Blackout and strobe safety apply;
  Release All also stops the preview. The list marks **● Preview** vs **▶ Live** (playing from console/timeline).
- **Engine (phase 4):** a dedicated preview layer after running functions and before manual overrides
  (`docs/architecture.md` §4). Real-light preview works once outputs exist (phase 7); until then on-screen only.
- The Scene Creator (§3.4) follows the same rules: editing is on-screen only unless "Live on lights" is on.
- **Function-type assumptions** (decided 2026-10-18, see decisions log): Strobe defaults to Software mode; Fade references
  scenes; Flash duration 0 = while held; Pulse/Strobe/Color cycle drive the Dimmer channel when a fixture has one.

### 3.4 Scenes — Scene Creator (phase 3)

```
[General] [Channels]            Fade in [0.5s]  Fade out [0.5s]      (editor toolbar, scenes only)
┌ General: lights in the scene ──────────────────────────────────────────────────────────────────┐
│ [Add ▾ (flyout + search)] [Remove]                                  Find light [__________]   │
│ Name          Profile        Mode     Universe·Address                                        │
│ Stage L PAR   Generic RGBW   4-ch     1·1                                                     │
│ Back bar  ⚠   (unverified profile: caution)                                                   │
└────────────────────────────────────────────────────────────────────────────────────────────────┘
Channels tab: searchable multi-select light list + fader banks (all channel levels are set here).
```

**Copy / paste (2026-10-21, scene and sequence editors):** the Channels tab toolbar, at the left of the fader strip, has
**Copy** (the first selected light) and **Paste to selected** (every selected light); there are no per-light buttons. Copy
takes the included channels (all when none is included), paste includes them. One in-memory clipboard shared with the Test
page; matching rules (same profile by channel key, otherwise by channel type and occurrence) are in
`src/DmxPro.Core/Profiles/CopiedLightValues.cs`. Faders are compact (36 px columns, 128 px travel).

Implemented as `SceneEditorPanel` (shared by the Functions page and the Shows page), with two tabs:

- **General** — lights only: **Add ▾** flyout with search, **Remove**, **Find light** search, and a list (name, profile,
  mode, universe·address, caution for unverified profiles). Colour presets and quick colour/dimmer sliders were removed
  (owner decision 2026-10-18); **all channel levels are set on the Channels tab**. Scene **fade in / fade out** are in
  the editor toolbar (scenes only). New lights copy Red/Green/Blue/White/Amber/UV/Dimmer from the first light in the
  scene that has each channel type. **Channels** — searchable multi-select light list plus fader banks of the selected
  lights. The editor **opens on Channels** when the scene already has lights, else on General.
- **All channels 0–255 for all lights** (owner request 2026-10-18): every channel of a light can be set, including
  strobe/macro/speed channels.
- **Set on selected lights:** pick a channel type (only types the selected lights have) and a value; it is written to
  that channel on every selected light, resolved per profile/mode (never a fixed offset).
- **DmxValueBox** (number box next to each slider): whole numbers 0–255; Enter or focus loss commits (rounded, clamped,
  300 → 255); non-numbers revert; Esc reverts; Up/Down ±1, PageUp/PageDown ±10.
- **From the Shows page:** selecting a scene item and choosing **Edit** opens the same editor on the **show's own copy**;
  the library original is never touched.

### 3.5 Virtual Console (phase 5)

```
Pages: [Basic colors] [Chases] [+]                         [✎ Edit layout]  [🔒 Operator mode]
┌──────────┐┌──────────┐┌──────────┐   ┌─┐ Master   ┌─┐ Front
│ BLACKOUT ││ RELEASE  ││  FLASH   │   │▮│          │▮│
└──────────┘└──────────┘└──────────┘   │▮│          │▮│
┌────┐┌────┐┌────┐┌────┐┌────┐┌────┐   └─┘          └─┘
│Red ││Grn ││Blue││Wht ││Cyan││Mag │   Speed ◐  Tap [♪]
└────┘└────┘└────┘└────┘└────┘└────┘
[▶ Party colors] [▶ Rainbow]
```
Edit mode: widget palette on the right (Button, Flash, Slider, Speed, Cue list, Label), drag to place, resize with
grid snap, properties panel (label, color, function, toggle/flash, key shortcut).

**Button ↔ slider switch (owner request, 2026-10-17):** a toggle in the top toolbar swaps every placed button for a
slider of the same color (and back). In QLC+ you need two separate widgets (button + slider) for this.

**Shows tab and transport bar (built, 2026-10-18).** A fixed **Shows** tab sits left of the page tabs. It lists all
project shows; a **Play** button, double-click or Enter plays the selected show (resumes it when paused).
At the bottom a **show transport bar** shows only the timeline of the active show:

| Control | Behaviour |
|---------|-----------|
| Play / Pause | Space (not while a button has focus); in the Shows list it plays/pauses the selected show |
| Stop | Stops the show; lights release |
| Back / forward | ← / → jump 5 s, Shift+← / Shift+→ jump 1 s |
| Home | Back to 0:00 |
| Timeline | Click or drag to scrub (pointer capture); position / length shown as `m:ss.mmm` |
| Status pill | `STOPPED` · `PAUSED` · `PLAYING` · `OVERRIDE — show muted` |

While a console function runs (default override mode), the show is muted on the lights, the timeline keeps moving and
is drawn with stripes, and the pill reads `OVERRIDE — show muted`; on release the show returns at its current position.
The big skip buttons and Left/Right use the **Seek step** setting (default 5 s); the small buttons and Shift+Left/Right are always 1 s.

### 3.6 Show Timeline (phase 8)

```
[▶][⏸][⏹] 00:42.150 / 03:31   Snap: Beat ▾   Zoom ─●──   [+ Audio] [+ Track]
           0:00      0:15      0:30      0:45      1:00
Audio  ▁▃▆█▆▃▁▂▅█▇▅▂▁▃▆█▇▅▃▁▂▅▇█▆▃▁▂▄▆█▇▅▃▁  song.mp3
Beats  |  |  |  |  |  |  |  |  |  |  |  |  |
Trk 1  [Blue wash      ][Party colors      ][Red  ]
Trk 2           [Pulse ×4]      [Flash]  [Rainbow ...]
               ▲ playhead
Right panel: selected item properties (start, length, fade in/out, intensity) or
"New function for selection" (pick type → editor opens with that duration).
```

### 3.7 Test & Preview (phase 6, partly built 2026-10-19; desk layout 2026-10-20)

Built layout, modelled on the QLC+ **Simple Desk** (owner request 2026-10-20): toolbar (**Universe** picker, **Release test
values**, DMX monitor toggle, search box); a **warnings strip** (red = error, amber = warning: output disabled / output
error, Grand Master < 100%, Blackout on, strobe blocked by the safety setting, unverified profiles, overlapping addresses,
missing profile/mode, footprint past 512, disabled lights); on the left a **Grand Master** strip (percentage, vertical
fader, **GM** button = back to 100 %; the same engine master as the Virtual Console); then the **desk**: the lights of
the chosen universe side by side in patch order, each a band (alternating neutral / green tint) with the light's name
and one narrow column per channel (icon tinted in the light colour, value box, tall fader, channel number, name, status
line, release ✕). The desk scrolls horizontally (the mouse wheel scrolls it sideways) and is virtualized per light.
Clicking a light selects it; the optional **DMX monitor** on the right (off by default) shows the same universe with
the selected light's footprint outlined. The universe list is the project's universes plus any universe a light is
patched to. Only patched lights are shown (no raw unpatched channels, unlike QLC+: test values are keyed by fixture +
profile channel).
**Copy / paste (2026-10-21):** toolbar **Copy** / **Paste** / **Paste to all** (copy one light, paste onto the selected light,
or onto every light on the desk), the same in the right-click menu of a light, and **Ctrl+C / Ctrl+V / Ctrl+Shift+V**. Pasted
values are test values (released on leaving the page). Matching rules: `CopiedLightValues.cs` (see 3.4). Desk columns are
34 px wide with a fixed 128 px slider travel.
The Fixtures Properties *Unverified* / *Verified* badge is a button that marks the profile verified (after testing on
the real light) or unverified again; verified profiles raise no unverified warnings here.

- Faders show the **live output value** (polled ~30 Hz while the page is visible). Moving a fader makes only that channel a
  manual override (wins over show/console/preview; Grand Master, Blackout and strobe safety still apply after it).
  Overridden faders get an accent frame and a per-channel release (✕); each row has **Release light**; the toolbar
  **Release test values** releases only test values (not the engine Release All).
- When the real output differs (Grand Master, Blackout, strobe not allowed) a small "→N" note with the reason appears.
- Mouse wheel over a fader scrolls the list instead of changing the value.
- **Leaving the page releases all test values**; status-bar Release All and project open also reset them. Test values are
  never saved and never mark the project dirty.
- Lights with problems stay listed: missing profile/mode shows no faders and the problem text; disabled lights or lights
  past 512 have disabled faders.
- Not built yet: quick R/G/B/W-only buttons, safe-intensity colour sequence, footprint view beyond the monitor highlight,
  connection diagnostics panel (see `open-questions.md`).

Original sketch:

```
Fixture [Stage L PAR ▾]  Group [▾]     Safe intensity [25% ▾]     Output: ● disabled  [Enable…]
[ Red only ] [ Green only ] [ Blue only ] [ White only ] [ Color sequence ] [ All off ]
┌ Raw channels (named from profile) ─────────┐ ┌ DMX monitor — Universe 1 ─────────────────┐
│ 1 Red    ▮▮▮▮▮▯ 200  │ 5 Dimmer ▮▮▮▮ 255   │ │ 001 200  002 000  003 000  004 000 …       │
│ 2 Green  ▯▯▯▯▯▯   0  │ 6 Strobe ▯▯▯▯   0 ⚠ │ │ (highlighted: selected fixture footprint) │
└────────────────────────────────────────────┘ └───────────────────────────────────────────┘
Warnings: unverified profile, overlapping address, output disabled.
```

### 3.8 Input / Output (phase 7, built 2026-10-18)

Built layout: toolbar with **Enable output** / **Disable output** (Ctrl+E / Ctrl+Shift+E), **Refresh devices**, **Apply
now** (shown when settings changed while output runs); left table of universes (one row per project universe plus
**Add universe**, no panel headers); right settings for the selected universe (Output type, Status, then type-specific fields without per-type headings:
Art-Net — broadcast or node IP, Art-Net universe; sACN — multicast or unicast, receiver IP, priority, source name;
Enttec Pro — COM port; Open DMX — FTDI device; Virtual — refresh rate); network adapter picker; a **Problems** list
(configuration errors and the last output error). Output starts disabled on every launch; configurations are saved
app-level in `settings.json`. A live **status pill** in the status bar shows Disabled / Connected / Sending / Error.

Example, a 2-port Art-Net node (Chauvet DMX-AN): project universe 1 -> Art-Net universe 1 (Port A), universe 2 -> Art-Net
universe 2 (Port B), each with the node's IP (or broadcast). Output settings are app-level and remembered; output always
starts disabled and is enabled by hand.
Original sketch:

```
┌──────────────────────────────────────────────────────────────────────────────────────────┐
│ Universe  Output                         Status              Settings                     │
│ 1         Art-Net → 192.168.1.50 U0      ● Sending 44 fps    [Configure]  [Test] [Stop]   │
│ 2         Enttec DMX USB Pro (COM4)      ● Error: unplugged  [Configure]                  │
│ 3         — none —                                           [Choose output ▾]            │
└──────────────────────────────────────────────────────────────────────────────────────────┘
Outputs available: Art-Net · sACN (E1.31) · Enttec DMX USB Pro · Open DMX (FTDI)
Network adapter [Ethernet 192.168.1.10 ▾]   [Discover Art-Net nodes]
[ Enable DMX output ]  ← big switch; disabled until configuration is valid
```

### 3.9 Settings (built 2026-10-18)

Category list on the left (no "Categories" header or per-section headers), changes saved automatically (`%APPDATA%\DmxPro\settings.json`), **Reset to defaults** button.

| Section | Contents |
|---------|----------|
| Appearance | Theme (dark / light / follow Windows) |
| Safety | On exit, send (blackout / profile defaults); **Strobe** switch (default **off**) and max strobe rate (Hz) with photosensitivity warning; confirm Release All (default off) |
| Show playback | Console override: mute the whole show (default) or only the lights the console touches; seek step (seconds; the big skip buttons and Left/Right on the transport bar) |
| Engine | DMX refresh rate (read-only, 44 Hz) |
| Project & folders | Autosave interval (stored; autosave itself is phase 9), default project folder, extra fixture-profile folders (loaded on next start) |
| About | Version, licences and attribution, settings file path |

Not built yet: accent colour, language.

### 3.10 Stage - Virtual Stage (built 2026-10-21, `Views/StageView.xaml`)

Rail item **Stage** before Settings (Ctrl+8, View > Stage). A 2D top view of a rectangular floor with every patched light
as a coloured disc (`StageCanvas` / `StageLightVisual`, drawn in one control, not one element per light).

- **Toolbar:** Place unplaced, Arrange (line / grid in list order), Remove from stage, Fit (Home), Names toggle, stage
  size (width x depth in metres, 1-200; shrinking pulls lights inside).
- **Left list** (search): all lights; drag one onto the stage to place it, Enter places the selected light, Delete takes it off.
- **Interaction:** click selects; drag moves with a 0.25 m snap (Shift = free); arrow keys nudge; Delete removes from the
  stage; mouse wheel zooms; middle or right drag pans. Lights are auto-placed (rows, back first, in patch order) the first
  time a project is opened on this page.
- **Colours:** the page polls the engine output snapshot at 30 Hz while the page or the live stage window is open, so it
  shows what is actually sent (Grand Master, Blackout and strobe safety included). It works with output disabled and says so in a note. Colour is
  derived from the profile (`StageColorModel`); a dimmer-only light is warm white; strobing lights flash.
- The floor is always dark (#17191D) in both themes. Positions are saved in the project (`"stage"`).
- **Live stage window** (2026-10-22, `Views/StageWindow.xaml`): main toolbar **Stage window** or **Ctrl+Shift+8** opens the
  same stage (toolbar, plan, status line; no lights list) in its own resizable, non-modal window, so it stays in view while
  working on Functions, Virtual Console, Shows or Test & Preview, or on a second monitor. Only one: the button again
  restores and activates it. It has no owner window (it never floats over the main window) and shows in the taskbar;
  closing the main window closes it. Edits in it act on the same layout as the page. Size/position are not remembered.
- Not built: rotation editing, strobe marker, undo (`open-questions.md` 29-31).

## 4. Build order for the menu/shell

1. Menu bar with all menus; finished items work, future items disabled with a "Coming in phase N" tooltip. ✅
2. View: page shortcuts Ctrl+1…9, theme switch, full screen F11. ✅
3. Help: keyboard shortcuts + About dialogs. ✅
4. Each phase enables its menu items as the feature lands.

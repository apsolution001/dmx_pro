# DMX Pro — UI Plan (top menu + every screen)

> The blueprint for the whole app UI. Build screens to match this; update this file when a screen changes.
> **Clickable reference:** `docs/prototype/index.html` shows every screen working; build WPF to match it.
> Phase numbers refer to `docs/roadmap.md`. Items not built yet are shown **disabled** in the app with "Coming in phase N".

## 1. Main window layout

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
│               │                                                                              │
│ ⚙ Settings    │                                                                              │
├───────────────┴──────────────────────────────────────────────────────────────────────────────┤
│ ● Output disabled │ Universe 1 · 0 fps │ Master ▮▮▮▮▮▮▯ 85% │ [BLACKOUT] [RELEASE ALL] │ 120 BPM │ Status bar
└──────────────────────────────────────────────────────────────────────────────────────────────┘
```

- **Left navigation rail** (Ctrl+1 … Ctrl+9): the main workspaces in workflow order.
- **Status bar** is always visible, on every page: output state, Grand Master, Blackout, Release All, BPM.
- **Full screen** (F11) hides the title bar for shows; **Operator mode** (F12) locks editing.

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
| **View** | Fixtures … Settings | Ctrl+1 … Ctrl+9 | Go to a page | ✅ 1 |
| | Stage preview panel | Ctrl+P | Docked live preview of fixture colors | 3 |
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
| **Output** | Enable DMX output | Ctrl+E | Only with a valid configuration; starts disabled every launch | 7 |
| | Disable DMX output | Ctrl+Shift+E | Stops sending (lights may hold last state) | 7 |
| | Blackout | B | Send dark state (≠ disable) | 4 |
| | Release all | Esc | Stop all functions, return to defaults | 4 |
| | Configure outputs… | | Go to Input / Output | ✅ 1 |
| **Tools** | Test fixture… | | Go to Test & Preview | ✅ 1 |
| | Profile editor | | | 2 |
| | Operator mode (lock editing) | F12 | | 5 |
| | Settings | Ctrl+, | Theme, safe frame on exit, strobe safety, folders (page itself arrives in phase 9) | ✅ 1 |
| **Help** | Getting started | F1 | First-run guide | 9 |
| | Keyboard shortcuts | | List of all shortcuts | ✅ 1 |
| | DMX basics | | Built-in explanation of DMX, addresses, universes | later |
| | About DMX Pro | | Version, licences (QLC+ fixture data Apache-2.0) | ✅ 1 |

## 3. Screens

### 3.1 Fixtures (phase 2)

```
[+ Add fixture] [+ Group] [Duplicate] [Delete]                              🔍 search   Universe ▾
┌ Fixture list ─────────────────────────────────────────┐ ┌ Details ─────────────────────────────┐
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

### 3.2 Fixture Library (built in phase 1)

Left: import button, search, profile list with ✓ verified / ⚠ unverified. Right: name, type, source, mode picker, channel
table (Ch, Name, Type, Default, Ranges). Phase 2 adds **New / Edit / Duplicate profile** (profile editor: add channels,
set type, ranges, modes by drag-ordering channels) and **Mark as verified**.

### 3.3 Functions (phase 4)

```
[+ New ▾] [Duplicate] [Delete] [▶ Preview]                                   🔍 search  Type ▾
┌ Function list ────────────────┐ ┌ Editor (changes with type) ────────────────────────────────────┐
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

### 3.4 Scenes — Scene Creator (phase 3)

```
┌ Targets ──────────┐ ┌ Color & channels ───────────────────────┐ ┌ Stage preview ─────────────┐
│ ☑ Front (group)   │ │   ◯ color wheel     R [▮▮▮▮▮▮] 255       │ │  ●  ●  ●  ●   (live colors) │
│ ☑ Stage L PAR     │ │   brightness ▮▮▮▮   G [▮▮    ]  80       │ │                             │
│ ☐ Back bar        │ │   presets: ■■■■■■■■ B [      ]   0       │ │  "Preview only — output is  │
│                   │ │                     W [▮▮▮   ] 120       │ │   disabled"                 │
│                   │ │ Advanced: strobe / macro / speed (only    │ └─────────────────────────────┘
│                   │ │ if the profile has them), raw channels    │
└───────────────────┘ └──────────────────────────────────────────┘
Name [Warm white]  Fade in [0.5s]  Fade out [0.5s]   [Save] [Save as] [Revert]  [Save + add to console]
```

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

### 3.7 Test & Preview (phase 6)

```
Fixture [Stage L PAR ▾]  Group [▾]     Safe intensity [25% ▾]     Output: ● disabled  [Enable…]
[ Red only ] [ Green only ] [ Blue only ] [ White only ] [ Color sequence ] [ All off ]
┌ Raw channels (named from profile) ─────────┐ ┌ DMX monitor — Universe 1 ─────────────────┐
│ 1 Red    ▮▮▮▮▮▯ 200  │ 5 Dimmer ▮▮▮▮ 255   │ │ 001 200  002 000  003 000  004 000 …       │
│ 2 Green  ▯▯▯▯▯▯   0  │ 6 Strobe ▯▯▯▯   0 ⚠ │ │ (highlighted: selected fixture footprint) │
└────────────────────────────────────────────┘ └───────────────────────────────────────────┘
Warnings: unverified profile, overlapping address, output disabled.
```

### 3.8 Input / Output (phase 7)

```
┌ Universes ───────────────────────────────────────────────────────────────────────────────┐
│ Universe  Output                         Status              Settings                     │
│ 1         Art-Net → 192.168.1.50 U0      ● Sending 44 fps    [Configure]  [Test] [Stop]   │
│ 2         Enttec DMX USB Pro (COM4)      ● Error: unplugged  [Configure]                  │
│ 3         — none —                                           [Choose output ▾]            │
└──────────────────────────────────────────────────────────────────────────────────────────┘
Outputs available: Art-Net · sACN (E1.31) · Enttec DMX USB Pro · Open DMX (FTDI)
Network adapter [Ethernet 192.168.1.10 ▾]   [Discover Art-Net nodes]
[ Enable DMX output ]  ← big switch; disabled until configuration is valid
```

### 3.9 Settings (phase 9)

Theme · accent color · safe frame on exit (blackout / profile defaults) · allow strobe + max strobe rate · autosave
interval · profile folders · language.

## 4. Build order for the menu/shell

1. Menu bar with all menus; finished items work, future items disabled with a "Coming in phase N" tooltip. ✅
2. View: page shortcuts Ctrl+1…9, theme switch, full screen F11. ✅
3. Help: keyboard shortcuts + About dialogs. ✅
4. Each phase enables its menu items as the feature lands.

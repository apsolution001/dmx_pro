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
- **Main toolbar, right side:** **Go live** toggle (Ctrl+F5, also **Output > Go live**, checked while live), then
  **Stage window** (Ctrl+Shift+8, also **View > Stage window**; opens the live stage window, section 3.10).
- **Go live** (owner decision 2026-10-22) is a **mode switch like QLC+ Operate mode, not output enable**: off = outline
  broadcast icon + "Go live"; on = accent fill, filled broadcast icon + "Live" (icon and text change, not colour only; no
  play/pause glyphs, which would read as show transport). While live **only the Virtual Console drives
  the lights** — console functions, console shows, Grand Master, Blackout, Release All. Function-editor previews, Test &
  Preview test values and shows played from the Shows page are suppressed **in the engine**. Switching on: the editor
  preview is removed, every test value is **released** (deterministic, like Release All for overrides; they do not come
  back), Shows-page shows stop (with their music). Switching off: what the console runs keeps running (no jump); the
  other sources are allowed again and an open editor previews again by itself. Always **off** on app start and on every
  project open; never saved. Editing and saving functions stays possible while live (saving a function the console is
  playing refreshes it in the engine, as before). Output enable/disable (Ctrl+E, status pill) is unchanged and separate.
  Shortcut is **Ctrl+F5**, not F5: F5 refreshes devices on the Input / Output page and can be a Virtual Console key.
- **Info bar** (above the page): success messages ("Saved show.dmxpro.", "Opened …") hide themselves after about 3 s;
  warnings and errors stay until closed (safety rule 10). Closing the bar clears the message, so the same message (a
  second save) shows again; a newer message replaces the old one and restarts the timer. Import results stay until
  closed.
- **Live reload / auto-save (2026-10-28):** when another program changes the open project file, the change appears in
  place within about half a second ("Reloaded "show.dmxpro": it was changed outside DMX Pro. Ctrl+Z undoes this.",
  auto-hides). Output, live mode, running lights and the console stay as they are. With unsaved changes (or an unsaved
  editor draft) a prompt asks **Reload** / **Keep mine** (Esc = Keep mine); a file that cannot be read shows a warning
  that stays. Auto-save shows "Auto-saved 14:05" (auto-hides); an auto-save error stays as an error.

## 2. Top menu bar

Menu items act on the page they belong to: Fixtures / Functions / Show items first open that page, then run the page's
own command, so menu, toolbar, context menus and page shortcuts behave the same. Edit items act on the **current** page
and are disabled when it has nothing to act on. Items for features that are not built yet stay disabled with a
"Coming soon" tooltip. Shortcuts marked *(list)* work while the page's list / table / timeline has keyboard focus (the
focused list decides what they act on); the others work anywhere in the window. Code: `MainWindow.xaml`,
`ViewModels/ShellViewModel.Menu.cs` (decided 2026-10-22).

| Menu | Item | Shortcut | What it does | Status |
|------|------|----------|--------------|--------|
| **File** | New project | Ctrl+N | Start an empty project (asks to save changes) | ✅ |
| | Open project… | Ctrl+O | Open a `.dmxpro` file | ✅ |
| | Open recent ▸ | | Last 8 opened or saved projects, newest first (app settings, not the project). A file that no longer exists is removed from the list with a message bar warning. Disabled while the list is empty | ✅ |
| | Save | Ctrl+S | Save to the current file | ✅ |
| | Save as… | Ctrl+Shift+S | Save to a new file | ✅ |
| | Import QLC+ workspace (.qxw)… | | Bring fixtures/scenes from QLC+ | Coming soon |
| | Export show report (PDF)… | | Patch sheet: fixtures, addresses, groups | Coming soon |
| | Exit | Alt+F4 | Close (stops functions, safe frame, stops output) | ✅ |
| **Edit** | Undo / Redo | Ctrl+Z / Ctrl+Y (also Ctrl+Shift+Z) | Every project edit (fixtures, groups, functions, shows, console layout, stage, music links). Up to 100 steps; quick edits in a row (a dragged fader) are one step. Tooltip names the step ("Undo: Delete fixture "Par 1" (Ctrl+Z)"); the message bar confirms "Undone: …". Same buttons in the main toolbar; the shortcuts work in the main and the live stage window, but not while a text box has focus (it keeps its own typing undo). Undo never touches output, live mode, Blackout, Grand Master or what is playing (a function that no longer exists stops). An open function editor follows: its function changed → it reopens on the restored version (unsaved draft dropped), deleted → it closes. Undoing back to the last save shows the project as saved; New/Open clear the history | ✅ |
| | Cut / Copy / Paste | Ctrl+X / C / V *(list)* | Functions page: whole functions (§3.3 function clipboard). Disabled on other pages | ✅ |
| | Duplicate | Ctrl+D *(list)* | Fixtures (selected rows), Functions, Shows (timeline item), Virtual Console (widget, edit layout) | ✅ |
| | Delete | Del *(list)* | Same targets as Duplicate, each with its page's confirmation | ✅ |
| | Rename | F2 *(list)* | Function, show, or console page (not in operator mode) | ✅ |
| | Select all | Ctrl+A | Routed to the focused control: all rows of a table (e.g. Fixtures) or all text of a text box | ✅ |
| | Search everything… | Ctrl+K | Command palette: pages, fixtures, functions, actions | Coming soon |
| **View** | Fixtures … Settings (Settings = Ctrl+7) | Ctrl+1 … Ctrl+7 | Go to a page | ✅ |
| | Stage | Ctrl+8 | Virtual Stage page (section 3.10) | ✅ |
| | DMX monitor | Ctrl+M | Opens Test & Preview with its DMX monitor shown (also toolbar *Monitor*) | ✅ |
| | Dark / Light theme | | Switch theme | ✅ |
| | Full screen | F11 | | ✅ |
| **Fixtures** | Add fixture… | Ctrl+Shift+A | Profile → mode → universe → address (auto free address) | ✅ |
| | Add group | Ctrl+G *(list)* | New group from the selected fixtures (empty when none), opens the Fixtures page and starts renaming it | ✅ |
| | Auto-patch selected | | Re-address the selected fixtures one after another; disabled without a selection | ✅ |
| | Import fixture profile (.qxf / .json)… | | Into the fixture library | ✅ |
| | New fixture profile… | | Channel layout editor for a new layout (as *New layout…* in Add fixture) | ✅ |
| **Functions** | New scene / chaser (only these two types) | | Opens the Functions page with the new function in its editor | ✅ |
| | Create starter scenes | | Blackout, Red, Green, Blue, White, Cyan, Magenta, Yellow | Coming soon |
| | Stop all functions | Shift+Esc | Same as Output ▸ Release all (also toolbar *Stop all*) | ✅ |
| **Show** | New show | | Opens the Shows page with a new show | ✅ |
| | Add track | | Adds a track to the show selected on the Shows page | ✅ |
| | Play / Pause | Space *(timeline)* | Plays / pauses the show selected on the Shows page | ✅ |
| | Stop & return to start | | Stops that show and moves its playhead to 0:00 (Home on the timeline only seeks to 0:00) | ✅ |
| **Output** | Enable DMX output | Ctrl+E | Only with a valid configuration; starts disabled every launch | ✅ |
| | Disable DMX output | Ctrl+Shift+E | Stops sending (lights may hold last state) | ✅ |
| | Blackout | Ctrl+B | Toggle; checked while on. Sends a dark state — output keeps sending (≠ disable, safety rule 3). Also toolbar *Blackout* | ✅ |
| | Release all | Shift+Esc | Ends held flashes, then stops every function and show and returns channels to defaults (safety rule 4); asks first only when Settings ▸ *Confirm Release All* is on. Grand Master and Blackout stay | ✅ |
| | Configure outputs… | | Go to Input / Output | ✅ |
| **Tools** | Test fixture… | | Go to Test & Preview | ✅ |
| | Profile editor… | | On the Fixtures page with a light selected: edits its channel layout (as Ctrl+L); otherwise a new layout | ✅ |
| | Operator mode (lock editing) | F12 | Toggles Virtual Console operator mode (checked while on) and opens the Virtual Console | ✅ |
| | Settings | Ctrl+, | Theme, safe frame on exit, strobe safety, folders (§3.9) | ✅ |
| **Help** | Getting started | F1 | First-run guide | Coming soon |
| | Keyboard shortcuts | | List of all shortcuts | ✅ |
| | DMX basics | | Built-in explanation of DMX, addresses, universes | Coming soon |
| | About DMX Pro | | Version, licences (QLC+ fixture data Apache-2.0) | ✅ |

**Shortcut changes (2026-10-22):** Blackout is **Ctrl+B** (a bare **B** cannot be a window shortcut: it would eat the
letter in text boxes and clash with console widget keys) and Release all is **Shift+Esc** (bare **Esc** already closes
editors, clears searches and leaves full screen). Live actions (Blackout, Release all) show no message bar, so the
console layout never moves during a show.

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
channel order. Live validation lists the `LAYOUT_*` codes (errors block Save, warnings do not). Footer: Use it for, Cancel, Save.
When opened from a built-in profile with lights using it: **Use it for** selected lights / all lights of that profile / no lights.

### 3.2 Fixture Library (built in phase 1; page removed from navigation 2026-10-17)

Left: import button, search, profile list with ✓ verified / ⚠ unverified. Right: name, type, source, mode picker, channel
table (Ch, Name, Type, Default, Ranges). The profile editor was replaced by the channel layout editor (§3.1); ranges and **Mark as verified** are not built.

### 3.3 Functions (phase 4)

```
[+ New ▾] [Duplicate] [Delete]                                               🔍 search  Type ▾
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
Types: Scene, Chaser (the New menu offers only these; the other types were removed 2026-10-28). Copy/paste and duplicate everywhere.

> **Status 2026-10-18:** the engine and the Core model supported eight types; since 2026-10-28 only Scene and Chaser remain. The Functions page has the scene editor
> (§3.4) and the chaser step grid (`Views/Editors/`, `ViewModels/Editors/`). Since 2026-10-22 the open editor previews its draft on the lights automatically (below).

**Chaser editor (rebuilt 2026-10-27, owner: "simple and obvious"):** one page, no tabs (only a handful of settings).
`Views/Editors/ChaserEditorView.xaml`, `ChaserEditorViewModel`, list logic in `ChaserStepListEditing` (unit-tested).

```
┌──────────────────────────────────────────────────────────────┐║┌─────────────────────────────┐
│ [+ Add steps… ▾] │ [⧉][−][↑][↓]                2 of 4 selected│║│ Playback                    │
│ Plays [1][2][3][4]  then starts again   (step colours, in play order)║│ Run order (•) Loop          │
│ ⠿ #  ● ◐ Scene / function      Hold (s)  Fade in (s)  Fade out (s)║│           ( ) Play once     │
│ ⠿ 1  ● ◐ Red                   [1]       [0]          [0]      │║│           ( ) Back and forth│
│ ⠿ 2  ● ◐ Blue                  [1]       [0.5]        [0]      │║│ Direction (•) Forward ( ) Backward
│                                                              │║│ ◷ Cycle: 4 s                │
│                                                              │║│ Step timing                 │
│                                                              │║│ Hold / Fade in / Fade out   │
│                                                              │║│ [Apply to selected (2)]     │
│                                                              │║│ [Apply to all steps]        │
└──────────────────────────────────────────────────────────────┘║└─────────────────────────────┘
```
- **Steps list:** number, colour swatch of the step's function (on screen only; dashed ring = no colour), type icon,
  name, editable Hold / Fade in / Fade out seconds (Enter or Tab applies, invalid text reverts). Several steps can be
  selected (Ctrl / Shift + click, Ctrl+A). Remove, duplicate (copies go after the last selected step) and move up / down
  act on the whole selection; dragging the ⠿ grip moves the selected steps (accent line shows where they land). Keys:
  Ins add, Del remove, Ctrl+D duplicate, Ctrl+Up / Ctrl+Down (also Alt+arrows) move. A deleted function shows as
  "(missing function)" with a warning icon and plays nothing. Removing is not confirmed: the editor is a draft, Revert
  brings steps back until Save.
- **Add steps…** (also the empty-state button, Ins): searchable picker with swatches; Enter in the search box adds the
  first match, Enter / double-click in the list adds the picked ones (Ctrl / Shift picks several). New steps go after
  the last selected step (or at the end), get the "Step timing" times and become the selection; the picker closes.
  The chaser itself and functions that would make it play itself are not offered.
- **Colour strip ("Plays"):** one cycle in play order (direction and back-and-forth applied), numbered blocks, selected
  steps outlined; the text after it says "then starts again" / "then stops".
- **Playback:** Run order *Loop* / *Play once* / *Back and forth* and Direction *Forward* / *Backward* as radio buttons
  with one-line tooltips; the cycle length below ("Cycle: 8 s", "Runs once: 7 s", "There and back: 10 s").
- **Step timing:** Hold, Fade in, Fade out (seconds) that new steps get (starts with the first step's times, else 1 / 0 /
  0). *Apply to selected (n)* and *Apply to all steps* copy them onto existing steps ("same time for all steps").
- Empty state: "No steps yet — Add scenes to play them one after another." with an **Add steps…** button.

**Colour cycle, Fade, Pulse, Flash and Strobe editors: removed (2026-10-28).** DMX Pro has only Scene and Chaser;
old projects convert on load (decisions log 2026-10-28). Equivalents: a colour cycle = a chaser of colour steps with fades; a fade = a 2-step chaser; a flash = a scene on a VC flash button; a strobe = a scene using the strobe channel.

**Sequence editor: merged into the Chaser editor (done 2026-10-28).** There is no Sequence type or editor any
more; `New ▸ Sequence` is gone and old sequences open as chasers. The step-look panel below reuses the former Sequence
editor's Colours / All channels views. Design: `docs/superpowers/specs/2026-10-27-chaser-steps-design.md`.

**Chaser steps (built 2026-10-28; replaces the "Add steps…" picker above):** a step is either an **own look**
(set right in the chaser, no function in the list) or a **function step** (a copy of a chaser). Both are owned by the chaser, hidden from the Functions list, and can be mixed and reordered.
- **Toolbar:** **Add step** (new own look; copies the selected step's look, or starts blank with the lights of the nearest
  own-look step above when a function step is selected), **Add blank step**, **Add from function…** (the searchable picker;
  a scene becomes an own-look step, other types a function step; always a copy, the original is never changed),
  **Save as function…** (makes a library copy of the selected step; the step keeps its own). Duplicate / Remove / Move stay
  as above. Keys: Ins = Add step, Ctrl+E = Edit step, Ctrl+Up/Down = move. New step functions stay in the editor draft
  until Save; the live preview includes them.
- **Step-look panel** (right, own-look step selected): Choose lights for this step (defaults to the previous step's
  lights), colour presets / **Off** / level sliders, and the **All channels** view; the live preview holds the selected
  step, with the header switch **This step / Whole chaser**.
- **Function step selected:** the panel shows its kind, name and a short summary, plus **Edit step**. If the chaser has
  unsaved changes the usual Save / Don't save / Cancel prompt comes first; then the copy's normal editor opens in the
  same area with a **"← Back to chaser 'Name'"** link.
- **Linked mark:** a step of an old chaser that still points at a library function shows a small "linked" mark. The first
  edit of it makes it the chaser's own copy and shows a note: "This step is now the chaser's own copy; the original
  'Red wash' is unchanged." No "Make independent" button.

**Select ≠ edit ≠ play** (decided 2026-10-17, preview changed 2026-10-22 — selecting never changes the lights; the open
editor shows its draft on the lights automatically while not live):

| User action | Result | Lights change? |
|---|---|---|
| Single click | Select; right panel shows summary + on-screen stage preview | No |
| Double-click / Enter / **Edit** button | Open the editor in the right panel; its function shows on the lights (**live edit preview**) | Yes, while the editor is open and live mode is off |
| Edit anything in the editor | The lights follow the unsaved draft (~25 updates/s while dragging) | Yes |
| Close / Save and close / Cancel / Esc / other page | Preview stops at once; lights return to whatever else runs, or to defaults | Back to previous |
| Right-click | New ▸ · Edit · Rename (F2) · Duplicate (Ctrl+D) · Copy (Ctrl+C) / Paste (Ctrl+V) · Add to Virtual Console · Delete | — |
| Ctrl+X / Ctrl+C / Ctrl+V in the list (or Edit menu) | Cut / copy / paste whole functions (in-app clipboard, see below) | No |
| **Add to Virtual Console** | Adds an on/off button for the function at the first free spot of the current console page (a new page when there is none); message bar confirms, "page is full" dialog otherwise | No |

- **Live edit preview (owner decision 2026-10-22):** while a function editor is open (Functions page, and the same editor
  panel on the Shows page) and **Go live** is off, the function being edited is shown on the real lights automatically
  and follows every edit of the **unsaved draft** — there is no Preview button and no "Live on lights" switch. Edits are
  throttled to one update per ~40 ms, so a dragged fader stays smooth. Chasers run live; changing them keeps them running (no restart). A scene's fade-in is skipped (the draft shows at
  once); the preview also ends without fade. **Switching to another program keeps every preview on the lights, flashes included** (owner decision
  2026-10-22: the operator switches apps while the lights are set). A
  single-shot chaser plays once; the next edit plays it again.
- **Preview stops** (deterministically) on: editor close / save-and-close / cancel, Esc closing the editor, another
  function opened, leaving the page, project replaced, **Go live** on, app close and **Release All**. After Release All it
  stays off until the next edit in the still-open editor ("Released — change something to show it again"); after Go live
  is switched off it comes back by itself. A Release All from anywhere always wins over a pending throttled update (the
  update is dropped, not sent). From the start of the close sequence nothing is previewed any more: the engine is put in
  live mode right after Release All, so nothing can come back before the safe frame (safety rule 6). After a fixture is
  re-addressed (or the patch changes otherwise) the engine drops the old draft and shows the re-sent one, so it never
  lands on the old channels.
- **Editor toolbar hint** (neutral info, not a warning) instead of the removed toggle: "Showing on lights" (output on,
  not live), "Output off — not on lights", "Live mode — not on lights", "Released — change something to show it again".
- **Playing for the show** happens only from the Virtual Console or the Show Timeline, never by clicking in this page.
- **Function clipboard (2026-10-22):** Copy keeps a snapshot of the whole function (later edits or deleting the source
  do not change it). Paste adds a new library function named `<name> (copy)`, `<name> (copy 2)`, … and selects it (filters
  are cleared if they would hide it); pasting a show's copy gives a normal listed library function. Cut = Delete with
  its usual confirmation, then copy (cancelling keeps the clipboard). In memory only, separate from the Windows
  clipboard, and cleared when another project is opened (functions refer to that project's lights by id).
- **Preview rules:** one preview at a time (the most recently opened editor wins); preview is layered **on top** of
  running functions (it wins on the channels it touches) and never stops them; it never enables output — with output
  disabled nothing reaches the lights; Grand Master, Blackout, "allow strobe" and the maximum strobe rate (safety rule 8)
  and clamping apply.
- **Engine:** a dedicated preview layer after running functions and before manual overrides; the draft is compiled on
  the UI thread and swapped in by the engine (`docs/architecture.md` §4).
- The Scene Creator (§3.4) follows the same rules.

### 3.4 Scenes — Scene Creator (phase 3)

```
[General] [Channels]            Fade in [0.5s]  Fade out [0.5s]      (editor toolbar, scenes only)
┌ General: lights in the scene ──────────────────────────────────────────────────────────────────┐
│ [Add / choose lights…] [Remove]                                     Find light [__________]   │
│ Name          Profile        Mode     Universe·Address                                        │
│ Stage L PAR   Generic RGBW   4-ch     1·1                                                     │
│ Back bar      Generic RGB    3-ch     1·5                                                     │
└────────────────────────────────────────────────────────────────────────────────────────────────┘
Channels tab: searchable multi-select light list + fader banks (all channel levels are set here).
```

**Choose lights dialog (2026-10-22):** a modal dialog replaces the old Add flyout in the scene, effect and sequence
editors. Opened by the **Add / choose lights…** button or **Ctrl+L** in the editor's Fixtures panel. The **Lights** tab
lists every patched light: click selects one, **Shift+click** selects a range, **Ctrl+click** toggles, **Ctrl+A** selects
all; a search box filters the list; **Select all** / **Clear** buttons act on the visible lights. The **Groups** tab
shows one checkbox per group, tri-state (all / some / none of its lights selected). A footer note reads "Unselected
lights are removed, new ones go to the end." — OK applies the selection, Cancel leaves the editor unchanged.

**Copy / paste (2026-10-21, scene and sequence editors):** the Channels tab toolbar, at the left of the fader strip, has
**Copy** (the first selected light) and **Paste to selected** (every selected light); there are no per-light buttons. Copy
takes the included channels (all when none is included), paste includes them. One in-memory clipboard shared with the Test
page; matching rules (same profile by channel key, otherwise by channel type and occurrence) are in
`src/DmxPro.Core/Profiles/CopiedLightValues.cs`. Faders are compact (36 px columns, 128 px travel).

Implemented as `SceneEditorPanel` (shared by the Functions page and the Shows page), with two tabs:

- **General** — lights only: **Add / choose lights…** (opens the Choose lights dialog, below), **Remove**, **Find light** search, and a list (name, profile,
  mode, universe·address). Colour presets and quick colour/dimmer sliders were removed
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

- **Live edit preview (2026-10-22):** an editor opened from the timeline (the show's own copy) previews its draft on the
  lights like on the Functions page (§3.3): it starts when the editor opens while the page is visible, stops when the
  editor closes or another opens, and leaving the page stops it (it comes back on return).
- **Go live:** while live only the Virtual Console controls the lights, so the **whole transport is disabled** on this
  page: Play / Pause / Resume (Space), Stop, Go to start (Home), Rewind / Fast forward and the ruler (click / drag does
  not seek; arrow cursor, hint tooltip). The commands' CanExecute and the methods themselves both refuse, so keyboard
  shortcuts are covered too. The playhead stays read-only and still moves to show what the console is playing (Follow
  playhead keeps scrolling). A neutral hint strip reads "Live mode — shows play from the Virtual Console; the timeline
  only follows". Turning live on stops a show started here (with its music) and cancels a ruler drag in progress without
  seeking. Timeline **editing** (moving / resizing blocks, properties, tracks, placing functions) stays available while
  live — it changes the project, never the player position.

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
No page shows "unverified profile" warnings (owner decision 2026-10-22: lights are checked in QLC+ first); the
Fixtures Properties panel has no Verified badge any more.

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
- **Go live (2026-10-22):** turning live mode on releases every test value; while live the channel faders, Paste /
  Paste to all and **Save as function…** are disabled (the faders still show the live output) and a neutral banner says
  "Live mode — only the Virtual Console controls the lights. Test values are off." The Grand Master strip stays usable.
  Test values are **not restored** when live mode is turned off.
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
| Project & folders | **Auto-save** on/off (default on) + every *n* minutes (1–60, default 5); **Live reload**: reload the project when its file changes on disk (default on); default project folder, extra fixture-profile folders (loaded on next start) |
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
  The live safety shortcuts also work while it has focus, with the same commands as the main window: Ctrl+B Blackout,
  Shift+Esc Release all, Ctrl+E / Ctrl+Shift+E output enable / disable, Ctrl+F5 Go live, Ctrl+S Save, and Ctrl+Z / Ctrl+Y
  (Ctrl+Shift+Z) Undo / Redo.
- Not built: rotation editing, strobe marker (`open-questions.md` 29-30). Stage edits are undoable (Edit ▸ Undo, 2026-10-23).

## 4. Build order for the menu/shell

1. Menu bar with all menus; finished items work, future items disabled with a "Coming in phase N" tooltip. ✅
2. View: page shortcuts Ctrl+1…9, theme switch, full screen F11. ✅
3. Help: keyboard shortcuts + About dialogs. ✅
4. Each phase enables its menu items as the feature lands.

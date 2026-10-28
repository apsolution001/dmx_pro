# DMX Pro — Architecture

> Proposed architecture. Changes must be reflected here (agents: update this file in the same commit).

## 1. Solution layout

```
DmxPro.sln
├─ src/
│  ├─ DmxPro.Core/          net8.0          Domain model, profiles, project I/O, validation (no UI, no hardware)
│  ├─ DmxPro.Engine/        net8.0          Playback engine: functions runtime, merge (HTP/LTP), grand master, timeline
│  ├─ DmxPro.Output/        net8.0          IDmxOutput: Art-Net, sACN, Enttec USB Pro, Open DMX, Virtual/Loopback
│  ├─ DmxPro.Audio/         net8.0-windows  NAudio decode/playback/capture, analysis (BPM, beats, energy, sections)
│  └─ DmxPro.App/           net8.0-windows  WPF app: Views, ViewModels, controls, themes, DI host
├─ tests/
│  ├─ DmxPro.Core.Tests/    xUnit
│  ├─ DmxPro.Engine.Tests/  xUnit
│  ├─ DmxPro.Output.Tests/  xUnit (packet byte-level tests)
│  └─ DmxPro.Audio.Tests/   xUnit (synthetic click tracks)
├─ profiles/                Built-in fixture profiles (*.dmxprofile.json)
└─ docs/
```

Dependency direction (no cycles): `App → Engine → Core`, `App → Output → Core`, `App → Audio → Core`.
**Core, Engine, Output must not reference WPF.** They are unit-testable on any OS.

## 2. Technology choices

| Concern            | Choice                                                             |
|--------------------|--------------------------------------------------------------------|
| UI framework       | WPF .NET 8                                                         |
| Modern controls    | **WPF-UI** (`Wpf.Ui`, lepoco, MIT) — Fluent, Mica, NavigationView, dark/light |
| MVVM               | **CommunityToolkit.Mvvm** (source-generated `[ObservableProperty]`, `[RelayCommand]`) |
| DI / hosting       | `Microsoft.Extensions.Hosting` + DependencyInjection               |
| Serialization      | `System.Text.Json` (source-generated context), polymorphic functions |
| Logging            | `Microsoft.Extensions.Logging` + Serilog file sink                 |
| Audio              | NAudio; FFT via NAudio.Dsp or FftSharp                             |
| Serial / USB       | `System.IO.Ports` (Enttec Pro VCP), FTDI D2XX wrapper (Open DMX)   |
| Tests              | xUnit + FluentAssertions (or Shouldly)                             |

## 3. Runtime threads

```
 UI thread (WPF Dispatcher)  ── commands ──▶  Engine (dedicated thread, fixed tick ~44 Hz)
        ▲                                         │ writes merged universe buffers
        │  snapshot (throttled ~30 Hz)            ▼
        └────────────────────────────────  Output workers (one per output, own thread/timer)
                                                  ▲
 Audio thread (NAudio) ── clock / beat events ────┘ (engine reads audio position each tick)
```

- UI never writes DMX directly; it sends **commands** (start function, set master, set channel override).
- Engine publishes **immutable snapshots** for preview/monitor; UI polls/receives them throttled.
- Outputs copy the latest frame and transmit at their own rate; failures raise status events, never crash the engine.

## 4. Engine frame pipeline (per tick)

1. Advance time (`delta` from high-resolution clock or audio clock for shows).
2. Each running function writes its contribution (with fade progress and function intensity), in **priority layers**:
   **show playback** (lowest) → **Virtual Console** (buttons/sliders/flash). A higher layer wins on the channels it
   touches; when it releases, the lower layer's values show again (owner decision 2026-10-17: the console overrides a
   running show).
3. Merge inside a layer: HTP for intensity types, LTP for others (latest start time wins).
4. Apply the **preview layer** (the open function editor's unsaved draft, shown automatically while not live; it wins
   on the channels it touches and is removed on editor close, page change, project open, live mode on or Release All —
   `ui-plan.md` §3.3), then manual overrides (Test tab / Simple desk). Both are skipped in **live mode** (below).
5. Color policy (RGB→RGBW), software dimming where no Dimmer channel.
6. Grand Master (intensity only) → Blackout.
7. Clamp, write per-universe `byte[512]`, publish snapshot, hand to outputs.

Details (`src/DmxPro.Engine/DmxEngine.cs`, `Runtime/`):

- **Layers:** `EngineLayer.Show` → `Console` → `Preview`, each with its own buffer. All commands only queue work; the
  next tick applies it (`CommandQueue`). The tick allocates nothing.
- **Show override mode** (`ShowOverrideMode`): `MuteWholeShow` (default) — while any console function runs, shows are
  muted on **all** channels but keep advancing, and reappear on release; `TouchedChannelsOnly` — plain layer priority
  (the 2026-10-17 behaviour). Preview, manual overrides, Grand Master and Blackout apply on top in both modes.
- **Show transport:** `StartShow(id, position)`, `PauseShow`, `ResumeShow`, `SeekShow`, `StopShow`. Programs render by
  **elapsed time** (not accumulated deltas), so a seek shows the correct step/fade level at once.
  `TryGetShowPlayback` returns a `ShowPlaybackInfo` (position, `IsPaused`, `IsMuted`) from the latest snapshot without
  copying universes (cheap for a 30 Hz UI timer).
- **Function programs:** `FunctionCompiler` turns each Core function (scene, chaser) into a program (`SceneProgram`, `StepProgram`) when a project is loaded or refreshed. The colour cycle,
  fade, pulse, flash and strobe programs were removed 2026-10-28. Every program time (fades, holds,
  durations, periods, offsets) is capped at `FunctionEditing.MaxSeconds` (1 h) when compiling, so damaged files cannot
  overflow a chaser's pass length to infinity; `ProjectSerializer` also clamps loaded function times to the editor
  range (`FunctionTimes.ClampTimes`).
- **Live edit preview (2026-10-22):** `SetPreviewFunction(project, draft)` compiles one (unsaved) `LightFunction` on the
  calling (UI) thread against the patch of the latest model (`EngineModel.PatchIndex`; referenced scenes/steps come from
  the project as saved; a timed flash compiles as held) and queues the program; the tick only swaps it in. The same
  draft id keeps its `RunningFunction` (elapsed time and level stay, effects keep running); anything else in the preview
  layer goes. Draft previews start at full level and stop without fade (`ClearPreview`, `StopPreview`,
  `StopFunction(id, Preview)`). The call reads `ReleaseGeneration` before compiling and carries it in the queued command;
  preview-removing commands (Release All, project open, live on) carry their own incremented generation, the tick keeps
  the highest one applied and drops a draft with an older generation, so a release queued while the draft compiled
  always wins (allocation-free; clearing overrides does not drop drafts). A draft is not
  part of any model: every model carries a `PatchVersion` (kept by `RefreshPatch` when universes and every fixture
  channel → flat index are unchanged, new otherwise, always new on `LoadProject`); `RefreshPatch` keeps a running draft
  only while the patch version is unchanged (the app re-sends the draft after every refresh) and a draft compiled for
  another patch version is ignored, so a re-addressed fixture never gets the old flat indexes. App side:
  `FunctionPreviewService` (`Attach`/`Detach` from the page, `FunctionEditorViewModel.DraftChanged`, ~40 ms trailing
  throttle, `BuildPreviewFunction()` = deep copy with the draft applied, editor hint `PreviewStatus`). Before each send
  it compares `ReleaseGeneration` with the one it last adopted (attach, edit after a release, live off), so a release
  from any thread wins over a pending throttle tick; switching to another program does not release anything
  (owner decision 2026-10-22); it stops on `EngineRuntimeService.ShuttingDown`. The close sequence switches
  the engine to live mode right after `ReleaseAll` (rule 6).
- **Live mode (2026-10-22, "Go live"):** `SetLiveMode(bool)` is an engine-level gate, applied in queue order. On: the
  preview layer is emptied, every manual override is cleared (bumps `ReleaseGeneration`, so `ChannelTestSession`
  forgets them) and every show started with `ShowOrigin.Editor` stops; while on, preview starts/drafts, `SetChannelOverride`
  and editor show starts are ignored (and the preview layer is skipped in the render). Console functions, console shows
  (`ShowOrigin.Console`, the default), Grand Master, Blackout and Release All are untouched; switching off changes
  nothing that runs. Not output enable (safety rules 1–3). App side: `ILiveModeService` / `LiveModeService` (off on every
  project open, never persisted; stops editor shows through `EngineConsolePlayback.StopEditorShows` so their music stops),
  `IConsolePlayback.PlayShow(id, from, ShowOrigin)` refuses editor starts while live.
- **Speed:** `SetFunctionSpeed(id, layer, factor)` scales a running function's own time (0..8, a new start resets it
  to 1); fades on start/stop stay real time. The console Speed widget feeds it (per-function speed × global speed).
- **Strobe enforcement (safety rule 8):** `AllowStrobe` (off = every Strobe channel forced to its safe value) and
  `MaxStrobeRateHz` (caps chaser step changes × speed to no faster than 1/(2×max rate) per step, or 1/6 s while strobe is not allowed; each running chaser re-bases its time on a limit change and a per-instance guard holds earlier step changes; shared channels crossfade linearly; 0 = steady light). Snapshots carry a `[Flags] StrobeLimit`
  (`RateLimited`, `Suppressed`) so the UI can show it. Hardware strobe channels cannot be rate-limited (profiles have no
  value → Hz calibration).
- **Safe frame:** `CreateSafeFrame(universe, SafeFrameMode)` — profile defaults (strobe at its safe value), or
  blackout (intensity 0). Used on close.
- **Core functions** (`src/DmxPro.Core/Functions`): `LightFunction` is polymorphic on `"type"` (only Scene and Chaser; legacy types are load-only, see below);
  `FunctionFactory` creates them, `FunctionReferences` validates references
  (`CHASER_MISSING_FUNCTION`, `FUNCTION_MISSING_FIXTURE`, `SEQUENCE_UNKNOWN_CHANNEL`,
  `FUNCTION_REFERENCE_LOOP`). `RgbColor` is saved as `"#RRGGBB"`.

- **Function ownership (built 2026-10-28):** `LightFunction.OwnerShowId` (show copies) and
  `OwnerFunctionId` (chaser steps) mark a function as owned; a function has **at most one owner**. Owned functions are
  hidden from every list (`FunctionListItems`, Virtual Console targets, Shows library, pickers, fade scene lists). A
  chaser step is an own look (owned hidden `SceneFunction`) or a deep copy of a library function; the engine plays steps
  as before. Lifecycle (Core, tested): delete chaser = delete its owned functions; remove step = delete its owned
  function; duplicate / clipboard copy-paste / `PlaceFunctionInShow` = deep copy of the chaser and its owned functions
  (re-pointed, nothing shared); deleting a show cleans up its chaser copies' owned functions. The chaser editor keeps new
  step functions in its draft until Save, then commits chaser and owned functions as one undo step. Old chasers whose
  steps point at library functions keep working; the first edit of such a step makes it an owned copy. Step helpers:
`src/DmxPro.Core/Functions/ChaserSteps.cs` (`NewOwnLookScene`, owned copies).
- **Preview with draft functions:** `DmxEngine.SetPreviewFunction(project, draft, draftFunctions)` takes the editor's
  unsaved owned step functions too (compiled next to the project's own), so the live preview of a chaser shows steps
  that are not saved yet. The chaser editor's preview is "This step" or "Whole chaser".
- **Sequence → Chaser migration:** on load each `SequenceFunction` becomes a `ChaserFunction` with the same id, name, run
  order, direction and timing; each step becomes an own-look step (owned hidden scene limited to the sequence's lights, fades 0).
  Code: `src/DmxPro.Core/Projects/SequenceMigration.cs`. A new Sequence can no longer be created.
  References (VC widgets, show items, chaser steps, fades) keep working; output is frame-identical (engine test). Files
  are saved as chasers.
- **Legacy function migration (2026-10-28):** `src/DmxPro.Core/Projects/LegacyFunctionMigration.cs` runs in
  `ProjectSerializer.Deserialize` after `SequenceMigration`. It converts old functions in place (same id, name,
  ownership): Colour cycle -> looping chaser, one step per colour; Fade -> chaser that plays once (From step, then To
  step with fade in = duration); Pulse -> looping bright/dark chaser with fades; Flash -> scene (VC flash buttons stay
  momentary); Strobe -> a 2-step on/off chaser for every light with the shutter held open (software mode), or a scene setting the strobe channel (hardware mode). Files are
  saved as scenes/chasers only.

## 5. Project file

- Extension **`.dmxpro`** — JSON (`schemaVersion`), optionally zipped later to embed audio/profile copies.
- Contains: profiles used (embedded copies, so projects are portable), fixtures, groups, scenes, functions,
  shows (timelines), console pages/widgets. Output configs and settings are currently **app-level**
  (`settings.json`, see §6), not in the project; they never contain an enabled flag.
- Stable GUID ids for all entities; references by id. Additive since 2026-10-27: `ownerFunctionId` on a function (absent = not owned); older files load unchanged.
- Migrations: `IProjectMigrator` per schema version.
- Auto-save (2026-10-28): the open project is saved **into its own file** through the normal save path (atomic write +
  `.bak`) every 5 min (setting 1–60), only when it has a file and unsaved changes; see section 11. Crash recovery from a
  separate autosave folder is not built.
- Live reload (2026-10-28): a change of the open file by another program is applied in place (section 11).

## 6. App structure (implemented in phase 1)

```
src/DmxPro.App/
├─ App.xaml(.cs)        Generic Host + DI, theme (WPF-UI dark), page DataTemplates
├─ MainWindow.xaml      FluentWindow: nav rail, page header, InfoBar, content, status bar
├─ Services/            ProjectService (open/save/dirty), ProfileLibraryService, file & message dialogs, AppPaths
├─ ViewModels/          ShellViewModel, PageViewModel base, one ViewModel per page
├─ Views/               One UserControl per page (selected by DataTemplate on the ViewModel type)
└─ Converters/
```

- Navigation is ViewModel-first: `ShellViewModel.SelectedItem.Page` is rendered via DataTemplates in `App.xaml`.
- Profile folders: built-in `profiles/` (copied next to the exe) and user `%APPDATA%\DmxPro\profiles` (imports land here).
- Projects default to `Documents\DmxPro`.
- **Custom channel layouts:** Core `Profiles/ChannelLayout` + `ChannelLayoutRow` (draft model + `LAYOUT_*` validation, builds a
  `FixtureProfile` with `UserDefined = true`); `Project.UpdateProfile` swaps a profile in a project and returns a
  `ProfileUpdateResult` (`PatchIssues`, `FunctionIssues`). App: `ChannelLayoutViewModel` / `Views/ChannelLayoutWindow`;
  `ProfileLibraryService.SaveUserProfileAsync` / `DeleteUserProfileAsync` write `%APPDATA%\DmxPro\profiles\<id>.dmxprofile.json`.
- **App.xaml styles:** `App.ChainImplicitStylesToWpfUi` rebuilds implicit styles on WPF-UI's base at startup
  (see `docs/standards/ui-ux-guidelines.md`, "XAML resource rules").

### Runtime wiring (`Services/`)

- `EngineRuntimeService` owns the `DmxEngine` + `EngineHost` thread. It loads the current project on start, recompiles on
  project change/edit (coalesced per dispatcher turn), and disables output on every project open (safety rule 1).
- `OutputFrameSink` (an `IFrameSink`) hands each engine frame to the `OutputManager` (`DmxPro.Output`).
  `EngineConsolePlayback` (`IConsolePlayback`) is the UI-facing facade for console/show start-stop, pause, seek, speed.
- `SettingsEngineBridge` pushes app settings (allow strobe, max strobe rate, override mode, safe frame) into the engine.
- **Close sequence** (`MainWindow.xaml.cs` → `EngineRuntimeService.ShutdownAsync`, safety rule 6): Release All (stop
  functions) → one safe frame per universe (setting: blackout or profile defaults), awaited → stop all outputs → stop
  the engine thread. Idempotent, never throws, never blocks the UI thread.
- **App settings** (`AppSettings`, `AppSettingsService`): `%APPDATA%\DmxPro\settings.json`, not in the project file —
  theme, safe frame on exit, allow strobe (default **off**), max strobe rate, confirm Release All, console override mode,
  seek step, DMX refresh rate, auto-save on/off (default on) + interval (default 5 min, 1–60; an old stored 0 reads as 5),
  reload project when the file changes on disk (default on), default project folder, extra profile folders, sACN CID, and the
  **output configurations** (per universe). There is deliberately no "output enabled" flag (safety rule 1).

### Test & Preview wiring (phase 6)

- `DmxPro.Engine/Testing/ChannelTestSession`: UI-thread-only bookkeeping of test overrides in the engine's manual-override
  layer, keyed by fixture + channel key. It handles shared addresses of overlapping lights, `Reapply` after a patch refresh
  (overrides follow re-addressed lights) and `Reset` after an external Release All.
- `DmxPro.Core/Testing/PatchHealth` + `FixtureHealth`: per-light patch problems (missing profile/mode, overlap, footprint
  past 512, disabled) used by the warnings strip and row state.
- Events: `EngineRuntimeService.ProjectLoaded` / `PatchRefreshed`, `EngineConsolePlayback.AllReleased`;
  `PageViewModel.OnNavigatedFrom` lets a page clean up (the Test page releases its test values there).
- Layer order is unchanged: show → console → preview → manual overrides → Grand Master → Blackout (+ strobe safety).

## 7. UI navigation

Left `NavigationView` (WPF-UI): **Fixtures · Functions · Scenes · Virtual Console · Music Show · Test & Preview ·
Input/Output · Stage · Settings** (Settings = Ctrl+7, Stage = Ctrl+8; Stage added 2026-10-21). Top bar: project name, Save, Undo/Redo, command palette.
Bottom status bar: **Output pill** (Disabled / Connected / Sending 44 fps / Error), Grand Master slider, **Blackout**,
**Release All**, BPM.

## 8. Virtual Stage (2026-10-21)

- **Core** (`src/DmxPro.Core/Stage/`): `StageLayout` (width/depth in metres, clamped 1..200; placements with x from the
  left and y from the front; `Resize` pulls lights inside; `Normalize` on load), `StageArrange` (`PlaceUnplaced` in rows,
  back first, in patch order; `ArrangeInLine` / `ArrangeInGrid` in the given order), `StageColorModel` (profile-driven mix
  R+W+A+0.4UV / G+W+0.75A / B+W+0.8UV, normalised to hue + intensity; dimmer multiplies; dimmer-only = warm white;
  strobe flashing from profile capabilities, else a default-based fallback of 1-20 Hz capped at 25).
- **Project file:** `"stage": { width, depth, placements: [{ fixtureId, x, y, rotation }] }`, no schema bump (missing = empty).
  `RemoveFixture` drops its placements; duplicated lights are unplaced.
- **App:** `StagePageViewModel` polls the engine **output** snapshot at 30 Hz (so GM, Blackout and strobe safety are
  included) only while it has a **viewer**: `AddViewer()` / `RemoveViewer()` count them; the Stage page (navigation) is one
  viewer, the live stage window (`StageWindowService`, one shared `StageWindow`) another. Polling and rebuilds stop when the
  last viewer goes (a pending drag is committed). `StageCanvas` / `StageLightVisual` draw all lights in one control; every
  attached canvas follows the same ViewModel events.

## 9. Performance architecture (2026-10-21)

- `PageHost` keeps page views alive (collapsed when hidden); Fixtures/Functions/Shows rebuild only when
  `ProjectService.Revision` changed. `ProjectService.MarkChanged(affectsPlayback)`: the engine recompiles only for
  playback-relevant edits (console, groups and renames skip it).
- Engine compile shares identical scene arrays (`ArrayInterner`) and resolves addresses through `PatchIndex`; the show tick
  binary-searches the items near the playhead (`ShowProgram.FirstLiveIndex`).
- Fixtures `DataGrid`: row virtualization on, selection synced in code-behind. Overlap checks run per universe;
  `ScenePreviewColor` is O(n).
- (2026-10-22) `Project.FindFixture` / `FindProfile` are O(1) through a self-validating index (`ListLookup`: the
  lists stay public and freely edited; a stale index falls back to the old linear scan and rebuilds). `PatchValidator`
  finds overlaps through a 512-slot occupancy table per universe (O(fixtures + channels + overlaps), same issues in the
  same order as the old all-pairs check). The Add Fixture dialog plans against a `UniverseOccupancy` snapshot built once
  per universe. The Fixtures page keeps rows of unchanged fixtures across edits (adding lights inserts rows only) and a
  single-field edit updates the Groups tree in place; the Virtual Console rebuilds its target lists only when
  `ProjectService.Revision` changed.
- Project load uses `Utf8JsonReader`, save uses `SerializeToUtf8Bytes`. `TimelineBeatMarks` draws beat lines in one pass.
- Test page tooltips are built on open (`LazyToolTip` / `IToolTipSource`); a hidden DMX monitor is not updated.
- Measured: engine tick allocates 0 bytes, about 0.3-1.3 ms at 512 fixtures / 16 universes / 5 shows; `RefreshPatch` about
  105 ms / 4.4 MB (was 233 ms / 39 MB).

## 10. Undo / redo (2026-10-23)

- **Core** (`src/DmxPro.Core/History/`): `UndoHistory<T>` (linear states S0…Sn, max 100 steps, redo dropped on a new
  step, saved-state marker), `ProjectSnapshot` (the project as compact JSON **per item** — profiles, fixtures, groups,
  functions, shows, console pages, stage — with the project file's serializer; unchanged items share the previous
  snapshot's byte arrays, so a step costs only what changed), `ProjectChangeDescriber` (names a step from the diff:
  "Add fixture "Par 1"", "Delete 3 functions", "Edit stage"; no caller has to describe its edit) and `ProjectHistory`
  (the orchestration, unit-tested without WPF).
- **Coalescing:** `ProjectService.MarkChanged` only reports the edit (`NoteChange`); the snapshot is taken on the UI thread
  once edits pause for 500 ms, so a dragged fader or a multi-step command is one step and dragging never serializes the
  project per pixel. Undo, Redo and Save capture a pending edit first. Changes made without `MarkChanged` (music paths
  relinked by a save, the Stage page's automatic first placement) are folded into the current state, not a step.
- **Restore in place:** `ProjectSnapshot.RestoreInto` reads every changed item first, then puts the content into the
  same `Project` object and the same lists; items whose JSON did not change stay the same objects (open editors, rows and
  console pages of untouched items keep working, an editor's unsaved draft included). An automatic, unarranged stage
  placement survives undoing to an empty stage.
- **App:** `ProjectService.TryUndo` / `TryRedo` raise `ContentChanged` (Playback) and then `ContentRestored` — never
  `ProjectChanged`, so it is **not** a project open: no `LoadProject`, no `EngineRuntimeService.ProjectLoaded`, output
  enable state, live mode, Grand Master and Blackout untouched. The engine gets the usual coalesced `RefreshPatch`:
  running functions and shows that still exist keep running with the restored content, ones that no longer exist stop
  at once (`EngineConsolePlayback.SyncWithModel` updates the console). Pages: Fixtures and Virtual Console rebuild
  (the console keeps the shown page, edit mode and Shows tab; held flashes end because their buttons are rebuilt),
  Functions reloads and reopens an editor whose function changed (dropping its draft, no prompt) or closes it when the
  function is gone; Shows handles `ContentRestored` the same way for its show-copy editor; the others follow
  `Revision` / `PatchRefreshed` as before. Edits made by pages while they rebuild after an undo do not wipe the redo
  steps. Dirty flag = not at the saved state, so undoing back to the last save shows the project as saved; New / Open
  clear the history, Save keeps it.
- **Shortcuts:** Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z are window input bindings (main window and live stage window) wrapped
  in `Controls/OutsideTextInputCommand`, which is disabled while a text box has focus, so text boxes keep their own undo.
- **Not undoable (not project edits):** Test page values, live mode, output enable, Blackout / Grand Master, playback,
  selection, window layout, app settings, editor drafts (editors have their own Revert).
- **Measured** (Release, 512 fixtures + 100 full-rig scenes = 16 MB of JSON): first snapshot 58 ms, snapshot after one
  scene edit 47 ms (161 KB new memory), undo incl. capture 53 ms. Small projects: about 1 ms.

## 11. Live reload and auto-save (2026-10-28)

`src/DmxPro.App/Services/ProjectFileSyncService.cs`, started in `App.OnStartup` on the UI thread (file events and
timers are posted back to it; reads are async, parsing runs on the thread pool).

- **Disk fingerprint:** `ProjectSerializer.ComputeContentHash` (SHA-256 of the file bytes). `SaveAsync` returns the hash
  of what it wrote, `LoadAsync` reports the hash of what it read (`ProjectLoadResult.ContentHash`), and
  `ProjectService.DiskContentHash` holds what the file is known to contain (open, save, reload, or a disk version the
  user chose to "keep mine" over). A file whose hash equals it is ignored: that is how the app's own saves and
  auto-saves are told apart from other programs.
- **Watcher:** a `FileSystemWatcher` on the project's folder, filtered to the file name; Changed, Created and Renamed
  (tools that write a temp file and rename it over the project) and Error (buffer overflow) all trigger a check. Re-armed
  when the path changes (open, New, Save as); none for an untitled project or when the setting is off. A folder that
  cannot be watched shows a warning.
- **Check:** 500 ms after the last event (debounce, `TimeProvider`), read the file (retries while the writer holds it
  locked), hash, parse with the normal `ProjectSerializer` (migrations run). A parse failure is retried (half-written
  file); if it still fails, a warning stays in the info bar ("Project file changed on disk but could not be read: ...")
  and the project is kept; the same broken content is not reported twice. While a modal dialog is open or New / Open is
  running, the check waits. If the project or its file changed meanwhile (a save, another open), the check starts over.
- **Apply in place, not a project open:** `ProjectService.TryApplyExternalContent` -> `ProjectHistory.ApplyExternal`
  (captures a pending edit as its own step, then `ProjectSnapshot.RestoreInto`, the same restore as Undo, and commits
  one step "Reloaded from disk", marked saved). It raises `ContentChanged` (Playback) + `ContentRestored`, never
  `ProjectChanged`: output enable state, live mode, Grand Master, Blackout, running functions/shows and the Virtual
  Console stay; the engine gets the usual refresh, so a function deleted in the file stops. Pages and editors react as on
  undo. Afterwards the project is clean and `DiskContentHash` = the file. Ctrl+Z goes back to the content before the reload.
- **Conflict:** with unsaved changes, or an editor with an unsaved draft (`IUnsavedDraftSource`: Functions and Shows
  pages), `IDialogService.AskReloadExternalChangeAsync` asks: **Reload** (apply; the unsaved edits are the undo step
  before it) or **Keep mine** (remember the disk hash so the same content does not ask again; the next save overwrites).
  Closing the prompt = Keep mine.
- **Auto-save:** a one-shot `TimeProvider` timer every *interval*; saves through `ProjectService.SaveAsync` only when
  auto-save is on, the project has a path and unsaved changes. Postponed (retried after 30 s) while a modal dialog is
  open (`IDialogService.IsModalOpen`: its own prompts plus `ComponentDispatcher.IsThreadModal`), while another file
  action runs, or while `ShellViewModel` suspends it (New / Open / close prompts, so "Don't save" is never followed by an
  auto-save; closing stops it for good). Before writing it reads the file: if it differs from `DiskContentHash` (a change
  not applied yet) it does not overwrite it; with live reload on the reload flow runs instead, with it off a warning
  says so. Editor drafts are not part of the project, so they are not auto-saved. Success shows "Auto-saved 14:05"
  (auto-hides); failures are errors in the info bar (rule 10).
- **Live protection (owner decision 2026-10-28):** while Go Live is on, the file is applied only in part.
  - *What is protected* — `PlayingContentService` (App) unions what the console commanded
    (`EngineConsolePlayback.CollectPlaying`: functions on / at a level / flashed, running or paused shows, "a flash is
    held") with the latest `EngineSnapshot` (`ActiveFunctions` incl. ones fading out, `Shows`), and
    `ProtectedContent.FromPlaying` (Core.History) turns it into: **frozen** items (keep their in-memory version) = every
    playing function with its referenced functions and owned step functions (recursively), every playing show with its
    items' functions and show-owned copies, the fixtures those functions drive (patch) and the fixtures' profiles;
    **frozen widgets** = console function/show widgets whose function/show plays (the whole console while a flash is held:
    any page change rebuilds the console and would end the flash); **page dependencies** = what a page refers to, kept
    only if the file deleted it while the page itself has to stay whole. Groups do not affect output yet (group levels are
    UI-only), the stage layout is view state: neither is frozen.
  - *Partial apply* — `ExternalChangeMerge.PlanReload(current, disk, protection)` builds the snapshot to restore per item
    (the `ProjectSnapshot` sections keyed by id): the file's version of everything, except frozen items (in-memory
    version, also when the file deleted them), frozen widgets (kept inside the file's version of their page; a page with
    playing widgets that the file deleted is kept whole) and dependencies of pages kept whole that the file deleted. Items
    whose file version is held back are `Deferred` (`ProjectItemKey`: kind + id, widgets also name their page). It is
    applied with `ProjectService.TryApplyExternalContent(buildTarget, …)` → `ProjectHistory.ApplyExternal(project,
    buildTarget, …)`, the same in-place restore as Undo, one undo step "Reloaded from disk"; the project counts as saved
    and `DiskContentHash` = the file (the file already holds the waiting versions, so they do not make it dirty).
  - *Waiting changes* — the service keeps the file snapshot, its hash, the applied target and the deferred keys, and
    shows an informational note. On Go Live changes, console state changes (`PlayingContentService.Changed`, posted to
    the UI thread) and every `PendingRecheckInterval` (1 s; catches fade-outs and shows ending in the engine), it
    recomputes the protection; if `ExternalChangeMerge.HasReleasable` (cheap, no capture), `PlanRelease` gives the
    released items their file version (everything else, and in-app edits since, stays) as another undo step (saved
    state kept only if the project was clean). A waiting item edited in the app since the apply keeps the app's version
    and is dropped (`Discarded`, warning). Repeats until nothing waits. A newer file change runs a normal reload from
    the newest content and replaces the waiting set; Keep mine, Save / Save As (the disk hash changes) and a project
    open clear it.
  - *Saving* — auto-save is postponed while changes wait (it would overwrite them with the playing versions);
    `ConfirmSaveOverPendingAsync` (called by `ShellViewModel` before every save, the close prompt included) asks
    "Save now will overwrite the newer file versions of N playing items" (Save / Cancel) when saving to the watched file.
  - *Threading* — the protection is decided on the UI thread right before the apply, from the console state (immediate)
    and the engine snapshot (up to one tick old). Something that starts playing in between (same dispatcher turn) can
    still be changed; accepted (the console commands come from the same UI thread, so in practice only engine-side
    starts could race).
- **Tests:** `tests/DmxPro.App.Tests/ProjectFileSyncServiceTests.cs` (fake watcher + manual clock + temp files, and one
  real `FileSystemWatcher` rename test), `tests/DmxPro.App.Tests/ProjectFileSyncLiveTests.cs` (live protection),
  `tests/DmxPro.Core.Tests/ProjectHistoryApplyExternalTests.cs`, `tests/DmxPro.Core.Tests/ExternalChangeMergeTests.cs`.

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
4. Apply the **preview layer** (one function being checked from the Functions page / an editor with "Live on lights";
   it wins on the channels it touches and is removed on stop, editor close, page change or Release All —
   `ui-plan.md` §3.3), then manual overrides (Test tab / Simple desk).
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
- **Function programs:** `FunctionCompiler` turns each Core function (scene, chaser, sequence, color cycle, fade, pulse,
  flash, strobe) into a program (`SceneProgram`, `StepProgram`, `ColorCycleProgram`, `FadeProgram`, `PulseProgram`,
  `FlashProgram`, `StrobeProgram`) when a project is loaded or refreshed. Effects write color via `EffectFixture`
  (profile-resolved; the Dimmer channel carries the level when the fixture has one).
- **Speed:** `SetFunctionSpeed(id, layer, factor)` scales a running function's own time (0..8, a new start resets it
  to 1); fades on start/stop stay real time. The console Speed widget feeds it (per-function speed × global speed).
- **Strobe enforcement (safety rule 8):** `AllowStrobe` (off = every Strobe channel forced to its safe value) and
  `MaxStrobeRateHz` (caps software strobe rate × speed; 0 = steady light). Snapshots carry a `[Flags] StrobeLimit`
  (`RateLimited`, `Suppressed`) so the UI can show it. Hardware strobe channels cannot be rate-limited (profiles have no
  value → Hz calibration).
- **Safe frame:** `CreateSafeFrame(universe, SafeFrameMode)` — profile defaults (strobe at its safe value), or
  blackout (intensity 0). Used on close.
- **Core functions** (`src/DmxPro.Core/Functions`): `LightFunction` is polymorphic on `"type"` (Scene, Chaser, Sequence,
  ColorCycle, Fade, Pulse, Flash, Strobe); `FunctionFactory` creates them, `FunctionReferences` validates references
  (`CHASER_MISSING_FUNCTION`, `FADE_MISSING_SCENE`, `FUNCTION_MISSING_FIXTURE`, `SEQUENCE_UNKNOWN_CHANNEL`,
  `FUNCTION_REFERENCE_LOOP`). `RgbColor` is saved as `"#RRGGBB"`.

## 5. Project file

- Extension **`.dmxpro`** — JSON (`schemaVersion`), optionally zipped later to embed audio/profile copies.
- Contains: profiles used (embedded copies, so projects are portable), fixtures, groups, scenes, functions,
  shows (timelines), console pages/widgets. Output configs and settings are currently **app-level**
  (`settings.json`, see §6), not in the project; they never contain an enabled flag.
- Stable GUID ids for all entities; references by id.
- Migrations: `IProjectMigrator` per schema version.
- Autosave to `%LOCALAPPDATA%/DmxPro/autosave/` every 2 min + on crash; recovery prompt on start.

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
  seek step, DMX refresh rate, autosave interval, default project folder, extra profile folders, sACN CID, and the
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

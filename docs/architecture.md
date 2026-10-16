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
2. Each running function writes its contribution (with fade progress and function intensity).
3. Merge: HTP for intensity types, LTP for others (latest start time wins).
4. Apply manual overrides (Test tab / Simple desk).
5. Color policy (RGB→RGBW), software dimming where no Dimmer channel.
6. Grand Master (intensity only) → Blackout.
7. Clamp, write per-universe `byte[512]`, publish snapshot, hand to outputs.

## 5. Project file

- Extension **`.dmxpro`** — JSON (`schemaVersion`), optionally zipped later to embed audio/profile copies.
- Contains: profiles used (embedded copies, so projects are portable), fixtures, groups, scenes, functions,
  shows (timelines), console pages/widgets, output config (**without** the enabled flag), settings.
- Stable GUID ids for all entities; references by id.
- Migrations: `IProjectMigrator` per schema version.
- Autosave to `%LOCALAPPDATA%/DmxPro/autosave/` every 2 min + on crash; recovery prompt on start.

## 6. UI navigation

Left `NavigationView` (WPF-UI): **Fixtures · Functions · Scenes · Virtual Console · Music Show · Test & Preview ·
Input/Output · Settings**. Top bar: project name, Save, Undo/Redo, command palette.
Bottom status bar: **Output pill** (Disabled / Connected / Sending 44 fps / Error), Grand Master slider, **Blackout**,
**Release All**, BPM.

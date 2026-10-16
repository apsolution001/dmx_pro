# DMX Pro — HTML UI Prototype

A clickable, working mockup of the **whole app** (`index.html` + `styles.css` + `app.js`, no dependencies).

> **Behavior reference only.** It shows what each screen does. The real app's *look* is a native Windows desktop
> app laid out like QLC+ (toolbars, vertical tab strip, split panels, tables/trees) — see
> `docs/standards/ui-ux-guidelines.md`. Where the two differ, the guidelines win.

**Open it:** double-click `index.html` (any modern browser). Best at 1440×900 or larger.

## What works in the prototype

| Area | Try this |
|------|----------|
| Menu bar | All 9 menus open; items run their action (or show what they will do) |
| Navigation | Left rail or **Ctrl+1 … Ctrl+9**; **Ctrl+K** command palette |
| Fixtures | Add fixture (auto free address, multiple at once), select row, edit profile/mode/universe/address, overlap warning + "Fix", address map (click a square), groups |
| Fixture Library | Browse profiles, switch DMX mode, ✓/⚠ verified badge, **New/Edit profile** editor, import dialog |
| Functions | Chaser steps (edit, reorder, add, remove), color cycle, pulse, flash, strobe, fade editors; **Preview** runs it on the stage |
| Scenes | Color wheel + swatches, only the channels the profiles have, RGBW white policy, brightness, targets/groups, live stage preview |
| Virtual Console | Toggle buttons (color buttons are solo), flash (hold), master & speed sliders, tap tempo, pages, **Edit layout** (drag with 16 px snap, properties), operator lock |
| Show Timeline | Play/stop (Space/Home), waveform + beat markers, drag items, drag functions from the library onto tracks, **drag a range → "New function for selection"** |
| Test & Preview | R/G/B/W-only tests at safe intensity, color sequence, raw channel faders named from the profile, live DMX monitor with footprint highlight |
| Input / Output | Per-universe output type (Art-Net, sACN, Enttec Pro, Open DMX), settings, node discovery, **output switch disabled until a valid config** |
| Status bar | Output pill, fps, Grand Master, **Blackout (B)**, **Release all (Esc)**, BPM |
| Theme | View ▸ Dark / Light theme, or Settings |

The data and "engine" in `app.js` are simulated, but mirror `DmxPro.Core` (profiles → modes → channels; fixtures;
groups; HTP intensity scaling by master; blackout ≠ output disabled).

## How it maps to WPF (implementation guide)

| Prototype | WPF / WPF-UI |
|-----------|--------------|
| `.window` grid rows (title 32 · menu 32 · body · status 40) | `MainWindow` Grid rows |
| `.titlebar` | `ui:TitleBar` |
| `.menubar` / `MENUS` in app.js | `Menu` in MainWindow (same items, shortcuts, order) |
| `.nav` / `PAGES` | navigation `ListBox` bound to `ShellViewModel.NavigationItems` |
| `.page-header` + `#pageActions` (`PAGE_ACTIONS`) | page title/subtitle + per-page command buttons |
| `.view#view-x` | one `UserControl` per page (`Views/XView.xaml`) + `XViewModel` |
| `.card` | `ui:Card` |
| `.btn`, `.btn.primary`, `.btn.subtle` | `ui:Button` Appearance = Secondary / Primary / Transparent |
| `.input`, `select.input` | `ui:TextBox`, `ComboBox`, `ui:NumberBox` |
| `.toggle` | `ui:ToggleSwitch` |
| `.infobar` (success / warning / error) | `ui:InfoBar` Severity |
| `.badge` | `Border` with the `Badge` style in App.xaml |
| `.table` | `DataGrid` (read-only rows) or `ListView` + `GridView` |
| `.list-item` | `ListView` item template |
| dialog (`#dialogHost`) | `ui:ContentDialog` / `ui:MessageBox` |
| toast | `ui:Snackbar` |
| command palette | `ui:AutoSuggestBox` in a popup |
| `<i data-symbol="Save24">` | `<ui:SymbolIcon Symbol="Save24" />` (all names verified to exist in WPF-UI 3.0.5) |
| CSS variable `--TextFillColorPrimary` | `{DynamicResource TextFillColorPrimaryBrush}` (same name + `Brush`) |
| stage preview `.lamp` | `ItemsControl` of ellipses with a `DropShadowEffect` colored from the engine snapshot |
| color wheel canvas | custom `ColorWheel` control (HSV, saturation = radius) |
| timeline lanes | custom `TimelineCanvas` control (virtualized, pixels-per-second zoom) |
| DMX monitor `.monitor` | `ItemsControl` with `UniformGrid` / `VirtualizingWrapPanel` of 512 cells |

**Sizes:** 4 px spacing grid · radius 4 (controls) / 8 (cards) · nav rail 232 px · type 12 / 14 / 20 / 28 px ·
Segoe UI Variable. Console widgets snap to 16 px.

## Rules

- Change the prototype first when a screen's design changes, then implement it in WPF.
- Keep `MENUS`, `PAGES` and `PAGE_ACTIONS` in sync with `docs/ui-plan.md`.
- Keep icon keys valid WPF-UI `SymbolRegular` names.

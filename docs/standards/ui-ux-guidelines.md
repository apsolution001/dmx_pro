# UI / UX Guidelines (WPF desktop, QLC+ layout)

## Visual style — native Windows desktop app, laid out like QLC+ (decided 2026-10-16)

The owner wants a **proper Windows application**, not a web-page look. Structure every screen like QLC+:

- **Window:** title bar → menu bar → main icon toolbar → body → status bar. Solid background (no Mica/glass).
- **Main sections:** vertical **tab strip on the left** (icon above a short label), one section visible at a time.
- **Each page:** its own **toolbar strip** at the top (primary action as a labelled button, the rest icon buttons
  with tooltips, groups split by separators), then **docked panels** separated by **GridSplitters** the user can drag.
- **Panels:** thin border + a header row with the panel title (styles `PanelBorder`, `PanelHeader`, `PanelTitle`).
- **Data:** tables (`DataGrid`), trees (`TreeView`, e.g. manufacturer → model) and property forms
  (label column + control column, `FormLabel`) — not cards, hero headers or big page titles.
- **Dialogs:** real modal windows for multi-field tasks (e.g. *Add fixture*), `MessageBox` for confirmations.
- Shared styles live in `src/DmxPro.App/App.xaml`. The HTML prototype in `docs/prototype/` is a **behavior**
  reference (what each screen does); its web-style visuals are superseded by these rules.

### Theme details

- **WPF-UI** controls on a solid background (no Mica), **dark theme default** (operators work in dark venues),
  light theme switchable. Accent color from Windows or user choice.
- **Compact density** (owner decision 2026-10-18: "small but clearly visible, smooth"). All sizes are tokens in
  `src/DmxPro.App/Themes/Metrics.xaml`; **new views must use the tokens, never literal sizes**.
- Spacing grid **2 px** (mostly); spacing tokens 2 / 4 / 6 / 10 (`Dmx.Space.XS/S/M/L`). Corner radius 4 on controls; panels are square-edged with 1 px borders.
- Typography: Segoe UI Variable; sizes 10.5 (caption, the minimum), 11 (small), 12 (body), 13 (heading), 14 (title).
- Heights: control 24, toolbar 30 (toolbar button 24), tab 24, table row 22 / header 22, list row 20, check/radio row 20
  (16 px glyph), menu item 22, status bar 20, title bar 28, nav rail 62 wide, splitter 4, channel fader min 150. Icons 14
  (dense rows 12); `DmxIcon.MaxSize` caps icon size. Virtual Console transport bar 34 px (`ConsoleShowTransportBar`);
  Shows track height 28 (`ShowsViewModel.TrackHeight`); `VirtualConsoleViewModel.MinCellSize` is 40.
- Compact `CheckBox`, `RadioButton` and `ui:InfoBar` templates in `App.xaml` replace the WPF-UI defaults (second compact
  pass, owner 2026-10-19: "still bulky, shorter heights, smaller font").
- **Density rules:** no panel header that only repeats the page name or the obvious (e.g. "Functions" on the Functions
  list, "Properties" on a form); one line per light/row; long hints go into tooltips. Safety warnings stay visible.
- Motion (`Controls/DmxMotion.cs`): animate only when `DmxMotion.IsEnabled` (Windows animation effects on and hardware
  rendering), otherwise 0 ms. Durations `Fast` 100 ms (hover) and `Normal` 150 ms, easing `EaseOut`. Page switch = fade +
  8 px rise, 150 ms; the old page collapses instantly (Virtual Console flash release on leave relies on it). InfoBar open =
  fade + 4 px drop; the nav-rail indicator grows. **Never animate** lists, faders or live values.
  Context menus use the compact `Dmx.MenuItem` style.
- Logo: `Assets/Logo/` (`dmxpro-logo.svg`, `-small.svg`, `-256.png`, `dmxpro.ico`), images `Dmx.Logo.Image` and
  `Dmx.Logo.Small.Image` in `Themes/Logo.xaml`; used for the exe, window/taskbar icons and the title bars of the main, Add
  fixture and Channel layout windows. Concept: dark rounded tile, light-grey spot, amber beam (#FFB020), three DMX-pin dots.
- Stage floor exception: the Virtual Stage floor is always dark (#17191D) in both themes, like a real dark stage.
- Icon `stage.svg` is an original drawing (see `Assets/Icons/README.md`).
- Icons: Fluent System Icons (`SymbolRegular`) via WPF-UI. Never mix icon sets.
- Colors and brushes **only from theme resources** (`{DynamicResource ...}`) — no hardcoded hex in views,
  except actual light colors (scene/fixture colors).

## Layout patterns

- Vertical tab strip with the main sections; each page = toolbar strip + split panels.
- List/detail pages: list or tree on the left (search in the toolbar), properties panel on the right.
- Use dialogs (`ContentDialog`) only for confirmation of destructive actions and wizards.
- Status bar always visible: Output state, Grand Master, Blackout, Release All, BPM.

## Interaction rules

- Every destructive action: confirmation **or** undo (prefer undo + toast "Deleted · Undo").
- Every long action (> 300 ms): progress indicator; cancellable if > 2 s (e.g., audio analysis).
- Drag & drop: scenes/functions from library into chasers, timelines, console.
- Right-click context menus mirror toolbar actions (Copy, Paste, Duplicate, Rename, Delete).
- Keyboard: Ctrl+S save, Ctrl+Z/Y undo/redo, Ctrl+C/V/D copy/paste/duplicate, Ctrl+K command palette, F2 rename,
  Del delete, **B** blackout, **Esc** release all (in console operate mode), **Space** Go (cue list).
- Tooltips on all icon-only buttons. Empty states explain what to do next ("No fixtures yet — Add fixture").

## Live operation (Virtual Console)

- Big targets (min 64×64 px), high contrast, clear active state (glow/border + color).
- Operator lock mode hides editing chrome.
- Flash buttons release on mouse-up, touch-up, focus loss, and window deactivation.

## Safety in the UI

- Output state pill colors: Disabled = gray, Connected/idle = blue, Sending = green (with fps), Error = red.
- Channels with an unknown meaning (`Generic`) show a neutral info mark ("Meaning unknown: raw channel only"), never a
  confirmed meaning. No "unverified profile" warnings (decision 2026-10-22).
- Strobe controls show a photosensitivity warning the first time.

## Accessibility

- All controls reachable by keyboard; `AutomationProperties.Name` on icon buttons.
- Don't rely on color alone for state (add text/icon).
- Respect Windows text scaling.

## MVVM rules

- Views have no logic in code-behind except pure view concerns (focus, drag adorners, animations).
- ViewModels use CommunityToolkit.Mvvm (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`).
- ViewModels don't reference WPF types (`Brush`, `Visibility`) — use converters.
- Navigation via a `INavigationService`; dialogs via `IDialogService` (testable).

## XAML resource rules (2026-10-19)

- Implicit styles in `App.xaml` must **not** use `BasedOn="{StaticResource {x:Type T}}"` — it resolves to null there.
  `App.ChainImplicitStylesToWpfUi` rebuilds them on WPF-UI's base style at startup.
- Do not put `<StaticResource x:Key=...>` items in `App.xaml` resources; they drop neighbouring keys.

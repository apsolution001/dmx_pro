# UI / UX Guidelines (WPF, Fluent, modern)

## Visual style

- **WPF-UI** Fluent controls, Mica backdrop on Windows 11, **dark theme default** (operators work in dark venues),
  light theme switchable. Accent color from Windows or user choice.
- Spacing grid **4 px**; standard paddings 8/12/16/24. Corner radius 4 (controls) / 8 (cards).
- Typography: Segoe UI Variable; sizes 12 (caption), 14 (body), 20 (subtitle), 28 (title).
- Icons: Fluent System Icons (`SymbolRegular`) via WPF-UI. Never mix icon sets.
- Colors and brushes **only from theme resources** (`{DynamicResource ...}`) — no hardcoded hex in views,
  except actual light colors (scene/fixture colors).

## Layout patterns

- Left NavigationView with the main tabs; each page = header (title + primary actions) + content.
- List/detail pages: list on the left (search on top), editor on the right. No modal dialogs for everyday editing.
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
- Unverified profile channels show ⚠️ badge.
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

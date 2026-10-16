# DMX Pro — Product Requirements (beyond the PDF spec)

> The PDF spec (`feature-specification.md`) is the baseline. This file records **additional requirements from the
> product owner** and decisions taken since. Update it whenever a question in `open-questions.md` is answered.

## Platform

- **WPF, C#, .NET 8**, Windows 10/11 x64.
- **Modern UI:** Fluent design (Windows 11 look), dark theme by default, light theme optional, smooth animations,
  large touch-friendly controls on the Virtual Console. Must be easy for a non-expert.

## Additional features requested

| # | Feature                       | Notes                                                                                   |
|---|-------------------------------|-----------------------------------------------------------------------------------------|
| R1| **Create functions**          | All function types from spec §4, with a friendly editor                                 |
| R2| **Virtual Console**           | Spec §6, drag-and-drop editor, pages, operator lock                                     |
| R3| **Scenes**                    | Spec §5 Scene Creator                                                                   |
| R4| **Music upload → light show** | Upload audio, analyze beats/energy, auto-generate an editable show timeline (see `knowledge/06-music-to-light.md`) |
| R5| **Save / Load files**         | Project save, Save As, Open, recent files, autosave + crash recovery                    |
| R6| **Copy functions**            | Duplicate / copy-paste functions, scenes, widgets (also between projects via clipboard) |

## Design goals ("perfect, easy UI")

1. **Fewer clicks than QLC+**: e.g., "Save scene → Add to console" in one action.
2. **Guided first-run**: wizard to add first fixture, test it, create starter scenes, create the default console page.
3. **Always-visible status bar**: Output state pill, Grand Master, Blackout, BPM, CPU/fps.
4. **Undo/redo** for all editing.
5. **Keyboard shortcuts** (Ctrl+S, Ctrl+Z, Space = Go, Esc = Release all, B = Blackout).
6. **Search everywhere** (Ctrl+K command palette).
7. **Live on-screen preview** of fixtures (stage view with colored circles).

## Decisions log

| Date       | Decision                                              | Reason                          |
|------------|-------------------------------------------------------|---------------------------------|
| 2026-10-16 | Project created; docs + AI setup files added          | Kick-off                        |

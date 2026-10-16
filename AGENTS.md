# AGENTS.md — Instructions for AI coding agents

> Read by Claude Code (via `CLAUDE.md`), OpenAI Codex, Cursor, GitHub Copilot, Gemini and other agents.
> Keep this file short; details live in `docs/`.

## Project

**DMX Pro** — a modern Windows DMX lighting controller (like QLC+, but easier) for RGB/RGBW fixtures.
**Stack:** WPF, C#, .NET 8, WPF-UI (Fluent), CommunityToolkit.Mvvm, Microsoft.Extensions.Hosting/DI, System.Text.Json, NAudio, xUnit.

Main features: Fixtures (profile-driven patch) · Functions (scene, chaser, sequence, color cycle, fade, pulse, flash, strobe) ·
Scene Creator · Virtual Console · Music Show (upload audio → auto light show) · Test & Preview · Input/Output (Art-Net, sACN, USB-DMX) ·
Save/Load projects · Copy/paste functions.

## Read before working

| When you touch…              | Read first                                                     |
|------------------------------|----------------------------------------------------------------|
| Anything                     | `docs/spec/feature-specification.md`, `docs/spec/product-requirements.md`, `docs/standards/safety-rules.md` |
| Architecture / new project   | `docs/architecture.md`                                         |
| C# code                      | `docs/standards/coding-standards.md`                           |
| XAML / UI                    | `docs/standards/ui-ux-guidelines.md`                           |
| Profiles / channels          | `docs/knowledge/05-fixture-profiles.md`, `docs/knowledge/01-dmx512-fundamentals.md` |
| Output (network / USB)       | `docs/knowledge/03-network-dmx-artnet-sacn.md`, `docs/knowledge/04-usb-dmx-interfaces.md` |
| Music / audio                | `docs/knowledge/06-music-to-light.md`                          |
| QLC+ comparisons             | `docs/knowledge/02-qlc-plus-overview.md`                       |

## Golden rules

1. **Profile-driven:** never hardcode which channel is Red/Dimmer/Strobe. Resolve from the fixture profile + mode.
2. **Safety:** output disabled at start, never persisted as enabled; Blackout ≠ Output disabled; deterministic release. See `docs/standards/safety-rules.md`.
3. **Layering:** `Core`, `Engine`, `Output` have **no WPF references**. UI talks to the engine through services/commands.
4. **MVVM:** no business logic in code-behind. CommunityToolkit.Mvvm source generators.
5. **Hot path:** engine tick and output send allocate nothing and never block on the UI.
6. **Addresses 1–512 / universes from 1** in domain and UI; protocol-specific numbering only in Output.
7. **Tests:** add/adjust xUnit tests for every engine, profile, output, or analysis change. Protocol tests check exact bytes.
8. **Docs stay true:** update `docs/` in the same change when behavior or architecture changes. Record product decisions
   in `docs/spec/product-requirements.md`.
9. **Ask, don't guess** about product behavior that the spec doesn't define — add it to `docs/open-questions.md`.
10. Don't copy code from QLC+ or other projects; learning concepts is fine. Imported data (e.g., `.qxf`) needs attribution.

## Commands

```bash
dotnet restore
dotnet build DmxPro.sln -c Debug          # on Linux/macOS CI: add -p:EnableWindowsTargeting=true
dotnet test  DmxPro.sln
dotnet format DmxPro.sln --verify-no-changes
dotnet run --project src/DmxPro.App       # Windows only
```

## Definition of done

- Builds with zero warnings; tests pass; `dotnet format` clean.
- UI change: follows the UI guidelines, works in dark and light theme, keyboard accessible.
- Safety rules respected; docs updated; Conventional Commit message (`docs/standards/git-workflow.md`).

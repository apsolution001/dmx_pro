# DMX Pro

A modern, easy-to-use **Windows DMX lighting controller** for RGB/RGBW fixtures — inspired by QLC+, built with
**WPF · C# · .NET 8** and a Fluent (Windows 11) UI.

## Planned features

- **Fixtures** — profile-driven patching, address calculator, overlap warnings, groups
- **Functions** — scenes, chasers, sequences, color cycles, fades, pulses, flash, strobe; copy/paste
- **Scene Creator** — color picker, RGBW handling, fades, live stage preview
- **Virtual Console** — pages of buttons/sliders, drag & drop layout, operator lock
- **Music Show** — upload a song, automatic beat/energy analysis, auto-generated editable light show
- **Test & Preview** — raw channel tests, DMX monitor, diagnostics
- **Input / Output** — Art-Net, sACN (E1.31), Enttec USB-DMX
- **Projects** — save/load, autosave, crash recovery

## Documentation

| Doc | Content |
|-----|---------|
| [Feature specification](docs/spec/feature-specification.md) | Baseline requirements |
| [Product requirements](docs/spec/product-requirements.md) | Extra requirements & decisions |
| [Architecture](docs/architecture.md) | Solution layout, engine, threads, file format |
| [Roadmap](docs/roadmap.md) | Phases and status |
| [Open questions](docs/open-questions.md) | Decisions still needed |
| [Knowledge base](docs/knowledge/) | DMX512, QLC+, Art-Net/sACN, USB-DMX, profiles, music-to-light |
| [Standards](docs/standards/) | Coding, UI/UX, safety, git |

AI agents: start with [`AGENTS.md`](AGENTS.md) (Claude Code reads [`CLAUDE.md`](CLAUDE.md)).

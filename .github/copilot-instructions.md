# GitHub Copilot instructions

Follow `AGENTS.md` at the repository root. Key points:

- WPF / C# / .NET 8, MVVM with CommunityToolkit.Mvvm, Fluent UI with WPF-UI.
- Fixture channels are **profile-driven** — never hardcode Red/Dimmer/Strobe offsets.
- Safety rules in `docs/standards/safety-rules.md` are mandatory (output disabled by default, blackout ≠ disable).
- `Core`, `Engine`, `Output` projects must not reference WPF.
- Coding standards: `docs/standards/coding-standards.md`. UI rules: `docs/standards/ui-ux-guidelines.md`.

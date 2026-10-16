# CLAUDE.md

All shared agent instructions live in `AGENTS.md` — follow it.

@AGENTS.md
@docs/standards/safety-rules.md
@docs/standards/coding-standards.md

## Claude-specific notes

- Before implementing a feature, check `docs/roadmap.md` for the current phase and `docs/open-questions.md` for unresolved
  decisions. If a needed decision is open, ask the user instead of guessing.
- For UI work, also read `docs/standards/ui-ux-guidelines.md`.
- Subagents in `.claude/agents/`: `dmx-domain-expert` (protocol/profile questions and reviews),
  `wpf-ui-reviewer` (XAML/MVVM/UX review), `safety-reviewer` (checks safety rules on a diff).
- Slash commands in `.claude/commands/`: `/new-feature`, `/review-safety`, `/add-profile`.
- This repo is developed partly in Linux cloud containers: the WPF app can neither build nor run there. Use
  `DmxPro.CrossPlatform.slnf` locally, keep logic out of the App project so it is unit-testable, and after pushing
  check the Windows CI run (GitHub Actions) for XAML/WPF compile errors.

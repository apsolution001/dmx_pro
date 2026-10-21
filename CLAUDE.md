# CLAUDE.md

All shared agent instructions live in `AGENTS.md` — follow it.

@AGENTS.md
@docs/standards/safety-rules.md
@docs/standards/coding-standards.md

## Claude-specific notes

- Before implementing a feature, check `docs/roadmap.md` for the current phase and `docs/open-questions.md` for unresolved
  decisions. If a needed decision is open, ask the user instead of guessing.
- For UI work, also read `docs/standards/ui-ux-guidelines.md`.
- Subagents live in `.claude/agents/` — see **Agent team** below.
- Slash commands in `.claude/commands/`: `/new-feature`, `/review-safety`, `/add-profile`.
- This repo is developed partly in Linux cloud containers: the WPF app can neither build nor run there. Use
  `DmxPro.CrossPlatform.slnf` locally, keep logic out of the App project so it is unit-testable, and after pushing
  check the Windows CI run (GitHub Actions) for XAML/WPF compile errors.

## Agent team (`.claude/agents/`)

| Agent | Role | Edits? |
|-------|------|--------|
| `sr-coder` | Senior dev: complex features, architecture, engine/output/threading, task breakdowns | code + tests |
| `jr-coder` | Junior dev: small, clearly specified tasks (properties, commands, simple VMs, wiring, profiles) | code + tests |
| `tester-unit` | Tester 1: writes/runs xUnit tests, edge cases, exact-byte protocol tests | `tests/` only |
| `tester-qa` | Tester 2: full build/test/format, publish exe, smoke-launch, acceptance checklist vs spec | read-only |
| `ui-designer` | Designs and builds XAML screens/dialogs/styles (QLC+ desktop layout) | `DmxPro.App` views |
| `code-reviewer` | Reviews the diff: correctness, golden rules, tests, style | read-only |
| `bug-finder` | Reproduces bugs with a failing test, finds root cause; fixes only when told | tests (+ fix if asked) |
| `doc-generator` | Updates `*.md` docs: roadmap, architecture, ui-plan, decisions log, README | `*.md` only |
| `dmx-domain-expert` | DMX/Art-Net/sACN/profile questions and protocol review | read-only |
| `wpf-ui-reviewer` | XAML/MVVM/UX review against the UI guidelines | read-only |
| `safety-reviewer` | Checks the diff against the 10 safety rules | read-only |

### How to dispatch (main session = team lead)

- **Use the Agent tool with these `subagent_type`s. Do NOT use the Workflow tool** for this project.
- **The team is a toolbox, not a mandatory pipeline.** The lead decides per task whether to use agents at all,
  which ones, and how many. Use the fewest agents that fit the work:
  - **Do it directly (no agent)** when it's small or quick: a one-file fix, a question, reading a few files,
    a rename, running build/tests.
  - **One agent** for one coherent job, even a big one. Example: "find SVG icons online, convert them, build the
    icon control" is ONE job for ONE `ui-designer` — splitting it into 3 agents would duplicate the downloads and
    fight over the same folder.
  - **Several agents in parallel** only when the work splits into independent parts with separate files.
    Example: icon system ‖ shared styles ‖ new fixture profiles = 3 different areas → 3 agents at once.
  - Don't split work that needs one shared understanding (one screen, one algorithm) across agents.
  - Reviews/testers are optional too: run `code-reviewer` / `tester-qa` / `safety-reviewer` when the change is
    big or risky, not for every small edit.
- **When several agents fit, run them in parallel:** put the Agent calls in **one message**.
- **File ownership:** before a parallel round, give every writing agent an explicit list of files/folders it owns.
  Two writing agents must never own the same file. If that cannot be avoided, run them one after another, or give
  them `isolation: "worktree"` and merge afterwards.
- Every prompt is self-contained: the goal, the files it owns, relevant spec/doc sections, the definition of done,
  and what to report back. Agents do not see this conversation.
- Agents never commit. The main session reviews their reports, runs the final `dotnet build` / `dotnet test` /
  `dotnet format --verify-no-changes`, and commits (Conventional Commits).
- Read-only agents (reviewers, `tester-qa`, `dmx-domain-expert`) can always run in parallel with each other.

### Typical pipeline for a large feature (adapt or skip steps as the task needs)

1. **Plan:** `sr-coder` breaks the feature into tasks with file ownership (in parallel, `dmx-domain-expert` for
   protocol/profile questions). Open product questions go to the user first.
2. **Build (parallel):** `sr-coder` (Core/Engine/Output logic) + `jr-coder` (simple tasks) + `ui-designer` (XAML)
   + `tester-unit` (tests written from the spec), each on its own files.
3. **Verify (parallel):** `code-reviewer` + `tester-qa` (only if the owner asks) + `safety-reviewer` (engine/output/console/project-file
   changes) + `wpf-ui-reviewer` (UI changes).
4. **Fix:** send findings back to the agent that owns the file (`bug-finder` for failures with unclear cause).
5. **Docs:** `doc-generator` updates roadmap/architecture/ui-plan/decisions log; it can run in parallel with step 4
   once the design is settled.
6. Main session does the final build/test/format and commits.

For a bug: `bug-finder` (reproduce + root cause) → owner fixes (or `bug-finder` with "fix") → `tester-unit` +
`code-reviewer` in parallel.

### After every finished task: build the exe

The owner tests every change on the real app. **Priority:** as soon as any feature is done (build + tests green) —
not only at the end of a big batch — publish a fresh exe and tell the owner where it is:

```bash
dotnet publish src/DmxPro.App/DmxPro.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o G:\cipherverse\DmxPro-exe
```

**The only exe folder is `G:\cipherverse\DmxPro-exe`.** Every exe for the owner goes there and nowhere else — never
into the repo, a scratchpad, a worktree folder, Desktop or a new folder. Overwrite the old `DmxPro.exe` in place (one
exe, always the latest). If the folder is missing, recreate it with the command above. Always tell the owner the full
path `G:\cipherverse\DmxPro-exe\DmxPro.exe`. Subagents must not publish exes; only the main session does.

### No automatic app testing — the owner tests

Never launch the app, take screenshots, or send keyboard/mouse input to test it, and never let agents do so
(windows pop up on the owner's screen). Never probe real DMX hardware (network interfaces, COM/FTDI ports).
Allowed: `dotnet build`, `dotnet test` (unit tests, loopback only), `dotnet format`. The owner runs the exe
and reports problems. Don't run `tester-qa` unless the owner asks.

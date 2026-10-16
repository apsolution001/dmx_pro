# Git Workflow

- Branches: `main` (stable), feature branches `feature/<short-name>`, fixes `fix/<short-name>`.
  AI agents work on the branch they are assigned.
- Commits: **Conventional Commits** — `feat(fixtures): add overlap validation`, `fix(output): art-net length byte order`,
  `docs:`, `test:`, `refactor:`, `chore:`.
- One logical change per commit; build + tests pass before every commit.
- Update `docs/` in the same commit when behavior/architecture changes.
- Never commit: `bin/`, `obj/`, `.vs/`, user settings, secrets, large audio files (use small test fixtures).
- PRs: description lists what changed, how it was tested, screenshots for UI changes.

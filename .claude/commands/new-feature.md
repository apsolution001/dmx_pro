---
description: Plan and implement a DMX Pro feature following the project rules
argument-hint: <feature description>
---

Implement this feature: $ARGUMENTS

1. Read `AGENTS.md`, `docs/roadmap.md`, `docs/open-questions.md`, and the spec sections related to the feature.
2. If a product decision is missing, ask the user (and add it to `docs/open-questions.md`) before coding.
3. Write a short plan: affected projects (Core/Engine/Output/Audio/App), new types, tests.
4. Implement Core/Engine logic first with xUnit tests, then the ViewModel, then the XAML view.
5. Build and run tests (`dotnet build -p:EnableWindowsTargeting=true`, `dotnet test`).
6. Run the `safety-reviewer` subagent if engine/output/console/project-file code changed; `wpf-ui-reviewer` for UI.
7. Update docs (`architecture.md`, `roadmap.md` status, `product-requirements.md` decisions) and commit with a Conventional Commit message.

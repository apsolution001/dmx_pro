# Chaser Steps Without Extra Functions — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A chaser's steps are either an own look (lights/colours set inside the chaser) or a copy of a function, both
owned by the chaser and hidden from the Functions list; Sequence is merged into Chaser.

**Architecture:** Generalise the existing show-copy ownership (`LightFunction.OwnerShowId`) with
`LightFunction.OwnerFunctionId`. An own-look step is an owned hidden `SceneFunction`; a copied step is an owned hidden
deep copy. The engine keeps playing chaser steps exactly as today. Old `SequenceFunction`s are converted to chasers on
load. The Chaser editor works on a draft that holds the chaser plus its owned step functions and commits both on Save.

**Tech Stack:** C# / .NET 8, WPF + WPF-UI, CommunityToolkit.Mvvm, System.Text.Json, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-27-chaser-steps-design.md`

## Global Constraints

- CLAUDE.md / AGENTS.md golden rules, `docs/standards/safety-rules.md`, `docs/standards/coding-standards.md`.
- `dotnet build DmxPro.sln` 0 warnings · `dotnet test DmxPro.sln` green · `dotnet format DmxPro.sln --verify-no-changes` clean.
- Core/Engine have no WPF references; engine tick allocates nothing.
- File format is additive: new JSON property `ownerFunctionId`; files saved before 2026-10-27 must load and play the same.
- Function ids never change during migration (VC widgets / show items / fades keep pointing at them).
- A function has at most one owner: `OwnerShowId` **or** `OwnerFunctionId`, never both.
- Functions with `OwnerFunctionId != null` never appear in: Functions list, VC target lists, Shows library, any
  "add function" picker, Fade scene lists.
- Deviation from the spec (lead's call, better than the spec's version): new step functions live in the **editor draft
  until the chaser is saved** (no project changes outside Save). "Edit step" on a function step asks to save the chaser
  first (usual Save / Don't save / Cancel), then opens the copy's editor with "← Back to chaser".

## Review Focus

1. A chaser placed in a show, then the show deleted → no orphaned owned step functions left (Task 1 test).
2. Copy/paste or duplicate a chaser, then edit a step in the copy → the original chaser's step is unchanged (Task 1 test).
3. Old file with a chaser whose step references a library scene → loads, plays, and the first edit of that step makes
   it an owned copy without changing the library scene (Task 1 + Task 3 tests).
4. Old file with a sequence used by a VC button and a show item → after load the VC button and show item still play
   it, frame-identical (Task 2 tests).
5. Undo after saving a chaser that added/removed owned steps → chaser and its owned functions restored together
   (Task 1 test using `ProjectHistory`).

---

### Task 1: Core ownership model and step lifecycle (sr-coder, runs first)

**Files:**
- Modify: `src/DmxPro.Core/Functions/LightFunction.cs` (add `OwnerFunctionId`, `IsHiddenFromList`)
- Modify: `src/DmxPro.Core/Projects/Project.Shows.cs` (`FunctionListItems`, `PlaceFunctionInShow`, `DuplicateShowItem`, `DeleteShow` use owned deep copies / cleanup)
- Modify: `src/DmxPro.Core/Functions/FunctionLibrary.cs` (`DuplicateFunction`, `DeleteFunction` cascade)
- Modify: `src/DmxPro.Core/Functions/FunctionClipboard.cs` (copy/paste include owned functions)
- Modify: `src/DmxPro.Core/Functions/FunctionReferences.cs` (owned steps count as references; validation)
- Modify: `src/DmxPro.Core/Projects/Project.Console.cs:106` (VC targets exclude owned)
- Create: `src/DmxPro.Core/Functions/ChaserSteps.cs`
- Test: `tests/DmxPro.Core.Tests/ChaserStepsTests.cs`, `tests/DmxPro.Core.Tests/OwnedFunctionLifecycleTests.cs`

**Interfaces (Produces):**
- `LightFunction.OwnerFunctionId : Guid?` (JSON `ownerFunctionId`); `bool LightFunction.IsOwnedByFunction => OwnerFunctionId is not null`.
- `Project.FunctionListItems` excludes `OwnerFunctionId != null`.
- `static class ChaserSteps` (namespace `DmxPro.Core.Functions`):
  - `IReadOnlyList<LightFunction> OwnedFunctions(this Project project, Guid ownerId)` — recursive (a copied chaser's own steps included), project order.
  - `SceneFunction NewOwnLookScene(Guid chaserId, string name, IEnumerable<SceneValue> values)` — not added to the project.
  - `IReadOnlyList<LightFunction> CopyForStep(this Project project, Guid chaserId, LightFunction source)` — deep copy of `source` (and, for a chaser source, its owned functions re-pointed) owned by `chaserId`; element 0 is the step's function; nothing added to the project.
  - `IReadOnlyList<LightFunction> CloneWithOwned(this Project project, LightFunction root)` — clone of `root` with new ids plus clones of all its owned functions, steps re-pointed; element 0 is the root clone; not added.
  - `void CommitChaser(this Project project, ChaserFunction edited, IReadOnlyList<LightFunction> owned)` — replaces the saved chaser's content with `edited` (same id), adds/replaces `owned` (each `OwnerFunctionId == edited.Id`), removes owned functions of this chaser no longer referenced by any step.
  - `LightFunction SaveStepAsFunction(this Project project, Guid chaserId, int stepIndex, string name)` — library copy (`OwnerFunctionId = null`) via `CloneWithOwned`; the step keeps its copy.
  - `bool IsOwnLookStep(this Project project, ChaserStep step, Guid chaserId)` — the step's function is a `SceneFunction` owned by `chaserId`.
  - `bool IsLinkedStep(this Project project, ChaserStep step, Guid chaserId)` — the step's function is not owned by `chaserId` (legacy shared step).

- [ ] **Step 1: Write failing tests** (`OwnedFunctionLifecycleTests`, `ChaserStepsTests`):
  - `FunctionListItems_OwnedByChaser_Hidden`
  - `CommitChaser_StepRemoved_OwnedFunctionDeleted`, `CommitChaser_NewOwnLookStep_AddedHiddenAndOwned`
  - `DeleteFunction_Chaser_DeletesOwnedRecursively`
  - `DuplicateFunction_Chaser_DeepCopiesOwnedSteps` (edit a step scene in the copy → original unchanged)
  - `ClipboardPaste_Chaser_DeepCopiesOwnedSteps`
  - `PlaceFunctionInShow_Chaser_CopiesOwnedSteps_ThenDeleteShow_LeavesNoOrphans` (Review Focus 1)
  - `CopyForStep_Scene_IsIndependentCopy`, `CopyForStep_Chaser_CopiesItsOwnedSteps`
  - `SaveStepAsFunction_CreatesLibraryFunction_StepKeepsCopy`
  - `IsLinkedStep_LegacyLibraryReference_True`
  - `VirtualConsoleTargets_ExcludeOwned`
  - `ProjectHistory_UndoAfterCommitChaser_RestoresChaserAndOwned` (Review Focus 5)
  - `RoundTrip_OwnerFunctionId_Serialized`
- [ ] **Step 2:** `dotnet test tests/DmxPro.Core.Tests --filter "FullyQualifiedName~ChaserSteps|FullyQualifiedName~OwnedFunctionLifecycle"` → FAIL (types missing).
- [ ] **Step 3:** Implement the interfaces above; keep `OwnerShowId` behaviour identical; copies made for shows of a chaser re-own the chaser's step copies to the new chaser copy.
- [ ] **Step 4:** Same command → PASS; then full `dotnet test DmxPro.sln` green.
- [ ] **Step 5:** Report (lead commits): `feat(core): chaser-owned step functions`.

### Task 2: Sequence → Chaser migration (sr-coder, after Task 1)

**Files:**
- Create: `src/DmxPro.Core/Projects/SequenceMigration.cs`
- Modify: `src/DmxPro.Core/Projects/ProjectSerializer.cs` (call after `RepairStage` and time clamping)
- Modify: `src/DmxPro.Core/Functions/FunctionFactory.cs` (creating `FunctionType.Sequence` creates a `ChaserFunction`; enum value kept for old JSON)
- Test: `tests/DmxPro.Core.Tests/SequenceMigrationTests.cs`, `tests/DmxPro.Engine.Tests/SequenceMigrationPlaybackTests.cs`

**Interfaces:** Consumes Task 1 `ChaserSteps.NewOwnLookScene`. Produces `static int SequenceMigration.Migrate(Project project)` (returns number converted).

- [ ] **Step 1: Failing tests:**
  - `Migrate_Sequence_BecomesChaserWithSameIdNameRunOrderDirection`
  - `Migrate_EachStep_BecomesOwnedHiddenSceneWithStepValuesAndTiming` (name = step name or `Step N`)
  - `Migrate_OwnershipKept_SequenceOwnedByShow_ChaserOwnedByShow`
  - `Load_OldFileWithSequence_SavesAsChaser_ReloadsIdentically`
  - Engine: `Playback_MigratedSequence_FramesEqualOriginalSequence` (render N ticks of the original `SequenceFunction` vs the migrated chaser; byte-identical frames — Review Focus 4)
  - Engine: `Playback_MigratedSequence_ViaVirtualConsoleAndShowItem_StillPlays`
- [ ] **Step 2:** run → FAIL.
- [ ] **Step 3:** Implement. If the engine compiles a scene step differently from a sequence step (e.g. scene fade times), make the migrated owned scene neutral (scene fades 0) so frames match; do not change engine behaviour for existing chasers.
- [ ] **Step 4:** run → PASS; full solution green.
- [ ] **Step 5:** Report: `feat(core): convert sequences to chasers on load`.

### Task 3: Chaser editor with own-look and copied steps (ui-designer/sr-coder, after Task 1; parallel with Task 2 and 4)

**Files:**
- Modify: `src/DmxPro.App/ViewModels/Editors/ChaserEditorViewModel.cs`, `ChaserStepRowViewModel.cs`, `ChaserStepListEditing.cs`, `src/DmxPro.App/Views/Editors/ChaserEditorView.xaml(.cs)`
- Create: `src/DmxPro.App/ViewModels/Editors/ChaserStepLookViewModel.cs` (+ view part) — port the step-look panel (lights, colour presets, Off, level sliders, All channels via `SceneChannelsTab`) from `SequenceEditorViewModel` / `SequenceStepEditing` / `SequenceLevelRowViewModel` / `SequenceLightRowViewModel`
- Modify: `src/DmxPro.App/ViewModels/Editors/FunctionEditorViewModel.cs` only to let an editor expose a "back to" target (`BackTarget`) if needed
- Test: `tests/DmxPro.App.Tests/ChaserEditorOwnedStepsTests.cs`

**Interfaces:** Consumes Task 1 (`OwnedFunctions`, `NewOwnLookScene`, `CopyForStep`, `CommitChaser`, `SaveStepAsFunction`, `IsOwnLookStep`, `IsLinkedStep`).
- Draft = `ChaserFunction` clone + `List<LightFunction>` clones of its owned functions; `Save` → `project.CommitChaser(draft, ownedDraft)`; Revert/Don't save discard both.
- `BuildPreviewFunction()` must compile with the draft owned scenes (preview of unsaved own-look edits) — pass draft owned functions to the preview the same way `SequenceEditorViewModel` previews the selected step (selected step / whole chaser switch).

- [ ] **Step 1: Failing tests:**
  - `AddStep_CopiesSelectedOwnLook_NewOwnedSceneInDraftOnly` (project unchanged until Save)
  - `AddStep_AfterFunctionStep_StartsBlankWithNearestOwnLookLights`
  - `AddFromFunction_Scene_BecomesOwnLookStep`, `AddFromFunction_Pulse_BecomesFunctionStepCopy`
  - `EditOwnLook_ChangesDraftScene_SaveCommitsAndOriginalLibraryUnchanged`
  - `EditLinkedStep_FirstEdit_TurnsIntoOwnedCopy_NoteShown` (Review Focus 3)
  - `RemoveStep_Save_DeletesOwnedFunction`; `Revert_DiscardsNewOwnedSteps`
  - `SaveStepAsFunction_AddsLibraryFunction`
  - `EditStep_FunctionStep_UnsavedChaser_RequestsSave` (dialog fake)
- [ ] **Step 2:** run → FAIL.
- [ ] **Step 3:** Implement UI per spec "Editor": toolbar **Add step** / **Add blank step** / **Add from function…** / **Save as function…**; right panel = step-look panel for own-look steps, or kind + summary + **Edit step** for function steps; linked mark + note for legacy steps; keep everything from the 2026-10-27 Chaser editor (timing, multi-select, drag, playback, colour strip).
- [ ] **Step 4:** run → PASS; full solution green.
- [ ] **Step 5:** Report: `feat(app): chaser steps with own looks and copies`.

### Task 4: App-wide hiding, Sequence removal, docs (jr-coder, after Task 1; parallel with Task 2 and 3)

**Files:**
- Modify: `src/DmxPro.App/ViewModels/FunctionsViewModel.cs` ("Show-only copies" toggle must not reveal chaser-owned functions), `FadeEditorViewModel.cs:171`, `ShowsViewModel.cs:1206`, `VirtualConsoleViewModel.cs:759` (use `FunctionListItems` / exclude owned), `src/DmxPro.App/MainWindow.xaml` + `Views/FunctionsView.xaml` (remove New ▸ Sequence menu items), `FunctionTypeOption` list (no Sequence filter), `src/DmxPro.App/ViewModels/Editors/FunctionEditorFactory.cs` (Sequence → chaser editor fallback)
- Delete: `Views/Editors/SequenceEditorView.xaml(.cs)`, `ViewModels/Editors/SequenceEditorViewModel.cs`, `SequenceStepRowViewModel.cs` — **only after Task 3 has ported what it needs** (lead sequences this)
- Docs: `docs/ui-plan.md` §3.3, `docs/architecture.md` (ownership + migration), `docs/spec/product-requirements.md` decisions log, `AGENTS.md` feature list ("sequence" → merged)
- Test: `tests/DmxPro.App.Tests/OwnedFunctionVisibilityTests.cs`

- [ ] **Step 1: Failing tests:** `FunctionsPage_ShowOnlyCopiesOn_ChaserOwnedStillHidden`, `VirtualConsoleTargets_ExcludeChaserOwned`, `ShowsLibrary_ExcludeChaserOwned`, `FadeSceneList_ExcludeChaserOwned`, `NewFunctionTypes_DoNotContainSequence`.
- [ ] **Step 2:** run → FAIL. **Step 3:** implement. **Step 4:** run → PASS, full green. **Step 5:** Report: `refactor(app): hide chaser-owned functions, remove Sequence type from the UI`.

### Task 5: Verify (lead)

- [ ] Merge Tasks 1–4; full build/test/format; `safety-reviewer` + `code-reviewer` in parallel on the diff; fix findings; publish exe; push.

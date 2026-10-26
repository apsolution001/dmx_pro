# Chaser steps without extra functions (Sequence merged into Chaser) — design

Date: 2026-10-27 · Status: draft for owner approval

## Intent (owner's words, summarised)

To build a chaser today the owner must first create a scene (or other function) for every step, then add those
functions to the chaser. That fills the function list with "duplicate, unused" functions that exist only to feed one
chaser. The owner wants to build a chaser **directly**:

1. **New step = own look:** add a step and set its lights / colours / channels right inside the chaser, like the Scene
   editor — no separate function is created in the list.
2. **Step from an existing function = a copy:** "just like the Shows" — picking a scene or function puts the chaser's own
   copy in the step; editing the step never changes the original and vice versa.

Because own-look steps make Chaser do what Sequence does, **Sequence is merged into Chaser** (owner decision
2026-10-27): one type, "Chaser".

## Goals

- A chaser can be built with zero helper functions in the Functions list.
- Both step kinds can be mixed freely in one chaser and reordered, duplicated, timed as today.
- Step copies are independent of their source (copy semantics, like show copies).
- Existing projects keep working: existing chasers keep their steps; existing sequences open as chasers that play the
  same way.
- The Functions list shows only real, user-facing functions.

## Non-goals

- No live link between a step and its source function (no "update from original").
- No new engine features; playback timing / run order / direction stay as they are.
- No change to Scene, Colour cycle, Fade, Pulse, Flash, Strobe editors.

## Anti-goals (would be a failure even if it works)

- Hidden step functions leaking into the Functions list, Virtual Console target lists, Shows library, or the
  "Add to chaser" picker.
- Deleting / duplicating / copy-pasting / placing a chaser in a show leaving orphaned or shared step functions (two
  chasers sharing one hidden step function).
- An undo that restores a chaser but not its step functions (or the reverse).
- Old project files that fail to open or play differently.

## Model (builder's call within these rules)

Reuse the existing "owned copy" pattern used by shows (`LightFunction.OwnerShowId`, hidden from the list):

- Add `LightFunction.OwnerFunctionId` (Guid?): the function (a chaser) that owns this function. A function has at most
  one owner (a show **or** a chaser). Owned-by-chaser functions are always hidden from the Functions list, VC targets,
  Shows library and pickers (extend `FunctionListItems` and the other filters).
- **Own-look step** = the step references an owned, hidden `SceneFunction` created by the chaser ("Step 3" name).
  Editing the step edits that scene with the existing scene-editing UI. The engine plays it exactly like a scene step
  today → no engine change for playback.
- **Copied step** = picking a library function deep-clones it (`CloneAsCopy`) with `OwnerFunctionId = chaser.Id`, hidden;
  the step references the clone. Chasers/fades copied this way also need their own referenced functions handled: a copied
  chaser's owned steps are deep-copied too; a copied fade keeps references to library scenes (fade copies only itself — see Builder's calls).
- `ChaserStep` keeps `FunctionId` + timing; optionally a `Name` for own-look steps (or use the owned scene's name).
- Ownership lifecycle (all in Core, all tested): deleting a chaser deletes its owned functions; removing a step deletes
  its owned function; duplicating / copy-pasting a chaser deep-copies owned functions and re-points the steps; placing a
  chaser in a show (`PlaceFunctionInShow`) copies the chaser **and** its owned step functions (owned by the new chaser
  copy, which is owned by the show); deleting a show cleans up its chaser copies' owned functions; `FunctionReferences`
  / delete checks count owned steps correctly.
- **Save as function** on a step (like Shows): makes a library copy of that step's function; the step keeps its own copy.
- **Legacy steps:** existing chasers whose steps reference library functions keep working as-is (shared references stay
  allowed for old data). Optional "Make independent" action converts a step into a copy. New steps added from the picker
  are always copies.

## Sequence → Chaser migration

- On project load, each `SequenceFunction` becomes a `ChaserFunction` with the **same id, name, run order, direction,
  timing**; each sequence step becomes an own-look step (an owned hidden scene with the step's values, limited to the
  sequence's lights, name = step name or "Step N").
- References to the sequence id (Virtual Console widgets, show items, chaser steps, fades) keep working because the id
  is unchanged.
- `New ▸ Sequence` and the Sequence editor are removed from the UI; the `SequenceFunction` type stays readable for old
  files (load-time conversion) and can be deleted from the save format (files are saved as chasers).
- Engine output for a migrated sequence must equal the old sequence output frame-for-frame (engine test).

## Editor (rebuilt Chaser editor, extended)

- Steps list as today (number, swatch, name, Hold / Fade in / Fade out, multi-select, move, drag, duplicate, remove,
  Apply timing to selected / all, Loop / Play once / Back and forth, Forward / Backward, colour strip, cycle length).
- Toolbar: **Add step** (new own look; copies the selected step's look so patterns are quick), **Add blank step**,
  **Add from function…** (search picker → copies), **Save as function…** for the selected step.
- Selecting an own-look step shows a step-look panel on the right: Choose lights for this step (defaults to the previous
  step's lights), colour presets / Off / level sliders / all-channels view — reuse what the Sequence editor built
  (2026-10-27) rather than inventing new UI. Selecting a copied step shows its kind + name, a short summary, and
  **Edit step** opening that function's normal editor (scene editor for scenes, etc.) for the owned copy.
- Live preview on lights: as the Sequence editor does — the selected step while editing, with the "This step / Whole
  chaser" switch.

## Constraints

- Safety rules unchanged (preview path, strobe limits for copied strobe steps, Release All).
- Undo/Redo: ownership changes must be captured as one step (chaser + owned functions together).
- Project file format: additive (`ownerFunctionId`), old files load; document in `docs/architecture.md`.
- Tests: Core lifecycle + migration, Engine equivalence for migrated sequences, App editor tests (AppRig).

## Decided

- Merge Sequence into Chaser (owner, 2026-10-27).
- Steps from existing functions are copies, like Shows (owner, 2026-10-27).
- Own-look steps are edited directly in the chaser (owner, 2026-10-27).

## Builder's calls (lead's defaults, owner may override)

- Own-look steps are stored as owned hidden scenes (reuse scene engine path) rather than inline values.
- Own-look step lights are per step, defaulting to the previous step's lights.
- Legacy shared step references in existing chasers are kept as-is (not auto-converted) to avoid surprising changes.

- **A scene picked with "Add from function…" becomes an own-look step** (an owned hidden scene, edited inline like any
  own-look step). No step "kind" is stored: a step is "own look" when its owned function is a scene, otherwise it is a
  "function step" (copied chaser / colour cycle / fade / pulse / flash / strobe). So no file-format flag is needed.
- **"Add step" when the selected step is a function step** (e.g. a pulse) starts a blank look with the lights of the
  nearest own-look step above it (or no lights).
- **"Edit step" on a function step** opens that copy's normal editor in the same editor area with a
  "← Back to chaser 'Name'" link (like opening a show copy from the Shows page). If the chaser has unsaved changes, the
  usual Save / Don't save / Cancel prompt appears first. The copy is saved with its own Save; Back returns to the chaser.
  Consequence: an owned copy is created in the project when the step is added (so it can be edited), and removed again
  if the chaser's changes are discarded (Revert / Don't save) — the chaser draft tracks copies it created and deletes
  them on discard. One undo step covers "chaser saved + its new copies".
- **Old chasers with shared library steps:** the step shows a small "linked" mark. The first time such a step is edited
  (own-look panel change or Edit step), it is turned into the chaser's own copy automatically, with a one-line note
  ("This step is now the chaser's own copy; the original 'Red wash' is unchanged"). So editing a step never changes a
  library function. A "Make independent" button is therefore not needed.
- A copied fade copies only the fade; its From / To keep pointing at library scenes (a fade's scenes are shared, as
  today).

## Open questions (owner)

- None blocking. The lead's calls above are listed for the owner to check.

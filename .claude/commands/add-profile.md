---
description: Create a fixture profile from a manufacturer DMX chart
argument-hint: <manufacturer model, or path to manual/chart>
---

Create a fixture profile for: $ARGUMENTS

1. Read `docs/knowledge/05-fixture-profiles.md` for the JSON format and channel types.
2. Get the DMX chart (ask the user to paste it if not provided). Never guess channel meanings.
3. Create one profile with all modes; set `capabilities` ranges exactly as in the chart; mark `safe` ranges.
4. Set `"verified": false` unless the user confirms it was tested on the real fixture; fill `source`.
5. Save to `profiles/<manufacturer>-<model>.dmxprofile.json` (kebab-case) and add/extend a test that loads it.

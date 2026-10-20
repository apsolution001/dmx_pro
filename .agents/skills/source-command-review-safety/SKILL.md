---
name: "source-command-review-safety"
description: "Run the safety review on the current changes"
---

# source-command-review-safety

Use this skill when the user asks to run the migrated source command `review-safety`.

## Command Template

Use the `safety-reviewer` subagent to review the current diff against `docs/standards/safety-rules.md`,
then summarize PASS/FAIL per rule and fix any FAIL items after confirming with the user.

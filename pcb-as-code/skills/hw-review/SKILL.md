---
name: hw-review
description: Deep multi-wave review of a pcb-as-code hardware branch (docs, design.cs, schematics, boards, datasheet pin checks) by cheap read-only reviewer agents; the session triages, fixes the source and regenerates. Use before a fab order or after a large change.
argument-hint: "[optional: branch / base ref / reviewer budget / intent]"
---

# HW Review

A full-branch sweep: a **fixed budget of cheap read-only reviewers** (passed in, or auto-scaled 1-8 from the change), cut into **waves** of at most 6. Each wave is triaged by the session, its confirmed fixes land **in `design.cs`, a generator, the project rules or the docs**, the affected boards are regenerated and the gates re-run — then the next wave reviews the updated branch. Docs, design source and tooling, schematics, boards and renders go through the same sweep, and pin maps are checked against the parts' datasheets, which no gate can do.

Runs on ZCode (`hw-reviewer-glm` shards) and Claude Code (`hw-reviewer-claude` shards).

**Process:** load [references/wave-protocol.md](references/wave-protocol.md) and follow it end to end — scope, budget, waves, fix-and-regenerate, wave log, final report.

## Not this skill

| The user wants… | Use instead |
|---|---|
| One suspicious net or schematic section checked | the session itself — read `design.cs` and the datasheet |
| The mechanical gates (ERC, pin parity, DRC, fab limits) | the pipeline's gates (`verify.cs`, `fabcheck.cs`) |
| A design reviewed before any code exists | the pcb-as-code kickoff interrogation (`${CLAUDE_PLUGIN_ROOT}/skills/pcb-as-code/references/kickoff.md`) |

Firmware is in scope (an authored surface), but its native host tests are the primary instrument.

## ⛔ Non-negotiables

- **The reviewer never verdicts its own findings** — the session triages every one (evidence rule first, verified against source and datasheet); over-reporting is the designed failure mode.
- **Only `fix` verdicts change anything**; a `defer` is the user's, handed over at the end.
- **Fixes land in the source, never in generated KiCad files** — then regenerate.
- **Every wave re-runs the gates it touched** — no new red against the baseline, or the fix is not done.
- **The run ends when the budget is spent** — a clean wave refunds nothing.
- **No per-wave approval gate, nothing committed** — fixes stay in the working tree; the wave log and final report are the audit trail.
- **The final report leads with the severity summary and an explicit ANOTHER SWEEP: YES/NO verdict** — any critical/high still open says YES.

## Prerequisites

Git, on a pcb-as-code project (`pcb/tools/design.cs`), with README's regeneration command list runnable. The sweep takes the working tree as found and never commits, stashes, or asks for either.

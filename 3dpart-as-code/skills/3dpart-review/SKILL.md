---
name: 3dpart-review
description: Deep multi-wave review of a 3dpart-as-code branch (docs, part.cs, geometry scripts, renders, mating datasheets) by cheap read-only reviewer agents; the session triages, fixes the source and rebuilds. Use before a long print or after a large change.
argument-hint: "[optional: branch / base ref / reviewer budget / intent]"
---

# Part Review

A full-branch sweep: a **fixed budget of cheap read-only reviewers** (passed in, or auto-scaled 1-8 from the change), cut into **waves** of at most 6. Each wave is triaged by the session, its confirmed fixes land **in `part.cs`, a geometry script, the tooling or the docs**, the parts are rebuilt and the gates re-run — then the next wave reviews the updated branch. It spends reviewers on what the gates cannot see: dimensions against the mating parts' datasheets and drawings, printability (walls, bridges, layer direction against load, snap strain), fits that are consistent but wrong, docs that contradict the model, renders that show a feature on the wrong face.

Runs on ZCode (`part-reviewer-glm` shards) and Claude Code (`part-reviewer-claude` shards).

**Process:** load [references/wave-protocol.md](references/wave-protocol.md) and follow it end to end — scope, budget, waves, fix-and-rebuild, wave log, final report.

## Not this skill

| The user wants… | Use instead |
|---|---|
| One dimension or fit checked | the session itself — read part.cs, the probe and the datasheet |
| The mechanical gates (dimensions, meshes, fits) | the pipeline's tools (`check.cs`, `fit.cs`) |
| A part reviewed before any model exists | the 3dpart-as-code kickoff interrogation (`${CLAUDE_PLUGIN_ROOT}/skills/3dpart-as-code/references/kickoff.md`) |
| The boards a case mates with reviewed | pcb-as-code's `hw-review` |

## ⛔ Non-negotiables

- **The reviewer never verdicts its own findings** — the session triages every one (evidence rule first, verified against part.cs, the probes, the renders and the cited datasheet); over-reporting is the designed failure mode.
- **Only `fix` verdicts change anything**; a `defer` is the user's, handed over at the end.
- **Fixes land in the source, never in `mech/build/` or `mech/out/`** — then rebuild.
- **Every wave re-runs the gates** — no new red against the baseline, or the fix is not done.
- **The run ends when the budget is spent** — a clean wave refunds nothing.
- **No per-wave approval gate, nothing committed** — fixes stay in the working tree; the wave log and final report are the audit trail.
- **The final report leads with the severity summary and an explicit ANOTHER SWEEP: YES/NO verdict** — any critical/high still open says YES.

## Prerequisites

Git, on a 3dpart-as-code project (`mech/tools/part.cs`), with the pipeline runnable (`mech/.venv` or `PAC_PYTHON`). The sweep takes the working tree as found and never commits, stashes, or asks for either.

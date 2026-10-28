---
name: hw-review
description: Sweep a whole hardware branch in waves of cheap read-only reviewer agents — a total reviewer budget (passed in, or auto-scaled 1-8 from the change's size) cut into waves of at most 6, each wave triaged by the session (false positives weeded) with its confirmed fixes applied — into design.cs, a generator or the docs, then regenerated, never hand-edited in KiCad — before the next wave reviews the updated branch; the run ends when every budgeted reviewer has reviewed, closing with a severity summary of what was fixed. Reviews documentation, the design source and tooling, schematics and PCBs (board text + render images) in the same sweep, including pin-map checks against part datasheets. Use before a fab order, after a large change, or whenever a hardware project needs a deep multi-pass review.
when_to_use: A hardware project or branch — often a whole build — that deserves a deep multi-pass review before the fab order (boards cost money) or after a large change (parts/nets added, re-routed, docs rewritten), touching documentation, schematics, PCBs or any mix.
argument-hint: "[optional: branch / base ref / reviewer budget / intent]"
---

# HW Review

A full-branch review sweep for pcb-as-code projects: a **fixed budget of
cheap read-only reviewer agents**, cut into **waves** of at most 6, each
wave triaged by the session and its confirmed fixes applied — **into
`design.cs`, a generator, the project rules or the docs, then the affected
boards regenerated and the gates re-run**; a generated KiCad file is never
hand-edited (the workflow's own hard rule) — before the next wave reviews
the updated branch. The run ends when every budgeted reviewer has reviewed
— the budget is the depth knob, and the user set it. Documentation, the
design source and tooling, and the generated artifacts (schematics, boards,
renders) go through the same sweep, each surface with its own check list —
and the pin maps are checked against the parts' actual datasheets, which no
gate in the pipeline can do.

Runs on **both ZCode and Claude Code** — the shards are the plugin's
reviewer twins (`hw-reviewer-glm` on ZCode, `hw-reviewer-claude` on Claude
Code); the playbook's harness notes pick the twin and the spawn mechanics.

**This file is routing only** — the sweep's playbook lives in one
reference, loaded at the start: [wave-protocol.md](references/wave-protocol.md).

## When to use this skill

A hardware branch whose whole change deserves a deep, multi-pass review —
before the fab order (the proven case: it took several waves to find and
fix everything), after adding parts or nets, after a re-route, or before
opening the PR — taken as found: committed work and uncommitted
working-tree changes alike go into the same diff, and the sweep never asks
for a commit or a stash. The invocation may carry: the branch and base ref
(defaults: current HEAD, merge-base with `main`), the total reviewer budget
(else auto-scaled from the change), and an intent statement (the review's
anchor; `docs/design.md` is the standing fallback).

| The user wants… | Use instead |
|---|---|
| A single suspicious net or one schematic section checked | the session itself — read `design.cs` and the datasheet; don't spend a sweep |
| The mechanical gates (ERC, pin parity, DRC, fab limits) | the pipeline's own gates (`verify.cs`, `fabcheck.cs`) — they are cheaper and exact |
| A design reviewed before any code exists | the pcb-as-code skill's kickoff interrogation (`${CLAUDE_PLUGIN_ROOT}/skills/pcb-as-code/references/kickoff.md`) — this sweep reviews what was built |
| Firmware logic reviewed | still this sweep (the firmware is an authored surface), but its native host tests are the primary instrument |

Do not use for: a diff so small one reviewer covers it (that is the
auto-scale's own answer: budget 1, one wave).

## The three phases

| Phase | Mode | Produces | Playbook |
|---|---|---|---|
| 1 | normal | Scope: branch + base, the working tree as found, the review set split by surface (docs / source / boards), the measure, the anchor, the gate baseline, the budget and the wave cut — announced in one line before any spawn | [wave-protocol.md](references/wave-protocol.md) §Step 1–2 |
| 2 | normal | The waves: spawn → mechanical merge → session triage (evidence rule first) → fixes applied into the source + regenerate + gates re-run → wave log — until every budgeted reviewer has reviewed | [wave-protocol.md](references/wave-protocol.md) §Step 3–4 |
| 3 | normal | The final report: the severity summary first — a fixed/dismissed/still-open table per level (critical/high/medium/low) and an explicit **ANOTHER SWEEP: YES/NO** verdict with its reasons (any critical/high still open says YES, doubly so before a fab order) — then plan as executed, per-wave counts, the uncommitted working tree, any shard that never reviewed | [wave-protocol.md](references/wave-protocol.md) §Step 5 |

## ⛔ Non-negotiables

- **The reviewer never verdicts its own findings** — the session triages
  every finding (evidence rule first, verify against source and datasheet);
  a cheap reviewer over-reporting is the designed failure mode.
- **Only `fix` verdicts change anything** — a dismissal changes nothing; a
  `defer` is the user's, collected and handed over at the end.
- **Fixes land in the source, never in generated KiCad files** — a fix for
  a schematic or board finding goes into `design.cs` or the generator, the
  docs fix into the doc, and the boards are regenerated; hand-editing a
  generated file is the one change the sweep must never make.
- **Every wave re-runs the gates it touched** — regenerate, then the
  project's own gates (tests, ERC, verify, DRC, fabcheck) measured against
  the baseline: no new red, or the fix is not done.
- **The run ends when the budget is spent** — a clean wave does not refund
  the budget; the remaining waves still run. Hardware experience says the
  later waves are exactly where the last issues surface.
- **No per-wave approval gate** — the user launched a fix-it sweep; the
  fixes land in the working tree, and the wave log plus the final report
  are the audit trail.
- **Severity grades the exit, never the schedule** — every finding carries
  a critical/high/medium/low grade (critical means the board would be
  wrong as built — a re-spin or a part at risk) and the final report ends
  its severity summary with an explicit **ANOTHER SWEEP: YES/NO** verdict
  and the reasons; the run itself always ends when the budget is spent.

## Process

Load [wave-protocol.md](references/wave-protocol.md) and follow it end to
end — it carries the budget table, the wave cut, the per-surface check
lists, the stamped shard prompt, the fix-and-regenerate cycle, and the wave
log's grammar.

## Prerequisites

**Git**, on any branch of a pcb-as-code project (one with
`pcb/tools/design.cs`) — the sweep takes the working tree as it finds it
and never commits, stashes, or asks for either. The project's own
regeneration command list (README) must be runnable — the sweep regenerates
boards to apply fixes. The shards are the plugin's own reviewer twins —
`pcb-as-code:hw-reviewer-glm` on ZCode, `pcb-as-code:hw-reviewer-claude` on
Claude Code; both cheap, read-only, datasheet-capable — and no factory
tool is invoked.

## Working style

- **Reviewer = recall, the session's triage = precision** — multiplied by
  waves: every wave's reviewers see the fixed branch with fresh eyes, the
  wave log keeps their reports deduplicated against everything already
  recorded.
- **The anchor travels into every prompt** — the intent statement, else the
  branch's commit messages, else `docs/design.md` as the standing
  requirements record; divergence and coverage are measured against it.
- **The gates are the floor, not the review** — ERC/parity/DRC/fabcheck
  already hold the mechanical line; the sweep spends its reviewers on what
  gates cannot see: datasheet conformance, docs↔design↔board consistency,
  visual and geometric judgment, intent.
- **The reviewers are read-only** — they never edit, build, route, or run
  gates; the session regenerates and re-runs the gates after each wave's
  fixes.
- **The wave log is the sweep's memory** — mirrored outside the repository;
  later waves dedupe against it, and the final report is built from it.
- **Routing economics are part of the triage** — a fix that moves parts or
  pads re-routes the board (a dense board takes ~40 min per attempt); the
  pipeline's retry recipe and copper reuse (`--from HEAD` where the
  project's route.cs has it — else the step-5 net-name copy) are the sweep's
  own tools, and a fix whose re-route cannot go clean is reverted and
  handed to the user as a defer.

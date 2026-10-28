# HW review waves — the hardware branch-sweep playbook

A **reviewer budget** — a fixed total of cheap read-only reviewer agents — cut into **waves** of at most 6; each wave reviews the branch's current state, the session triages its findings, the confirmed fixes land **in the design source** (`design.cs`, a generator, project rules, docs) and the affected outputs are **regenerated** — all uncommitted, in the working tree; the next wave reviews the updated branch. **The run ends only when the budget is spent** — a clean wave does not refund it; on hardware the later waves are exactly where the last issues surface.

## Two surfaces, one sweep

- **authored** — files written directly: `docs/`, `README.md`, `AGENTS.md`, `pcb/fab.json`, `sim/`, `firmware/`, `pcb/tools/*.cs` (`design.cs`, generators, templates' project copies, tests). Reviewed **as a diff**.
- **boards** — generated artifacts, per board (one indivisible unit): its `.kicad_sch`, `.kicad_pcb`, `.kicad_pro` (net classes, design rules), `pcb/lib/` when it changed (the footprints the datasheet check judges live there), the `pcb/schematics/` PDF and `pcb/renders/` images (netlist too, if the project keeps one). Reviewed **as files, never as diff hunks** — routing churn and zone fills swamp a diff.

The mechanical gates (ERC, pin parity, DRC, fabcheck, tool tests) are the floor; the sweep spends reviewers on what gates cannot see — datasheet conformance, docs↔design↔board consistency, visual/geometric judgment, intent. **A condition the gates check and pass is not a finding; a check the gates LACK is.**

## Step 1 — Scope

1. **Branch and base.** Current HEAD unless the invocation names a branch (checked out in item 2, before anything is measured). Base: the invocation's ref, else `git merge-base main HEAD`. No `main` → stop, ask for the base. "Nothing to review" only when `git diff <base>` is empty after item 2.
2. **Working tree as found.** A named branch that differs from the checkout is switched to here — a dirty tree makes that switch a question, never a force. Never ask for a commit or stash — the diff already includes the tree. Snapshot `git status --porcelain` (rides to the final report). Untracked files: list them, ask which belong in; the chosen are intent-added file by file (`git add -N <file>`; never `git add -N .`); build outputs (`pcb/build/`, `pcb/gerbers/`, `pcb/<board>-backups/`, `obj/`, `bin/`) are never offered in. Merge/rebase in progress or unmerged paths → stop, ask.
3. **Review set.** `git diff --name-only <base>`, split by surface (genuinely ambiguous → authored; the stricter check list wins). A board is "changed" when any of its files changed.
4. **Measure** — authored diff lines **+ 400 per changed board** (a board carries visual, geometric and datasheet review that changed lines don't capture):

   ```bash
   git diff <base> -- docs README.md AGENTS.md pcb/fab.json sim firmware pcb/tools | wc -l
   ```
   (PowerShell: `| Measure-Object -Line`.)

5. **Anchor.** The invocation's intent statement; else the branch's commit messages (`git log <base>..HEAD`). `docs/design.md`, the standing requirements record, is read context for every shard either way. Neither intent nor commits → `docs/design.md` alone anchors divergence and coverage; say so in the report.
6. **Gate baseline.** Run and record once, before any spawn: `dotnet test pcb/tools/tests` and the project's `verify.cs`; `fabcheck.cs` when `pcb/fab.json` exists; read any existing DRC reports in `pcb/build/` (route nothing). Pre-existing red is recorded and stands — Step 4's no-new-red gate measures against it.
7. **Scratch directory.** Create one outside the repository (the session's scratchpad or temp dir) and announce its absolute path: it holds the wave log and each wave's per-shard diff files. Never in the repository, never committed.
8. **AGENTS.md note.** If the project's `AGENTS.md` carries no `hw-review` bullet (match on the skill name, not the exact text), add this bullet under its rules (create no file; a project without AGENTS.md is named in the report instead):

   ```markdown
   - **Reviews (docs, schematics, boards) run as the pcb-as-code `hw-review` sweep:** waves of read-only reviewer agents, triaged by the session; confirmed fixes land in `design.cs`, a generator or the docs, and the boards are regenerated (never hand-edited) with the gates re-run before the next wave. Run it before a fab order and after any large change.
   ```

## Step 2 — Budget and wave cut

**Budget `N` = total reviewers for the whole run.** The invocation's number wins in both directions. Before a fab order, ≥ 6 is the honest default. Otherwise the measure sets it:

| Measure (diff lines + 400/board) | ≤ 300 | 301–800 | 801–1500 | 1501–2500 | 2501–4000 | 4001–6000 | 6001–9000 | > 9000 |
|---|---|---|---|---|---|---|---|---|
| Budget | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |

Ceiling: **at most 6 reviewers at once**; cut the budget into waves of ≤ ceiling, remainder last (8 → 6+2). Shard units: whole authored files (contiguous, size-balanced — docs with docs, `design.cs` with the generators it feeds) and each changed board as one unit, never split mid-board. When units cover the wave and K > 1, the last shard is the **seam shard** — no unit of its own; it owns the cross-surface contracts: net names shared across boards really shared at both ends, `pinmap.md` vs `design.cs`, `wiring.md` vs ribbons/harnesses, `bom.md` vs the design's parts (off-board included), README's regeneration list vs the tools that exist, `AGENTS.md`'s specifics vs what `verify.cs` checks. Fewer units than reviewers → units deliberately overlap, each reviewer given a different lens (datasheet conformance / renders and mechanics / docs consistency); the seam role lapses. The budget's total is never reduced; only each wave's shape bends.

Announce one line before spawning: budget, wave cut, ceiling, anchor, reviewer identity, scratch directory. No mid-run budget changes.

## Step 3 — Run the waves

Per wave, in order:

1. **Cut the shards** against the branch's *current* state: recompute `git diff --name-only <base>` (after `git add -N` on the sweep's own new source files — never `git add -N .`), split per Step 2's units.
2. **Write each shard's material to the scratch directory** with the shell — never paste it into a prompt: `git diff <base> -- <this shard's authored files> > <scratch>/w<w>-s<k>.diff` (a seam shard: `git diff --stat <base> > <scratch>/w<w>-s<k>.diff`; a board-only shard: no diff file). The wave log lives at `<scratch>/wave-log.txt` (§The wave log).
3. **Spawn the reviewers in parallel** — one message, one Agent call per shard, the stamped prompt below. ZCode: each `run_in_background: true`, collected per [zcode-wall-clock.md](zcode-wall-clock.md). Claude Code: foreground; maxTurns is the bound.
4. **Merge mechanically** — never triage at the merge: dedupe (same file:line + same failure; in doubt, keep both), concatenate in shard order with each shard's ranking preserved, renumber `F1..Fn`, grades untouched, cut nothing (the per-shard cap of 8 is the only cap). Empty/malformed report → one re-run, same prompt; errored spawn → one re-spawn; still nothing usable → named in the final report with its slice.
5. **Triage** — evidence rule first; verify every survivor against the source (datasheet findings: against the datasheet yourself before confirming); one explicit verdict per finding — `fix` / `dismiss` / `defer` — confirming or correcting the grade (the triage owns the grade). Dedupe vs the wave log: a re-report **with new evidence** re-opens; an identical one drops.
6. **Apply the fixes** (Step 4) — all of the wave's `fix` verdicts, then **one** regeneration pass, then the gates.
7. **Log the wave**; the next wave reviews the updated branch.

**Triage's yardsticks** (the reviewer body carries the same text):
- *Evidence rule* — a finding without it is auto-dismissed: a file:line (or §ref, or render + region) and the concrete failure; datasheet → the design.cs line AND the datasheet pin/wording; divergence/coverage/regression → the anchor text or existing behavior; bug → trigger + expected-vs-actual; contradiction → both locations. A condition the gates check and pass is not a finding.
- *Grades* — **critical**: the board would be wrong as built (a re-spin or a part at risk; a wiring error no gate sees; an enclosure conflict blocking assembly). **high**: misleads (doc vs built design, wrong pinmap entry, wrong assembly/bring-up instruction). **medium**: stale or incomplete but reconcilable nearby. **low**: polish.

### The spawn identity

Agent tool, one of the plugin's **reviewer twins** — identical bodies (rules, check list, evidence rule, grades, report format), one declaration per harness's cheap model; the declaration fixes model and read-only posture, the spawn sets no model key:

- **ZCode:** `subagent_type: pcb-as-code:hw-reviewer-glm`
- **Claude Code:** `subagent_type: pcb-as-code:hw-reviewer-claude`

The reviewers have no shell: everything is named by absolute path — diff files, wave log, boards, renders, `docs/design.md`. A rejected spawn strands the wave: end the sweep, name the wave and its slice in the final report.

### The shard prompt (stamped copy)

Only the variable part — the agent body carries everything fixed. Adapt the bracketed bits; drop lines that do not apply (at K = 1: the surface line becomes the whole review set).

```
Wave <w>, shard <k>/<K>. Project root: <abs path>.
Surface: <authored files owned | board <name>: pcb/<name>.kicad_sch, .kicad_pcb, .kicad_pro, pcb/renders/<name>*, pcb/schematics/<name>.pdf, pcb/lib/ when changed | lens <datasheet conformance | renders and mechanics | docs consistency> on <unit> | SEAM: cross-surface contracts, no unit of your own>.
Diff vs <base> (earlier waves' fixes included): <scratch>/w<w>-s<k>.diff
Anchor: <intent statement | "commit messages:" + git log one-liners | "docs/design.md alone" | no intent, commits or docs/design.md: "No anchor: skip divergence and coverage.">; docs/design.md is read context.
Wave log (wave > 1): <scratch>/wave-log.txt
```

## Step 4 — Apply the fixes (source → regenerate → gates)

All of the wave's `fix` verdicts first, then **one** regeneration pass, then the gates — batching matters: a `design.cs` fix that moves parts re-routes a board for ~40 minutes; three fixes route once.

- **A generated artifact is never hand-edited.** Every board finding translates to a source fix: `design.cs` (a part, net, pin map, footprint), the generator (placement, drawing, copper), the project rules (`verify.cs`'s construction checks), or the docs. Naming the file each fix lands in is the fix.
- **docs / fab.json / firmware / sim** → edit in place; firmware fixes run their native host tests.
- **design.cs or a generator** → edit, then regenerate per README's command list, restricted to the steps downstream of the change. A change that moves **no** part or pad reuses the committed copper (`route.cs <board> --from HEAD` where the project has it; else copper-copy by net name per `${CLAUDE_PLUGIN_ROOT}/skills/pcb-as-code/references/step5-route.md`). A change that moves parts or pads re-routes with the retry recipe (DSN shuffle per attempt, ≤ 8 attempts).
- **Verify** against the Step 1 baseline — **no new red**: `dotnet test pcb/tools/tests`, then per affected board ERC 0/0 and `TOTAL PROBLEMS: 0` (`verify.cs`), DRC 0 errors / 0 unrouted, `FAB PROBLEMS: 0` (`fabcheck.cs`). Baseline red neither blocks nor gets chased; new red does — not green, not done.
- **A fix whose re-route cannot go clean** within the retry budget is **reverted** (source edit undone, board regenerated back), recorded `defer — route blocked`, handed to the user. The sweep never leaves a board red and never waives a gate to keep a fix.

Then stop — **nothing is committed**. Fixes stay in the working tree (the sweep's own new source files intent-added so later waves count them); every later wave's surface is `git diff <base>`, which keeps the previous wave's fixes under review.

⛔ **No per-wave approval gate:** every fix traces to a cited finding, nothing commits, and the wave log plus the final report are the audit trail. A `defer` never blocks the budget.

⛔ **Rubber-stamping every finding is as wrong as dismissing them all.** A high-recall cheap reviewer makes noise by design — especially datasheet claims (wrong part variant fetched, a typical-application circuit mistaken for a requirement). Verify before confirming.

## The wave log

One line per finding ever recorded, appended at each wave's triage, kept in the conversation **and** in `<scratch>/wave-log.txt` (a long sweep can outlive a compaction; later waves' reviewers read it there):

```
wave <w> — F<n> [<check>|<level>] <file:line, §ref or render> — <one-line description> — <verdict> — <one-line reason>
```

The description lets the next wave tell an identical re-report from a new failure at the same spot. The severity summary reads each finding's **latest line only**.

## Step 5 — The end and the report

The sweep ends when the last budgeted reviewer has reviewed — a wave whose findings all die in triage does not end it.

Final report, in this order:

- **The severity summary — first, in this exact shape**, counts from each finding's latest wave-log line:

  | Level | Fixed this run | Dismissed | Still open |
  |---|---|---|---|
  | Critical | n | n | n |
  | High | n | n | n |
  | Medium | n | n | n |
  | Low | n | n | n |

  ("Still open" = deferred and route-blocked findings — everything not fixed and not dismissed.) Then the verdict, on its own line:

  > **ANOTHER SWEEP: YES** — <each critical/high still open, one line each, with its question> — do not order boards before it closes.

  > **ANOTHER SWEEP: NO** — everything critical/high is fixed; what remains is medium/low polish.

  **Any critical or high still open says YES** — doubly so with a fab order pending. Every still-open finding is listed under the verdict with its one-line question (a route-blocked fix names the board and the placement conflict). A new sweep takes the branch as it now stands.
- **The plan as executed:** budget, wave cut, ceiling, wall clock (ZCode), anchor, reviewer identity.
- **Per wave:** reviewers, findings, verdict counts; boards regenerated, and any re-route with its duration.
- **The working tree:** every fix is uncommitted — the user's to review, commit, or discard. Name the files Step 1's snapshot showed already dirty, the untracked files left out of scope, and the intent-added files with their undo, `git reset -- <file>`. If the boards are to be ordered, re-run export so the gerbers match the fixed boards.
- **Shards that never produced a usable report**, named with their surfaces — the report never implies coverage that did not happen.

⛔ **Severity grades the exit — it never ends the run early or refunds the budget.** The ANOTHER SWEEP verdict launches a fresh run (the user's call), never extends this one.

# HW review waves — the hardware branch-sweep playbook

**What this is:** the playbook the hw-review skill runs on a pcb-as-code project. A **reviewer budget** — a fixed total of cheap read-only reviewer agents — cut into **waves** of at most 6; each wave reviews the branch's current state, the session triages its findings, the confirmed fixes land **in the design source** (`design.cs`, a generator, project rules, docs) and the affected outputs are **regenerated** — all uncommitted, in the working tree; the next wave reviews the updated branch. **The run ends only when the budget is spent** — a clean wave does not refund it; on hardware the later waves are exactly where the last issues surface.

## Two surfaces, one sweep

- **authored** — files written directly: `docs/`, `README.md`, `AGENTS.md`, `pcb/fab.json`, `sim/`, `firmware/`, `pcb/tools/*.cs` (`design.cs`, generators, templates' project copies, tests). Reviewed **as a diff**, pasted into the shard prompt.
- **boards** — generated artifacts, per board (one indivisible unit): its `.kicad_sch`, `.kicad_pcb`, `.kicad_pro` (net classes, design rules), `pcb/lib/` when it changed (the footprints the datasheet check judges live there), the `pcb/schematics/` PDF and `pcb/renders/` images (netlist too, if the project keeps one). Reviewed **as files, never as diff hunks** — routing churn and zone fills swamp a diff. Targeted Grep/Read on the s-expression text; renders read visually, the look a human does in KiCad before ordering.

The mechanical gates (ERC, pin parity, DRC, fabcheck, tool tests) are the floor the pipeline already holds; the sweep spends reviewers on what gates cannot see — datasheet conformance, docs↔design↔board consistency, visual/geometric judgment, intent. **A condition the gates check and pass is not a finding; a check the gates LACK is.** A change touching only one surface simply exercises its half.

## Step 1 — Scope

1. **Branch and base.** Current HEAD unless the invocation names a branch (a named branch is checked out in item 2, before anything is measured). Base: the invocation's ref, else `git merge-base main HEAD`. No `main` → stop, ask for the base. "Nothing to review" only when `git diff <base>` is empty after item 2.
2. **Working tree as found.** A named branch that differs from the checkout is switched to here, before anything is measured — a dirty tree makes that switch a question, never a force. Never ask for a commit or stash — the diff already includes the tree. Snapshot `git status --porcelain` (rides to the final report). Untracked files: list them, ask which belong in; the chosen are intent-added file by file (`git add -N <file>`; never `git add -N .`); build outputs (`pcb/build/`, `pcb/gerbers/`, `pcb/<board>-backups/`, `obj/`, `bin/`) are never offered in. Merge/rebase in progress or unmerged paths → stop, ask.
3. **Review set.** `git diff --name-only <base>`, split by surface (genuinely ambiguous → authored; the stricter check list wins). A board is "changed" when any of its files changed.
4. **Measure** — authored diff lines **+ 400 per changed board** (a board carries visual, geometric and datasheet review that changed lines don't capture):

   ```bash
   # Bash
   git diff <base> -- docs README.md AGENTS.md pcb/fab.json sim firmware pcb/tools | wc -l
   ```
   ```powershell
   # Windows (PowerShell) — same measure
   git diff <base> -- docs README.md AGENTS.md pcb/fab.json sim firmware pcb/tools | Measure-Object -Line
   ```

5. **Anchor.** The invocation's intent statement; else the branch's commit messages (`git log <base>..HEAD`, pasted into every shard prompt) — alongside `docs/design.md`, the standing requirements record, which every shard also receives as read context either way. Neither intent nor commits → `docs/design.md` alone anchors divergence and coverage; say so in the report.
6. **Gate baseline.** Run and record once, before any spawn: `dotnet test pcb/tools/tests` and the project's `verify.cs`; `fabcheck.cs` when `pcb/fab.json` exists; read any existing DRC reports in `pcb/build/` (route nothing). Pre-existing red is recorded and stands — the baseline is what Step 4's no-new-red gate measures against.
7. **AGENTS.md note.** If the project's `AGENTS.md` carries no `hw-review` bullet (match on the skill name, not the exact text), add this bullet under its rules (create no file; a project without AGENTS.md is named in the report instead):

   ```markdown
   - **Reviews (docs, schematics, boards) run as the pcb-as-code `hw-review` sweep:** waves of read-only reviewer agents, triaged by the session; confirmed fixes land in `design.cs`, a generator or the docs, and the boards are regenerated (never hand-edited) with the gates re-run before the next wave. Run it before a fab order and after any large change.
   ```

## Step 2 — Budget and wave cut

**Budget `N` = total reviewers for the whole run.** The invocation's number wins in both directions — a user-chosen budget is not a defect. Before a fab order, ≥ 6 is the honest default. Otherwise the measure sets it:

| Measure (diff lines + 400/board) | Budget |
|---|---|
| ≤ 300 | 1 |
| 301 – 800 | 2 |
| 801 – 1500 | 3 |
| 1501 – 2500 | 4 |
| 2501 – 4000 | 5 |
| 4001 – 6000 | 6 |
| 6001 – 9000 | 7 |
| > 9000 | 8 |

Ceiling: **at most 6 reviewers at once**; cut the budget into waves of ≤ ceiling, remainder last (8 → 6+2). Shard units: whole authored files (contiguous, size-balanced — docs with docs, `design.cs` with the generators it feeds) and each changed board as one unit, never split mid-board. When units cover the wave and K > 1, the last shard is the **seam shard** — no unit of its own; it owns the cross-surface contracts: net names shared across boards really shared at both ends, `pinmap.md` vs `design.cs`, `wiring.md` vs ribbons/harnesses, `bom.md` vs the design's parts (off-board included), README's regeneration list vs the tools that exist, `AGENTS.md`'s specifics vs what `verify.cs` checks. Fewer units than reviewers → units deliberately overlap, each reviewer given a different lens (one datasheet-leaning, one render-leaning); the seam role lapses — with no disjoint units, cross-surface questions are every reviewer's own. The budget's total is never reduced; only each wave's shape bends.

Announce one line before spawning: budget, wave cut, ceiling, anchor, reviewer identity. No mid-run budget changes — fixes shift the measure, the budget was set against the branch as found.

## Step 3 — Run the waves

Per wave, in order:

1. **Cut the shards** against the branch's *current* state: recompute `git diff --name-only <base>` (after `git add -N` on the sweep's own new source files — never `git add -N .`), split per Step 2's units.
2. **Spawn the reviewers in parallel** — one message, one Agent call per shard. **ZCode:** each `run_in_background: true`, collected against the wall clock (§The wall clock). **Claude Code:** foreground, all calls in one message; the effort budget (maxTurns 400, harness-enforced) is the bound, no wall clock.
3. **Merge mechanically** — never triage at the merge: dedupe (same file:line + same failure; in doubt, keep both), concatenate in shard order with each shard's ranking preserved, renumber `F1..Fn`, severity grades untouched, cut nothing (the per-shard cap of 8 is the only cap). On ZCode a wall-clocked shard's wrap-up output counts as its report. Empty/malformed report → one re-run, same prompt; errored spawn → one re-spawn; still nothing usable → named in the final report with its slice.
4. **Triage** — evidence rule first; verify every survivor against the source (datasheet findings: against the datasheet yourself before confirming); one explicit verdict per finding — `fix` / `dismiss` / `defer` — confirming or correcting the grade (the reviewer grades impact, the triage owns the grade). Dedupe vs the wave log: a re-report **with new evidence** re-opens; an identical one drops.
5. **Apply the fixes** (Step 4) — all of the wave's `fix` verdicts, then **one** regeneration pass, then the gates.
6. **Log the wave**; the next wave reviews the updated branch.

### The spawn identity (both harnesses)

Agent tool, one of the plugin's **reviewer twins** — identical bodies and tool restrictions, one declaration per harness's cheap model; the declaration fixes model and read-only posture, the spawn sets no model key:

- **ZCode:** `subagent_type: pcb-as-code:hw-reviewer-glm`
- **Claude Code:** `subagent_type: pcb-as-code:hw-reviewer-claude`

No shell on either harness: hand each shard everything — the authored diff pasted, boards and renders named by absolute path. A rejected spawn strands the wave: end the sweep, name the wave and its slice in the final report.

### The wall clock (ZCode only — Claude Code spawns are foreground; skip this section there)

Deadline: **35 minutes from the spawn**, per shard — it stops runaway shards, never paces healthy ones. Collect in shard order, blocking with TaskOutput (one wait caps at 10 min; re-block to the deadline; a shard reached with its deadline already past is judged on arrival). Still running at its deadline → **TaskStop + exactly one SendMessage wrap-up** ("do not investigate; emit the `REVIEW:` line from what you have"); its output is the report; still nothing usable → the empty-report re-run rule. The deadline is never extended.

### The shard prompt (stamped copy)

Adapt the bracketed bits; at K = 1 drop the slice/seam sentences.

```
You are shard <k> of <K> in wave <w> of a STRICTLY READ-ONLY, static review of a pcb-as-code hardware project. No creating, editing or deleting files; no building, compiling, routing or testing anything (no dotnet, no kicad-cli, no scripts). You have no shell: the material is pasted below or named by absolute path — read files with Read/Grep (big s-expression files via targeted Grep, never whole-file dumps; render images with Read, visually). Fetching a part's datasheet with WebFetch/WebSearch is allowed and expected where a check calls for it.

Effort budget ≈ 400 rounds. Consolidate findings as you go; near the budget, stop investigating and emit. If stopped and messaged to wrap up, the close-out is emission only — report what you have.

<At K > 1: Your owned surface for findings: <the authored files this shard owns, and/or: board <name> — its pcb/<name>.kicad_sch, pcb/<name>.kicad_pcb and pcb/renders/<name>* images, reviewed as files>. — when reviewers outnumber the units: Your owned surface: <the unit> — reviewed through the lens of <datasheet conformance | renders and mechanics | docs consistency>; the other overlapping shards carry the other lenses. — seam shard (disjoint units only): You own the seams, no unit of your own: do the cross-surface contracts still match — net names shared across boards shared at both ends, pinmap.md vs design.cs, wiring.md vs the ribbons, bom.md vs the parts, README's regen list vs the tools that exist, AGENTS.md's specifics vs verify.cs?>

Anchor (the intent the change is measured against): <paste the intent statement, else the branch's commit messages; docs/design.md at <path> is ALWAYS also read context> <or: "No anchor: divergence and coverage are skipped." — run only the datasheet, bug, regression, contradiction and gap checks.>

Change under review — the authored surface's diff against <base> as of wave <w> (earlier waves' fixes included), pasted below. Generated board files are NOT pasted — review them as files, as named above.
<paste `git diff <base> -- <this shard's authored files>`; a seam shard gets the file list with per-file change stats only>
Do NOT stage or commit anything.

<Wave > 1 only: This is wave <w> of a multi-wave sweep; earlier waves' confirmed findings are fixed. The wave log lists every finding recorded so far with its verdict — re-report only with NEW evidence; an identical re-report is noise:
<paste the wave log>>

Check, in priority order (your surface's list first; the rest is context):
- Datasheet — the top check: parity gates cannot catch a consistent-but-wrong map. design.cs pin→net vs the part's datasheet (fetch it); a pin the datasheet requires tied, pulled or decoupled that is not; a supply pin with no decoupling on its net; a logic-level/tolerance violation; a footprint/package that does not match the ordered part. Cite the design.cs line AND the datasheet pin number with its own wording.
- Divergence — departs from the anchor: built differently than design.md/intent describes, parts/nets/rules the intent does not cover, an approach it ruled out. Cite the intent text.
- Coverage — a requirement the anchor states (design.md's decisions, the interrogation record) that design.cs or the docs do not implement.
- Regression — existing behavior in touched files this change breaks (a renamed net silently dropping a cross-board link, a removed part docs still reference).
- Bug — a defect in changed authored content: generator/tool code that misbehaves, or a document that cannot be followed as written (broken reference, wrong path, failing command). Concrete trigger + expected-vs-actual.
- Contradiction — changed docs vs the design, the boards or another doc: pinmap.md vs design.cs pin maps, wiring.md vs shared net names, bom.md vs parts, fab.md vs pcb/fab.json, assembly.md vs what exists. Cite BOTH locations.
- Gap / undefined term — a changed document naming a net, part, tool or rule without the detail its reader needs, or that the project never defines.
- Mechanical/enclosure (boards) — placement vs the enclosure constraints in docs/assembly.md and AGENTS.md: slot clearances, legends outside, mounting holes and screw bonding, connector keying. verify.cs's construction rules are the floor; report what they do not cover.
- Visual (boards) — the renders: silk over pads or holes, legend orientation and readability, parts colliding or overhanging the outline, LED/slot alignment, connector orientation.
- Copper judgment (boards) — stitching fences vs design.md's planned return paths, zone/thermal coverage on power pads, locked generator copper present where the design says it should be.
- Comment noise — a comment whose content the code already states: a signature restated, step numbers, `// was previously...`, commented-out old code; quote the redundant clause AND the declaration line. NEVER a finding: design.cs why-comments (drive levels, tolerance reasoning, pin rationale — the workflow requires them) and any comment carrying a constraint, units, ownership, a gotcha, or why the obvious alternative fails — correct at any length. Check AGENTS.md (or a CLAUDE.md forwarding to it) for a comment convention first.

Evidence rule: every finding names a file:line (or document §ref, or render image + region); a datasheet finding cites the design.cs line AND the datasheet pin/wording; divergence/coverage/regression cites the anchor text; a bug gives trigger + expected-vs-actual; a contradiction cites both locations. A condition the project's gates check and pass (ERC, pin parity, DRC, fabcheck) is not a finding — a check the gates LACK is. Out of scope: style, "could be cleaner", circuit redesign (topology is the designer's — report only violations of a datasheet requirement, the project's own documents, or the intent).

Grade every finding's impact — metadata for the triager, never a verdict:
- critical — the board would be wrong as built: a datasheet violation that risks a part or a function, a wiring error no gate can see, an enclosure conflict that blocks assembly. Caught before the fab order, it saves the re-spin.
- high — misleads: a doc contradicting the built design, a wrong pinmap entry, an instruction that sends assembly or bring-up the wrong way.
- medium — stale or incomplete but reconcilable nearby: a drifted count, a missing entry, a wrong name.
- low — polish: silk wording, comment noise, doc wording.

Report at most 8 findings, highest grade first. Output ONE ' ;; '-separated line, exactly this format and nothing else: REVIEW: hw wave <w> shard <k/K> — <clean | m findings> ;; F1 [<check>|<level>] <evidence> <file:line, §ref or render> — one-line description ;; F2 ... where <check> is one of datasheet|divergence|coverage|regression|bug|contradiction|gap|mechanical|visual|copper|comment-noise and <level> is one of critical|high|medium|low — exactly one of each per finding (e.g. [datasheet|critical]); a clean shard returns exactly the header. Do NOT triage, rank, or decide what to fix. Do NOT edit anything.
```

## Step 4 — Apply the fixes (source → regenerate → gates)

All of the wave's `fix` verdicts first, then **one** regeneration pass, then the gates — batching matters: a `design.cs` fix that moves parts re-routes a board for ~40 minutes; three fixes route once.

- **A generated artifact is never hand-edited** — the workflow's own hard rule. Every board finding translates to a source fix: `design.cs` (a part, net, pin map, footprint), the generator (placement, drawing, copper), the project rules (`verify.cs`'s construction checks), or the docs. Naming the file each fix lands in is the fix.
- **docs / fab.json / firmware / sim** → edit in place; firmware fixes run their native host tests.
- **design.cs or a generator** → edit, then regenerate per README's command list, restricted to the steps downstream of the change. A change that moves **no** part or pad reuses the committed copper (`route.cs <board> --from HEAD` where the project has it; else copper-copy by net name per `pipeline-steps.md` step 5). A change that moves parts or pads re-routes with the retry recipe (DSN shuffle per attempt, ≤ 8 attempts).
- **Verify** against the Step 1 baseline — **no new red**: `dotnet test pcb/tools/tests`, then per affected board ERC 0/0 and `TOTAL PROBLEMS: 0` (`verify.cs`), DRC 0 errors / 0 unrouted, `FAB PROBLEMS: 0` (`fabcheck.cs`). Baseline red neither blocks nor gets chased; new red does — not green, not done.
- **A fix whose re-route cannot go clean** within the retry budget is **reverted** (source edit undone, board regenerated back), recorded `defer — route blocked`, handed to the user. The sweep never leaves a board red and never waives a gate to keep a fix.

Then stop — **nothing is committed**. Fixes stay in the working tree (the sweep's own new source files intent-added so later waves count them); every later wave's surface is `git diff <base>`, which keeps the previous wave's fixes under review. The user reviews, commits or discards at the end.

⛔ **No per-wave approval gate:** every fix traces to a cited finding, nothing commits, and the wave log plus the final report are the audit trail. A `defer` never blocks the budget — recorded, handed over at the end.

⛔ **Rubber-stamping every finding is as wrong as dismissing them all.** A high-recall cheap reviewer makes noise by design — especially datasheet claims (wrong part variant fetched, a typical-application circuit mistaken for a requirement). Verify before confirming; the evidence rule is the discriminator, the session's judgment is why triage exists.

## The wave log

One line per finding ever recorded, appended at each wave's triage:

```
wave <w> — F<n> [<check>|<level>] <file:line, §ref or render> — <one-line description> — <verdict> — <one-line reason>
```

The description lets the next wave tell an identical re-report from a genuinely new failure at the same spot. The severity summary reads each finding's **latest line only** — a re-opened finding's earlier line is history. Keep the log in the conversation **and** mirrored to a scratch file outside the repository (announce the path; the temp dir is fine): a long sweep can outlive a compaction, and the log is the only memory later waves' dedupe and the final report trust. Never committed, never in the repository.

## Step 5 — The end and the report

The sweep ends when the last budgeted reviewer has reviewed. A wave whose findings all die in triage does not end the run — the remaining waves still run; on hardware the later waves are where the last issues have proven to surface, and fresh eyes on a changed board catch what the fix itself broke.

Final report, in this order:

- **The severity summary — the run's headline, first, in this exact shape.** One row per level, counts from each finding's latest wave-log line only:

  | Level | Fixed this run | Dismissed | Still open |
  |---|---|---|---|
  | Critical | n | n | n |
  | High | n | n | n |
  | Medium | n | n | n |
  | Low | n | n | n |

  ("Still open" = deferred and route-blocked findings — everything not fixed and not dismissed.) Then the verdict, on its own line, unmissable:

  > **ANOTHER SWEEP: YES** — <each critical/high still open, one line each, with its question> — do not order boards before it closes.

  > **ANOTHER SWEEP: NO** — everything critical/high is fixed; what remains is medium/low polish.

  The rule: **any critical or high still open says YES** — doubly so with a fab order pending; only medium/low remaining says NO. Every still-open finding is listed under the verdict with its one-line question (a route-blocked fix names the board and the placement conflict) — the user's to answer. A new sweep takes the branch as it now stands, this run's fixes included.
- **The plan as executed:** budget, wave cut, ceiling, wall clock (ZCode), anchor, reviewer identity (the harness's twin throughout).
- **Per wave:** reviewers, findings, verdict counts; for each wave that regenerated — which boards, and any re-route with its duration.
- **The working tree:** every fix is uncommitted — the tree is the user's to review, commit, or discard. Name the files Step 1's snapshot showed already dirty, the untracked files left out of scope, and the intent-added files (user-chosen plus sweep-owned) with their undo, `git reset -- <file>`. If the boards are to be ordered, re-run export so the gerbers match the fixed boards.
- **Shards that never produced a usable report** — re-run and re-spawn both failed, or a wave stranded by a rejected spawn — named with their surfaces, so the report never implies coverage that did not happen.

⛔ **Severity grades the exit — it never ends the run early or refunds the budget.** The report closes with the ANOTHER SWEEP verdict; the verdict launches a fresh run (a fresh budget on the branch as it stands) and never extends this one; acting on it is the user's. The run itself always ends when the budget is spent.

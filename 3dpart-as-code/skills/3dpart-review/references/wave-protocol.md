# Part review waves — the printed-part branch-sweep playbook

A **reviewer budget** — a fixed total of cheap read-only reviewer agents — cut into **waves** of at most 6; each wave reviews the branch's current state, the session triages its findings, the confirmed fixes land **in the source** (`part.cs`, a geometry script, the tooling, docs) and the parts are **rebuilt** — all uncommitted, in the working tree; the next wave reviews the updated branch. **The run ends only when the budget is spent** — a clean wave does not refund it.

## Two surfaces, one sweep

- **authored** — files written directly: `docs/` (`mech.md` first), `README.md`, `AGENTS.md`, `mech/tools/*.cs` (`part.cs`, the libraries, the entry tools, tests), `mech/tools/pac.py`, `mech/parts/*.py`, `mech/requirements.txt`. Reviewed **as a diff**.
- **parts** — generated artifacts, per printed body (one indivisible unit): its `mech/build/renders/<body>-*.png` and its lines in `build/probe-*.json`, plus the shared `assembly-*.png` renders and `build/fit.json`. Reviewed **as files** — images read visually, JSON read for the numbers. Never the STL or STEP bytes.

The gates (`BUILD`, `DIM`, `MESH`, `FIT` at 0, tool tests) are the floor; the sweep spends reviewers on what they cannot see. **A condition a gate checks and passes is not a finding; a check the gates LACK is** — a mate with no clearance check, a critical number with no probe, an insertion path with no sweep.

## Step 1 — Scope

1. **Branch and base.** Current HEAD unless the invocation names a branch (checked out here, before anything is measured). Base: the invocation's ref, else `git merge-base main HEAD`. No `main` → stop, ask for the base. "Nothing to review" only when `git diff <base>` is empty after this step.
2. **Working tree as found.** A dirty tree makes a branch switch a question, never a force. Never ask for a commit or stash. Snapshot `git status --porcelain` (rides to the final report). Untracked files: list them, ask which belong in; the chosen are intent-added file by file (`git add -N <file>`; never `git add -N .`); `mech/build/`, `mech/.venv/`, `bin/`, `obj/` are never offered in. Merge/rebase in progress → stop, ask.
3. **Build first.** Run the chain once — `build.cs`, `check.cs`, `fit.cs`, `render.cs` — so the renders and probes reviewed are the branch's own. Record the gate results as the **baseline**; pre-existing red stands and is reported, never chased.
4. **Review set.** `git diff --name-only <base>`, split by surface. A printed body is "changed" when its script, part.cs, or anything it reads changed — in practice every body when part.cs changed.
5. **Measure** — authored diff lines **+ 300 per changed printed body**:
   ```bash
   git diff <base> -- docs README.md AGENTS.md mech/tools mech/parts mech/requirements.txt | wc -l
   ```
6. **Anchor.** The invocation's intent; else the branch's commit messages; `docs/mech.md` is read context for every shard either way. None of the three → no anchor: divergence and coverage are skipped; say so in the report.
7. **Scratch directory** outside the repository (the session's scratchpad); announce its absolute path. It holds the wave log and each wave's diff files.
8. **AGENTS.md note.** If the project's `AGENTS.md` names no `3dpart-review` (match on the skill name), add the bullet from `${CLAUDE_PLUGIN_ROOT}/skills/3dpart-as-code/references/project-structure.md` under its printed-parts section.

## Step 2 — Budget and wave cut

**Budget `N` = total reviewers for the run.** The invocation's number wins. Before a long print (hours of filament, many parts) ≥ 4 is the honest default. Otherwise:

| Measure (diff lines + 300/body) | ≤ 300 | 301–800 | 801–1500 | 1501–2500 | 2501–4000 | 4001–6000 | 6001–9000 | > 9000 |
|---|---|---|---|---|---|---|---|---|
| Budget | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |

At most **6 at once**; waves of ≤ 6, remainder last. Shard units: whole authored files (part.cs with the scripts it feeds; docs with docs) and each changed printed body (its renders and probes). When units cover the wave and K > 1, the last shard is the **seam shard**: part.cs ↔ scripts (every parameter used, no literal dimension in a script — probe-window pads aside), part.cs ↔ docs/mech.md (fits, materials, hardware, sheet), every mate in docs ↔ a clearance or sweep in part.cs, README's commands ↔ the tools. Fewer units than reviewers → units overlap, each reviewer given a lens (**dimensions and datasheets** / **printability and strength** / **renders and assembly** / **docs consistency**); the seam role lapses.

Announce one line before spawning: budget, wave cut, anchor, reviewer identity, scratch directory.

## Step 3 — Run the waves

Per wave, in order:

1. **Cut the shards** against the branch's current state.
2. **Write each shard's material to the scratch directory** with the shell, never into a prompt: `git diff <base> -- <shard's files> > <scratch>/w<w>-s<k>.diff` (seam: `git diff --stat`; a parts-only shard: no diff file). The wave log is `<scratch>/wave-log.txt`.
3. **Spawn the reviewers in parallel** — one message, one Agent call per shard, the stamped prompt below. ZCode: `run_in_background: true`, collected per [zcode-wall-clock.md](zcode-wall-clock.md). Claude Code: foreground.
4. **Merge mechanically** — dedupe (same file:line + same failure), concatenate in shard order, renumber `F1..Fn`, grades untouched. Empty/malformed report → one re-run; still nothing → named in the final report.
5. **Triage** — evidence rule first; verify every survivor yourself (re-measure the render, re-read the probe, fetch the datasheet); one verdict per finding — `fix` / `dismiss` / `defer` — confirming or correcting the grade. Dedupe vs the wave log: a re-report with new evidence re-opens; an identical one drops.
6. **Apply the fixes** (Step 4) — all of the wave's `fix` verdicts, one rebuild, the gates.
7. **Log the wave.**

**The spawn identity** — the plugin's reviewer twins, identical bodies, one per harness's cheap model; the spawn sets no model key:
- **ZCode:** `subagent_type: 3dpart-as-code:part-reviewer-glm`
- **Claude Code:** `subagent_type: 3dpart-as-code:part-reviewer-claude`

**The shard prompt** (variable part only — the agent body carries the rest):

```
Wave <w>, shard <k>/<K>. Project root: <abs path>.
Surface: <authored files owned | body <name>: mech/build/renders/<name>-*.png, its probes in mech/build/probe-*.json | lens <dimensions and datasheets | printability and strength | renders and assembly | docs consistency> on <unit> | SEAM: cross-surface contracts>.
Diff vs <base> (earlier waves' fixes included): <scratch>/w<w>-s<k>.diff
Anchor: <intent | "commit messages:" + one-liners | "docs/mech.md alone" | "No anchor: skip divergence and coverage.">; docs/mech.md is read context.
Gate baseline: <BUILD/DIM/MESH/FIT counts>.
Wave log (wave > 1): <scratch>/wave-log.txt
```

## Step 4 — Apply the fixes (source → rebuild → gates)

- **A generated file is never edited.** A finding on a render or probe translates to `part.cs` (a number, a fit, a check, an orientation), a script (geometry, a probe), or the docs.
- Then **one** rebuild: `build.cs`, `check.cs`, `fit.cs`, `render.cs`; and `dotnet test mech/tools/tests` if tooling changed.
- **No new red** against the baseline. A fix that cannot go green is reverted and recorded `defer — gate blocked`.
- Nothing is committed; the sweep's new source files are intent-added so later waves see them.

⛔ Rubber-stamping every finding is as wrong as dismissing them all. Cheap reviewers over-report printability opinions and misread renders (an iso's foreshortening is not a wrong length) — verify against the numbers.

## The wave log

```
wave <w> — F<n> [<check>|<level>] <file:line, §ref or render> — <one-line description> — <verdict> — <one-line reason>
```

Kept in the conversation and in `<scratch>/wave-log.txt`. The severity summary reads each finding's latest line.

## Step 5 — The end and the report

The sweep ends when the last budgeted reviewer has reviewed.

- **Severity summary first:**

  | Level | Fixed this run | Dismissed | Still open |
  |---|---|---|---|
  | Critical | n | n | n |
  | High | n | n | n |
  | Medium | n | n | n |
  | Low | n | n | n |

  Then the verdict on its own line: **ANOTHER SWEEP: YES** — each critical/high still open with its question — do not print the parts before it closes; or **ANOTHER SWEEP: NO** — what remains is medium/low polish. Any critical or high still open says YES.
- **The plan as executed:** budget, wave cut, anchor, reviewer identity.
- **Per wave:** reviewers, findings, verdict counts, rebuilds.
- **The working tree:** every fix uncommitted; the files already dirty at Step 1; intent-added files with their undo (`git reset -- <file>`); re-run `export.cs` so `out/` matches the fixed parts.
- **Shards that never produced a usable report**, with their surfaces.

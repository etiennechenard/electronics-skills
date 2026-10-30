# Project structure

## Layout

```
<project>/
├── AGENTS.md              thin orientation file for coding agents (below)
├── README.md              what it is + the exact regeneration command list
├── docs/mech.md           interrogation record, bodies, fits, hardware, sheet
├── pcb/                   (a pcb-as-code project, when there is one)
└── mech/
    ├── requirements.txt   the pinned kernel (build123d)
    ├── .venv/             created by build.cs (ignored)
    ├── tools/             part.cs (the model) + the skill's templates + pac.py
    │   └── tests/         the copied xUnit suite; tests/example/ = the fixture
    ├── parts/             the geometry scripts, *.py
    ├── build/             params.json, STEP/STL, probes, fit.json, renders (ignored)
    └── out/               the deliverables: STL, STEP, renders, manifest (committed)
```

A part-only repository is the same without `pcb/`.

## AGENTS.md — orienting any agent (thin, on purpose)

Every coding agent reads `AGENTS.md` unasked. Keep it a pointer to the
workflow plus the project's specifics; the workflow itself is documented
once, in the skill. In a repository that already has one (a pcb-as-code
project), append the section instead of replacing it.

```markdown
## Printed parts (mech/)

This project's printed parts follow the **3dpart-as-code** workflow (a Claude Code / ZCode skill): the parts live in code and the STL/STEP files are generated build outputs.

- `mech/tools/part.cs` is the single source of truth for every dimension, fit, body and check. The geometry scripts in `mech/parts/` hold no numbers. Never edit `mech/build/` or `mech/out/` by hand, and never keep a change made in CAD: change part.cs or a script and rebuild.
- Every stage gates, from the project root: `dotnet run mech/tools/build.cs` (BUILD PROBLEMS: 0), `check.cs` (DIM and MESH PROBLEMS: 0), `fit.cs` (FIT PROBLEMS: 0), `render.cs`, then `export.cs`, which refuses stale or dirty builds.
- Run `dotnet test mech/tools/tests` after touching tooling (set `PAC_PYTHON` to `mech/.venv/Scripts/python.exe` for the live pipeline tests).
- Record print-relevant decisions (material, orientation, hardware, tuned fits, coupon results) in `docs/mech.md`.
- **Reviews run as the 3dpart-as-code `3dpart-review` sweep:** waves of read-only reviewer agents, triaged by the session; confirmed fixes land in part.cs, a script or the docs, then rebuild and re-run the gates. Run it before a long print and after any large change.
```

(A one-line `CLAUDE.md` containing `@AGENTS.md` forwards older tools to it.)

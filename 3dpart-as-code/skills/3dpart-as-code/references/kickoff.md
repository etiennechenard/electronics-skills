# Step 0 — kickoff: interrogate, structure, document

A part starts with a short interrogation, then a mechanical scaffold —
before any geometry. The scaffold may land in an existing repository (a
pcb-as-code project gets a `mech/` beside its `pcb/`) or a fresh one.

## Interrogate

Ask the user, record the answers in `docs/mech.md`. Pin down, in this order:

1. **Mode.** Design (a new part) or reproduce (a copy of a part that
   exists). Reproduce → also read `reproduce-mode.md` now: the maker's
   drawing or STEP is looked for before anything is measured.
2. **Function.** What the part does, what holds it, what it holds; loads
   (static, impact, a screw torque, a snap that flexes); which surfaces
   matter (mating, sealing, visible) and which are free.
3. **Mates.** Everything it touches: boards (pcb-as-code →
   `pcb-bridge.md`), screws and inserts (size, type), other printed parts,
   bought parts with a datasheet drawing. Each mate becomes a fit or a
   reference body.
4. **Environment.** Temperature (a 13 W box, a car dashboard, outdoors),
   UV, solvents, food contact. It decides the material class (PLA softens
   around 55 °C, PETG around 80 °C, ASA/ABS around 95–100 °C) — recorded as a
   note in docs/mech.md, never as a slicer setting.
5. **Printer envelope.** The largest part the user's printer takes (x × y ×
   z, mm). It goes into part.cs as `Envelope`; a body larger than it is
   split in the design, with a joint.
6. **Hardware.** Screws, heat-set inserts, magnets, bearings the design
   uses — each one's datasheet dimensions as `Datasheet` parameters.
7. **Assembly.** How the pieces go together and in which order: every
   insertion path becomes a sweep check.

## Structure

Create the layout (see `project-structure.md`), then:

- **Copy the templates and the suite.** The skill's base directory (the
  folder holding SKILL.md) has `templates/` and `tests/` side by side:
  - `templates/*.cs` and `templates/pac.py` → `mech/tools/`;
  - `templates/parts/bracket.py` → `mech/parts/`;
  - `templates/requirements.txt` → `mech/`;
  - `tests/` → `mech/tools/tests/` (without its `bin/` and `obj/`);
  - the worked example a second time, as the pipeline test's fixture:
    `templates/part.cs` and `templates/parts/bracket.py` →
    `mech/tools/tests/example/` (the csproj never compiles that folder).
- **Rewrite the csproj for the project's layout.** It compiles the eight
  libraries by relative path, `..\templates\<name>.cs` in the plugin; in
  `mech/tools/tests/` they become `..\partmodel.cs`, `..\part.cs`, and so
  on. Never a bare `..\*.cs` glob: it would also compile the entry files'
  top-level statements.
- **Ignore the generated and local files:** `mech/.venv/`, `mech/build/`,
  `mech/params.xlsx`, `mech/~$params.xlsx`, `mech/params.tmp.xlsx`, `mech/tools/tests/bin/`, `mech/tools/tests/obj/`. `mech/out/` IS committed
  — it is what gets printed.
- **Prove the copy** from the project root:
  1. `dotnet run mech/tools/smoke.cs` → `smoke ok`;
  2. `dotnet run mech/tools/build.cs` (creates `mech/.venv` and installs the
     pinned kernel on first run, a few minutes), then `check.cs`, `fit.cs`,
     `render.cs`, `sheet.cs`, `export.cs` on the example — every gate at 0 (`sheet.cs` and `export.cs` download the ClosedXML and Roslyn packages on their first run);
  3. `dotnet test mech/tools/tests` with `PAC_PYTHON` set to
     `mech/.venv/Scripts/python.exe` (Windows) so the pipeline tests run
     live rather than early-returning.
- **Replace the example.** Write the project's own `part.cs` (step 1) and
  scripts; delete `mech/parts/bracket.py` and `mech/out/`. The fixture in
  `tests/example/` stays: it keeps the pipeline tests independent of the
  project's model.
- **Scaffold `AGENTS.md`** (thin — see `project-structure.md`; append a
  section if the repository already has one) and a README regeneration
  list.

## Deploy the documentation

`docs/mech.md` starts as the interrogation record and grows with the part:

| Section | Holds | Grown by |
|---|---|---|
| Purpose | function, loads, the surfaces that matter | 0 |
| Mates | each mate, its fit or reference body, and why | 0, 1, 4 |
| Material and environment | the class and the reason (temperature, UV…) | 0 |
| Bodies | each printed body, its print orientation and why | 1, 2 |
| Hardware | screws, inserts, magnets, with datasheet references | 0, 1 |
| Fits | the named fits' values, and the coupon results that tuned them | 1, 5 |
| Measurement sheet | reproduce mode: `Model.SheetMarkdown()` pasted after each measuring round | 1 |
| Assembly | the order, insertion paths, what to check on the first print | 4, 6 |

Then continue with step 1 (`step1-part.md`).

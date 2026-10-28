# Step 0 — kickoff: interrogate, structure, document

New projects start with a short interrogation, then a mechanical scaffold —
before any design work. (For a large build, a formal work-order/blueprint
flow can run first; this is the hardware-specific minimum.)

## Interrogate

Ask the user, record the answers in docs/design.md. Pin down, in this order:

1. Function: what does the unit do; channels/ports; accuracy; speed.
2. Interfaces: how it is controlled and what it talks to (USB-TMC, serial,
   GPIB, Ethernet, plain IO); is a command set needed?
3. Power: input source, per-rail budget, safe defaults on power loss.
4. Enclosure: which case, which panels become PCBs, connectors, mounting —
   get the physical case FIRST and check panel outlines against it.
5. Off-board parts: what hangs off the boards (switches, actuators) —
   net-map entries plus a BOM section, though never on the boards.
6. Fabrication: JLCPCB by default; which process (layers, copper, finish,
   mask colour: some limits depend on it); assembly wanted? SMD-only
   panels? impedance-controlled nets for RF?
7. Firmware: which MCU and interfaces; a host-testable protocol core?
8. Simulation: which sections are behavior-critical enough to SPICE before
   the design freezes (analog paths, power, precision references, and the
   power-up sequencing / timing of digital sections)?

## Structure

Create the repository layout (see `project-structure.md`), then:

- **Copy the templates and the suite.** The skill's base directory (the
  folder holding SKILL.md, shown as "Base directory for this skill" when
  the skill loads) has `templates/` and `tests/` side by side:
  - copy `templates/*.cs` into `pcb/tools/`;
  - copy `tests/` into `pcb/tools/tests/`.
- **Rewrite the csproj for the project's layout.** The copied suite's
  csproj compiles the libraries by relative path: seven explicit
  `..\templates\<name>.cs` `Compile Include` entries, the plugin's own
  layout. With the templates flat in `pcb/tools/`, they become
  `..\sexpr.cs`, `..\design.cs`, and so on. Never use a bare `..\*.cs`
  glob: it would also compile the entry files' top-level statements
  (smoke.cs, fabcheck.cs and the project's tools).
- **Prove the copy.** From the project root, run
  `dotnet run pcb/tools/smoke.cs` (the `#:include`s resolve relative to
  the entry file), then `dotnet test pcb/tools/tests`. FileBasedAppTests
  finds smoke.cs and fabcheck.cs in both layouts, and fails when it finds
  neither.
- **Write the `design.cs` skeleton:**
  - the `Boards` registry, with one placeholder board per PCB from (4);
  - power nets and `Flag` parts.

  DesignTests' invariants run over every board in `Boards`, so they keep
  testing the real design once the example board is replaced. They also
  hold a placeholder to them: every board needs pins, and every power
  flag a real pin on its net, so a board of flags alone is red until its
  first parts land in step 1 (keep the example board in `Boards` until
  then if the suite must stay green). Add the
  project's own invariants as it grows: cable ends that agree, net maps of
  off-board parts.
- **Fetch the fab's limits.** Fetch the chosen fab's capability page
  (WebFetch) and write `pcb/fab.json` from it, every value with the page's
  verbatim quote (`fabrication.md`). Until boards exist,
  `dotnet run pcb/tools/fabcheck.cs -- pcb/fab.json` checks the file
  alone: every key, a quote per value, and the check date.
- scaffold `AGENTS.md` (thin — see `project-structure.md`) and `README.md`
  (what the unit is; the regeneration command list starts empty and grows
  per step).

## Deploy the documentation

`docs/` skeletons with the answers already filled in; each file is grown
by the step that owns it:

| File | Starts as | Grown by |
|---|---|---|
| design.md | the interrogation record: function, interfaces, power, decisions and WHY | 1-4 |
| fab.md | chosen fab and process, why the project's rules sit where they do, add-ons (the fab's numbers live in pcb/fab.json) | 0, 7 |
| wiring.md | inter-board links: ribbons, power leads, twist/grounding plan | 1 |
| pinmap.md | placeholder until the connectors exist | 1, verify |
| bom.md | off-board parts now (on-board parts are generated later) | 1, 7 |
| assembly.md | enclosure checks, build order, bring-up checks to fill in | 8 |
| routing-comparison.md | only created when FreeRouting versions are compared | 5 |

Then continue with step 1 (`pipeline-steps.md`).

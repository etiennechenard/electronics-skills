# Part library

Micro-Cap's model library is at `C:\MC12\LIBRARY`: ~180 `.lib` files (plain-text SPICE models from vendors — `ad.lib`, `irf.lib`, `DiodesInc.lib`, `cs.lib`, ...) plus `.mac` macro circuits (also text). `NOM.lib` is the master index that chains all libraries.

- **To use a library part in a netlist**: add `.LIB "NOM.LIB"` and reference the part by name. Verified working in batch mode:
  ```
  D1 1 2 1N4148
  .LIB "NOM.LIB"
  ```
  Micro-Cap resolves the model automatically. Alternatively `.LIB` a specific file.
- **To find a part / check its exact name**: grep the library for `.MODEL <name>` (primitive devices: diodes, BJTs, MOSFETs) or `.SUBCKT <name>` (op-amps, power MOSFETs, complex parts). Names must match exactly and vendors use suffixes (e.g. `irf540_IR`, `2N2222A`), so always grep before using a part name.
- **Subcircuit parts** are instantiated with `X` devices: `X1 out inp inn vcc vee LM741`. Pin order comes from the `.SUBCKT` header line — read it in the `.lib` file first.
- `.LBR` files and shape/component databases are binary — not readable; the text `.lib`/`.mac` files contain everything needed for simulation.

## Adding a new part (vendor SPICE model)

Two options, both verified:

1. **One-off use**: save the vendor `.lib`/`.mod`/`.txt` file anywhere and reference it directly in the netlist: `.LIB "C:\path\to\vendor.lib"`. No install changes.
2. **Permanent install**: copy the file into `C:\MC12\LIBRARY\` and append a registration line to `C:\MC12\LIBRARY\NOM.lib`:
   ```
   .lib "vendorfile.lib"
   ```
   The part is then found by name from any netlist containing `.LIB "NOM.LIB"` (and by the GUI's model auto-search). Micro-Cap reindexes automatically. Back up `NOM.lib` before editing; keep the entry format identical to existing lines.

Vendor models are ordinary SPICE text (`.MODEL` for primitives, `.SUBCKT` for complex parts). Before installing, sanity-check the file: confirm the model/subckt names don't collide with existing library names (grep first — earlier libraries in NOM.lib win on duplicates), and note that encrypted or PSpice-specific constructs (e.g. `.PARAM` tricks, behavioral ABM blocks) may need adaptation. Registering a part for GUI schematic placement (shape/pin assignment in the Component Editor) is GUI-only and cannot be done headlessly — but it is not needed for netlist simulation.

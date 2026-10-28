# Digital and mixed-signal simulation

Micro-Cap is a mixed-signal simulator: an event-driven digital engine runs
alongside SPICE, and A/D interfaces are inserted automatically wherever a
net crosses domains. The core facts below were verified against this
install — three live batch runs (a DFF divide-by-2, a library 7400, and
the power-up ramp) plus the Micro-Cap 12 help chapters; primitive
behaviors those runs did not exercise (JKFF/SRFF/DLTCH details, TRIST,
MNTYMXDLY) come from the help chapters. Items explicitly marked untested
(the schematic Stimulus Generator) are the exception.

## Digital primitives in a text netlist

PSpice-style `U` devices with a timing model and the standard I/O model:

```
* 2 MHz analog clock into a DFF wired as divide-by-2 (D = QBAR)
VCLK 1 0 PULSE(0 5 0 10N 10N 240N 500N)
VPRB 2 0 5
VCLB 3 0 5
U1 DFF(1) $G_DPWR $G_DGND 2 3 1 4 5 4 DLY_EFF IO_STD
RQ 5 0 10K
.MODEL DLY_EFF UEFF (TPCLKQLHTY=15NS TPCLKQHLTY=15NS)
.TRAN 1N 3U
.PRINT TRAN V(1) V(5) V(4)
.END
```

- General form: `U<name> <type>[(n)] $G_DPWR $G_DGND <nodes...> <timing model> IO_STD`.
  The power/ground pair is only used by the A/D interface circuits; the
  globals `$G_DPWR` / `$G_DGND` are conventional.
- Gate types: `INV BUF AND NAND OR NOR XOR XNOR` with `(n)` input counts,
  plus tristate `TRIST`. Flip-flops: `DFF(k) JKFF(k) SRFF(k) DLTCH(k)` —
  arrays (one `U` line, k bits, shared preset/clear/clock) work in text
  circuits. DFF clocks on the rising edge, JKFF on the falling; presetbar
  and clearbar are ACTIVE LOW.
- Node order for `DFF(1)`: `<presetb> <clearb> <clk> <D> <Q> <QBAR>`.
- Timing model classes: `UGATE` (gates: `TPLHTY/TPHLMX...`),
  `UEFF` (edge FFs: `TPCLKQLHTY`, `TPCLKQHLTY`, `TSUDCLKTY`...), `UIO` for
  I/O models. Every timing parameter has MN/TY/MX variants;
  `MNTYMXDLY=1..4` selects per device. Timing-constraint violations print
  as warnings into the `.TNO`.
- `IO_STD` is built-in — no `.LIB` needed for primitives.

## Analog ↔ digital

- An analog source can drive a digital input directly: the AtoD interface
  is inserted automatically (a `PULSE` source is a perfectly good clock).
  There is no netlist-usable DIGCLOCK in MC12, and the PSpice-style
  `$D_HI`/`$D_LO` constants are NOT documented for this version — do not
  rely on them. Tie an inactive active-low pin to a 5 V `V` source, as in
  the example above. (The schematic Stimulus Generator does not transfer
  to text netlists — untested; use analog sources.)
- A digital output driving an analog node gets a DtoA interface: load it
  with a resistor to ground and print the analog node. IO_STD drives
  ≈3.4 V high / ≈0.2 V low into 10 kΩ — interface levels, not rails.
- A pure digital node prints directly: `.PRINT TRAN V(4)` on a
  digital-only node outputs the logic state as a number (0/1).

## Digital parts from the library

`C:\MC12\LIBRARY\dig000.lib` … `dig874.lib`, `74lv.lib` carry the 74xx
families as plain-text `.SUBCKT`s with internal timing models, reachable
through `NOM.LIB`:

```
VL 1 0 PULSE(0 5 0 10N 10N 490N 1U)
VB 2 0 5
X1 1 2 3 7400
RY 3 0 10K
.LIB "NOM.LIB"
```

Pin order comes from the `.SUBCKT` line — grep the `dig*.lib` file first
(e.g. `.SUBCKT 7400 1A 1B 1Y`); families differ (`7400`, `74ALS00A`,
`74AC00`, ...), so pick the exact suffix.

## Power-up sequencing

Verified on this install with this circuit — the divide-by-2 circuit with
its clearbar tie replaced by the RC and the supply ramped from zero:

```
* supply ramp + RC-held clearbar (verified run)
VSUP 1 0 PWL(0 0 2U 5)
VCLK 2 0 PULSE(0 5 0 10N 10N 240N 500N)
VPRB 4 0 5
RCLB 1 3 1K
CCLB 3 0 10N
U1 DFF(1) $G_DPWR $G_DGND 4 3 2 5 6 5 DLY_EFF IO_STD
RQ 6 0 10K
.MODEL DLY_EFF UEFF (TPCLKQLHTY=15NS TPCLKQHLTY=15NS)
.TRAN 1N 6U
.PRINT TRAN V(1) V(3) V(6)
.END
```

Result: the FF's output stays cleared while the RC is below the logic
threshold and starts operating once the RC releases (release at ≈2.5 µs in
this circuit, RC node at ≈1.4–1.5 V — the interface's logic threshold) — no
`.IC` or `.UIC` needed for this pattern. Two caveats from the run: the
analog side of the RC node carries the AtoD interface's loading (≈0.85 V
offset at t=0), so judge the release by when the outputs start following
the clock; and size the RC from the release time you measure, not from τ
alone.

## Reference

The help file has the full digital chapters; to read them as HTML:
`hh.exe -decompile <outfolder> C:\MC12\mc12.chm` — see
`general_digital_primitives_format.htm`, `digital_gates.htm`,
`digital_flip_flops_and_latches.htm`, `digital_device_models.htm`,
`analog_digital_interface.htm`. The example circuits in `C:\MC12\DATA`
(`Counter.cir`, `CMOS_COUNTER.CIR`, `dig_power.cir`) are GUI schematic
files with the netlist embedded — useful to read, but schematic-only parts
(the Stimulus Generator) are assumed not to transfer to text netlists —
untested.

---
name: micro-cap
description: Run Micro-Cap 12 circuit simulations headlessly (batch mode) on this machine. Use whenever the user asks to simulate, analyze, or verify an electronic circuit, filter, amplifier, or SPICE netlist — AC/frequency response, transient, DC sweep, distortion, digital or mixed-signal logic (gates, flip-flops, 74xx families, power-up sequencing) — or mentions Micro-Cap, MC12, .cir files, or netlists.
---

# Micro-Cap 12 batch simulation

Micro-Cap 12 (64-bit) is installed at `C:\MC12\mc12_64.exe`. It is a GUI app, but it supports a batch mode that runs analyses and writes plain-text numeric output. All facts below were verified against this install unless the final section ("Documented but unverified") says otherwise.

## Critical facts

- **Analysis switches only work inside a batch control file** invoked as `mc12_64.exe @controlfile`. Passing `circuit.cir /A` directly on the command line does NOT run an analysis (it just opens the file in the GUI and hangs).
- **Micro-Cap never exits on its own** after a batch run. There is no exit switch. You MUST poll for the output file, then kill the process (pattern below).
- Numeric output for SPICE netlists requires a `.PRINT` statement in the netlist. No `.PRINT` → no output file → your poll times out.
- Output file extensions by analysis: transient `.TNO`, AC `.ANO`, DC `.DNO` (written uppercase, e.g. `RC_OUT.ANO`). The extensions for `/HD`, `/ID` and `/STABILITY` output are NOT verified on this install — run once, see what appears next to the `/NOF` path (or check the `.DOC` byproduct), then poll that name.
- Byproduct files: `<circuit>_cir.inx` next to the circuit, and `<controlfile>.DOC` next to the control file. Harmless; ignore or clean up.
- Use absolute paths everywhere; avoid paths with spaces in the circuit file path (quoting inside the control file is only verified for the `/NOF` argument).

## Workflow

### 1. Write a SPICE netlist (`.cir`)

Standard SPICE syntax. First line is a title comment. Must include an analysis statement and `.PRINT`:

```
* RC low-pass filter
V1 1 0 AC 1
R1 1 2 1K
C1 2 0 100N
.AC DEC 20 10 1MEG
.PRINT AC VM(2) VP(2)
.END
```

For transient: `.TRAN <tstep> <tstop>` + `.PRINT TRAN V(2) I(R1)`. For DC sweep: `.DC V1 0 5 0.1` + `.PRINT DC V(2)`.

### 2. Write the batch control file

One line per run: `<circuit path> <analysis switch> /NOF "<output path, no extension>"`

```
C:\path\to\rc_test.cir /A /NOF "C:\path\to\rc_out"
```

Analysis switches: `/T` transient, `/A` AC, `/D` DC, `/HD` harmonic distortion, `/ID` intermodulation distortion, `/STABILITY` stability. Multiple lines (or multiple switches per line) run multiple analyses. Optional `/DEF "X val"` overrides a `.DEFINE X ...` variable in the circuit — useful for parameter sweeps across lines. Name the control file `.bat` or `.txt` (it is read by Micro-Cap, never executed by the shell).

### 3. Launch, poll, kill (PowerShell)

```powershell
$p = Start-Process -FilePath "C:\MC12\mc12_64.exe" -ArgumentList "@C:\path\to\control.bat" -PassThru
$deadline = (Get-Date).AddSeconds(60)   # increase for long transient runs
while ((Get-Date) -lt $deadline -and -not (Test-Path "C:\path\to\rc_out.ANO")) { Start-Sleep -Milliseconds 500 }
Start-Sleep -Seconds 2                   # let the file finish writing
if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
```

When batching several runs in one control file, poll for the output of the LAST line before killing.

### 4. Parse the output

The `.ANO`/`.TNO`/`.DNO` file is plain text: a header block (analysis limits), then an "Interpolated Waveform Values" table with one column per `.PRINT` expression in scientific notation:

```
           F  MAG(V(2))    PH(V(2))
        (Hz)        (V)   (Degrees)
   1.000E+01  1.000E+00          NA
```

Parse with whitespace splitting; skip until the units line. `NA` appears where a value is undefined (e.g. phase at the first point).

## Beyond plain SPICE (references/)

- **Library parts** (`C:\MC12\LIBRARY`, chained by `NOM.LIB`): add
  `.LIB "NOM.LIB"`, grep for the exact `.MODEL`/`.SUBCKT` name (vendors
  use suffixes), instantiate subcircuits with `X` devices — pin order from
  the `.SUBCKT` line. Installing vendor models:
  `references/part-library.md`.
- **Digital / mixed-signal** (verified: three live runs + the help
  chapters): PSpice-style `U`
  primitives (`DFF`, `JKFF`, gates with `UGATE`/`UEFF` timing models),
  automatic analog↔digital interfaces, 74xx `.SUBCKT` families, digital
  nodes printing as logic states — worked netlist examples and the
  `$D_HI`/DIGCLOCK traps: `references/digital-mixed-signal.md`.

## Troubleshooting

- **Poll times out, no output file**: netlist error or missing `.PRINT`. Kill the process, then check for a `<circuit>.err` file or open the `.DOC` file next to the control file for clues. Verify the netlist parses (component syntax, node names, `.END` present).
- **A stray mc12_64 process from a previous run** can confuse things — check with `Get-Process mc12* -ErrorAction SilentlyContinue` and kill leftovers before launching.
- **User's own schematic files** (drawn in the GUI, also `.cir`/`.ckt`) work in batch mode too, but numeric output must have been enabled in their Analysis Limits dialog; if no output appears, that's the likely cause. The `/NOF` redirect still applies.

## Documented but unverified

Image export from batch lines (untested on this install — verify before relying on it): `/IA Page="Main" Output="plot.png"` exports analysis plots, `/IC Page="Page 1" Output="cir.gif"` exports a schematic page. Only one analysis switch per line when using image commands. Also `/S` (save run for later recall), `/R` (recall saved run), `/P`/`/PC`/`/PA` (print). Full reference: `C:\MC12\Documents\rm12.pdf` and `ug12.pdf`; the "Command Lines and Batch Files" topic in `C:\MC12\mc12.chm`.

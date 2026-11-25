---
name: micro-cap
description: Run Micro-Cap 12 circuit simulations headlessly (batch mode) on this machine — natively on Windows, through Wine on macOS. Use whenever the user asks to simulate, analyze, or verify an electronic circuit, filter, amplifier, or SPICE netlist — AC/frequency response, transient, DC sweep, distortion, digital or mixed-signal logic (gates, flip-flops, 74xx families, power-up sequencing) — or mentions Micro-Cap, MC12, .cir files, or netlists.
---

# Micro-Cap 12 batch simulation

Micro-Cap 12 (64-bit) is installed at `C:\MC12\mc12_64.exe` — on Windows directly, on macOS inside a Wine prefix (see **macOS (Wine)** below; every Micro-Cap fact here holds there too, only the launch and the paths differ). It is a GUI app, but it supports a batch mode that runs analyses and writes plain-text numeric output. All facts below were verified against the Windows install unless the final section ("Documented but unverified") says otherwise.

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

### 3. Launch, poll, kill (PowerShell — Windows; macOS: see **macOS (Wine)**)

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

## macOS (Wine)

Micro-Cap 12 has no macOS build; it runs as the Windows app under Wine. **This path is not yet verified against a live install** — the launcher logic is tested, Micro-Cap-under-Wine is not; confirm with the RC example above on first use and report what differs.

**One-time setup (the user runs it):** install Wine — Gcenx's build (`brew install --cask --no-quarantine gcenx/wine/wine-crossover`; the official Homebrew `wine-stable` cask fails Gatekeeper and is disabled since 2026-09-01) or CrossOver — on Apple Silicon Wine also needs Rosetta 2 (`softwareupdate --install-rosetta`). Create a dedicated prefix and run the Micro-Cap 12 installer into `C:\MC12`:

```bash
WINEPREFIX=~/.wine-mc12 wine ~/Downloads/<Micro-Cap 12 installer>.exe   # install to C:\MC12
```

The install then lives at `~/.wine-mc12/drive_c/MC12/` on the Mac (library: `~/.wine-mc12/drive_c/MC12/LIBRARY`, docs: `.../MC12/Documents`). With CrossOver, install into a bottle named `MC12` instead (or set `MC12_BOTTLE`).

**Paths:** Micro-Cap sees Windows paths. Wine maps the Mac root to drive `Z:`, so `/Users/me/sim/rc.cir` is `Z:\Users\me\sim\rc.cir`. Every path *inside the control file* uses the `Z:` form; the poll uses the Mac path. Same rules as Windows: absolute paths, no spaces in the circuit path. Write the control file with CRLF line endings (as on Windows).

**Run** with the bundled launcher — it converts the control-file path, starts `mc12_64.exe @control` under Wine, polls for the output, and kills Micro-Cap (plus the prefix's `wineserver`) afterwards:

```bash
MC="${CLAUDE_PLUGIN_ROOT}/skills/micro-cap/scripts/mc12-mac.sh"
SIM="$PWD/sim"                                       # absolute, no spaces
"$MC" winpath "$SIM/rc_test.cir"                       # -> Z:\...\sim\rc_test.cir
printf '%s /A /NOF "%s"\r\n' "$("$MC" winpath "$SIM/rc_test.cir")" "$("$MC" winpath "$SIM/rc_out")" > "$SIM/control.bat"
"$MC" run "$SIM/control.bat" "$SIM/rc_out.ANO" 60     # exit 0 found, 1 timed out, 2 no Wine / usage
"$MC" kill                                               # clear a stray Micro-Cap from an earlier run
```

Overrides: `MC12_WINE` (Wine binary), `MC12_WINEPREFIX` (default `~/.wine-mc12`), `MC12_BOTTLE` (CrossOver, default `MC12`), `MC12_EXE` (default `C:\MC12\mc12_64.exe`). Wine on macOS draws windows through the Mac desktop, so runs need a logged-in GUI session (not a bare SSH shell); the Micro-Cap window may flash up during a run. APFS is case-insensitive by default, so polling `rc_out.ANO` also matches the uppercase `RC_OUT.ANO` Micro-Cap writes.

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
- **A stray mc12_64 process from a previous run** can confuse things — check with `Get-Process mc12* -ErrorAction SilentlyContinue` (Windows) or `pgrep -fl mc12_64` (macOS; `mc12-mac.sh kill` clears it) and kill leftovers before launching.
- **macOS: `run` exits 2 "Wine not found"** — install Wine or CrossOver, or point `MC12_WINE` at the binary. **Times out at once with no output** — Wine could not start `C:\MC12\mc12_64.exe`: check the prefix (`ls ~/.wine-mc12/drive_c/MC12/mc12_64.exe`) and run `WINEPREFIX=~/.wine-mc12 wine 'C:\MC12\mc12_64.exe'` by hand to see Wine's error.
- **User's own schematic files** (drawn in the GUI, also `.cir`/`.ckt`) work in batch mode too, but numeric output must have been enabled in their Analysis Limits dialog; if no output appears, that's the likely cause. The `/NOF` redirect still applies.

## Documented but unverified

Image export from batch lines (untested on this install — verify before relying on it): `/IA Page="Main" Output="plot.png"` exports analysis plots, `/IC Page="Page 1" Output="cir.gif"` exports a schematic page. Only one analysis switch per line when using image commands. Also `/S` (save run for later recall), `/R` (recall saved run), `/P`/`/PC`/`/PA` (print). Full reference: `C:\MC12\Documents\rm12.pdf` and `ug12.pdf`; the "Command Lines and Batch Files" topic in `C:\MC12\mc12.chm`.

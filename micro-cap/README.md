# micro-cap

A plugin for running Micro-Cap 12 (Spectrum Software) circuit simulations headlessly on Windows, installed from the `electronics-skills` plugin marketplace. Micro-Cap is a GUI circuit simulator, but it supports a batch mode (`mc12_64.exe @controlfile`) that runs analyses and writes plain-text numeric output. Claude reads the skill, writes the SPICE netlist and the batch control file, launches and polls `mc12_64.exe` from the command line, and parses the numeric results — no GUI interaction. The plugin ships no scripts or compiled tooling; the skill is the verified workflow, learned once so every session skips the traps.

## How it works

1. Write a SPICE netlist (`.cir`) with an analysis statement and `.PRINT` (no `.PRINT` → no output file).
2. Write a batch control file listing the circuit and analysis switches (`/A`, `/T`, `/D`, ...) — the switches only work inside the control file, not on the command line.
3. Launch `mc12_64.exe @controlfile`, poll for the output file, then kill the process — Micro-Cap never exits on its own.
4. Parse the `.ANO` (AC) / `.TNO` (transient) / `.DNO` (DC) numeric output.

Analyses covered: transient, AC/frequency response, DC sweep, harmonic and intermodulation distortion, and stability (`/STABILITY`).

Mixed-signal too: digital primitives (gates, DFF/JKFF, tristate), 74xx library subcircuits, and automatic analog↔digital interfaces work in the same batch flow — the reference documents which configurations were run live (DFF divide-by-2, a library 7400, the power-up pattern), and the traps that only show up in batch mode (the `$D_HI` definition, DIGCLOCK quirks, digital nodes printing as logic states).

## Requirements

| Requirement | Notes |
|---|---|
| Windows 10/11 x64 | Batch mode drives the 64-bit `mc12_64.exe`. |
| Micro-Cap 12 | Installed at `C:\MC12` — the skill's verified paths assume this location. Verified on Micro-Cap 12.2.0.5 (64-bit), Windows 10. |
| Nothing else | No SDK, no dependencies — the skill drives `mc12_64.exe` directly with built-in PowerShell. |

## Uninstall

Remove the plugin through your client's plugin management UI (in Claude Code, `/plugin uninstall micro-cap@electronics-skills`). The plugin installs nothing system-wide and writes nothing itself — but the simulations it runs leave byproducts in your working directories (`<circuit>_cir.inx`, `<controlfile>.DOC`, the `.ANO`/`.TNO`/`.DNO` output files, any exported images). Those are your simulation results and the run's scratch files; keep or delete them as you like.

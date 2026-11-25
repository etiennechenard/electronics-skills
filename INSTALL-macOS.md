# electronics-skills on macOS — installation guide

This repository ships five Claude Code (and ZCode) plugins. As of the `macos-support` branch, **every plugin runs on both Windows and macOS**: there is no separate macOS edition — each tool picks the OS-specific piece at run time (secret store, VISA library, KiCad/Java paths, Wine launcher).

| Plugin | On macOS | Verification status |
|---|---|---|
| `parts-search` | API keys in the login **Keychain** (instead of the Windows Credential Manager) | Tested (full suite + a real Keychain round-trip) |
| `pcb-as-code` | KiCad from `/Applications/KiCad` or Homebrew; Java/FreeRouting auto-detected | Tested (full suite against a real kicad-cli 10.0.3); FreeRouting routing not run |
| `3dpart-as-code` | Already cross-platform (venv at `mech/.venv/bin/python`) | Unchanged, docs adjusted |
| `visa` | `VISA.framework` (NI-VISA) or `RsVisa.framework` (R&S VISA) | Tested without VISA installed; not tested against real instruments |
| `micro-cap` | Micro-Cap 12 (a Windows app) launched **through Wine** by `mc12-mac.sh` | Launcher tested with a fake Wine; **Micro-Cap under Wine not yet verified** |

---

## 1. Quick start

```bash
tar xzf electronics-skills-macos-*.tar.gz
cd electronics-skills
./install-macos.sh             # checks prerequisites, lists what is missing with links
./install-macos.sh --install   # then adds the local marketplace and installs all five plugins
```

The manual equivalent:

```bash
claude plugin marketplace add ~/path/to/electronics-skills
claude plugin install parts-search@electronics-skills
claude plugin install pcb-as-code@electronics-skills
claude plugin install 3dpart-as-code@electronics-skills
claude plugin install visa@electronics-skills
claude plugin install micro-cap@electronics-skills
```

Install only the plugins you need — each has its own prerequisites (section 3).

## 2. Common prerequisites

| Tool | Install | Link |
|---|---|---|
| Claude Code CLI | `npm install -g @anthropic-ai/claude-code` or the installer | <https://claude.ai/code> |
| Homebrew (recommended) | see site | <https://brew.sh> |
| Git | `xcode-select --install` | — |
| **.NET 10 SDK** (parts-search, pcb-as-code, 3dpart-as-code; also covers visa) | `brew install --cask dotnet-sdk` | <https://dotnet.microsoft.com/download/dotnet/10.0> |

> Check with `dotnet --list-sdks`: you need a `10.0.x` line (3dpart-as-code requires 10.0.300+). The .NET 8 SDK alone is only enough for `visa`.

## 3. Per plugin

### parts-search — DigiKey / Mouser / Newark part lookup

1. Free API keys (once per vendor):
   - DigiKey: <https://developer.digikey.com> (an app with *Product Information API v4* → client id + client secret)
   - Mouser: <https://www.mouser.com/api-hub/> (the **Search** key, not Cart/Order)
   - Newark / element14: <https://partner.element14.com>
2. Store them **in your own terminal** (hidden input):
   ```bash
   dotnet run --project parts-search/skills/parts-search/scripts/PartsSearch -- key set digikey
   dotnet run --project parts-search/skills/parts-search/scripts/PartsSearch -- key list
   ```
   They show up in **Keychain Access** as `parts-search/<vendor>/<field>`. If you later switch to another .NET install, macOS asks once for access — choose *Always Allow*.
3. The cache lives in `~/Library/Application Support/parts-search/cache/` (safe to delete).

### pcb-as-code — KiCad schematics/PCBs generated from C#

| Tool | Install | Link |
|---|---|---|
| KiCad 10 | `brew install --cask kicad` | <https://www.kicad.org/download/macos/> |
| Java 17+ (for FreeRouting) | `brew install openjdk` | <https://adoptium.net/> |
| FreeRouting (autorouter) | download the `.jar` into `~/tools/freerouting-1.4.5.1/` | <https://github.com/freerouting/freerouting/releases> |

Auto-detection: `kicad-cli` in `/Applications/KiCad/KiCad.app/Contents/MacOS/`, then `/opt/homebrew/bin/`. Overrides: `KICAD_CLI`, `FREEROUTING_JAVA`, `FREEROUTING_JAR` (handy when your FreeRouting is not 1.4.5.1: `export FREEROUTING_JAR=~/tools/freerouting-2.x.jar`).

### 3dpart-as-code — 3D-printable parts as code

| Tool | Install | Link |
|---|---|---|
| Python 3.12 (build123d) | `brew install python@3.12` | <https://www.python.org/downloads/macos/> |
| KiCad 10 | only for parts that mate with a board | see above |

The first `dotnet run mech/tools/build.cs` creates `mech/.venv` and installs build123d (a few hundred MB). For the live tests: `export PAC_PYTHON=mech/.venv/bin/python`.

### visa — instrument control (GPIB, USB-TMC, serial, LAN/LXI)

Install **one** macOS VISA library:

- **NI-VISA** (universal binary, native Apple Silicon since 2023 Q3): <https://www.ni.com/en/support/downloads/drivers/download.ni-visa.html> — installs `/Library/Frameworks/VISA.framework`. macOS readme: <https://ni.com/pdf/manuals/ni-visa-macos-2023-q3.html>
- **R&S VISA** for Mac: <https://www.rohde-schwarz.com/applications/r-s-visa-application-note_56280-148812.html> — installs `/Library/Frameworks/RsVisa.framework`

The skill finds either automatically; otherwise `export VISA_LIBRARY=/path/to/library`.

- **Architecture**: the library must match the `dotnet` process (arm64 on Apple Silicon). If it is Intel-only, install the **x64** .NET SDK and run it under Rosetta: `arch -x86_64 /usr/local/share/dotnet/x64/dotnet run …`. Exit code 3 (`visa_library_missing`) lists every path tried and any architecture mismatch.
- **GPIB** additionally needs the vendor's macOS GPIB driver (NI-488.2); LAN/LXI and USB-TMC work with the VISA library alone.

Quick test: `dotnet run --project visa/skills/visa/scripts/Visa -- list`

### micro-cap — Micro-Cap 12 SPICE simulation (through Wine)

Micro-Cap 12 is Windows-only. It has been freeware since Spectrum Software closed in 2019 ([Wikipedia](https://en.wikipedia.org/wiki/Micro-Cap), [Hackaday](https://hackaday.com/2020/01/08/commercial-circuit-simulator-goes-free)).

1. **Rosetta 2** (Apple Silicon): `softwareupdate --install-rosetta --agree-to-license`
2. **Wine**: the official Homebrew `wine-stable` cask is **disabled since 2026-09-01** (it fails the Gatekeeper check, see <https://formulae.brew.sh/cask/wine-stable>). Use instead:
   - Gcenx's build: `brew install --cask --no-quarantine gcenx/wine/wine-crossover` (<https://github.com/Gcenx/macOS_Wine_builds>, <https://github.com/Gcenx/wine-on-mac>)
   - or **CrossOver** (paid): <https://www.codeweavers.com/crossover> (bottle named `MC12`)
3. **Micro-Cap 12 installer** (v12.2.0.5): spectrum-soft.com is gone, but the archived download page still works: <https://web.archive.org/web/2020/http://www.spectrum-soft.com/download/download.shtm>
4. Install it into a dedicated Wine prefix, at `C:\MC12`:
   ```bash
   WINEPREFIX=~/.wine-mc12 wine ~/Downloads/<Micro-Cap 12 installer>.exe
   ```
5. Run simulations with the bundled launcher (converts paths to `Z:\…`, launches, polls for the output file, kills Micro-Cap):
   ```bash
   MC=micro-cap/skills/micro-cap/scripts/mc12-mac.sh
   "$MC" winpath "$PWD/sim/rc_test.cir"
   "$MC" run "$PWD/sim/control.bat" "$PWD/sim/rc_out.ANO" 60
   ```
   Variables: `MC12_WINE`, `MC12_WINEPREFIX` (default `~/.wine-mc12`), `MC12_BOTTLE` (CrossOver), `MC12_EXE` (`C:\MC12\mc12.exe` for the 32-bit build if the 64-bit one misbehaves under Wine).
   A logged-in GUI session is required (Wine briefly shows the Micro-Cap window); a bare SSH shell will not work.

Since this path has not been verified against a real Micro-Cap yet, run the RC example from the SKILL.md first and note anything that differs.

## 4. Check that everything works

```bash
./install-macos.sh
dotnet test parts-search/skills/parts-search/scripts/PartsSearch.Tests
dotnet test pcb-as-code/skills/pcb-as-code/tests
dotnet test visa/skills/visa/scripts
```

The test that touches the real Keychain is opt-in: `PARTS_SEARCH_KEYCHAIN_TEST=1 dotnet test …` (creates one throwaway item and deletes it).

## 5. Uninstall

```bash
claude plugin uninstall <plugin>@electronics-skills
claude plugin marketplace remove electronics-skills
```

What may remain: the `~/Library/Application Support/parts-search/` cache, the `parts-search/*` Keychain items (`key clear <vendor>`), and the `~/.wine-mc12` Wine prefix.

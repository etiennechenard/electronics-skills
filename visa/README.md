# visa-skill

A plugin for controlling VISA-addressable laboratory instruments over GPIB, USB-TMC, Serial/ASRL, and TCPIP/LXI transports, installed from the `electronics-skills` plugin marketplace. Claude reads the skill, constructs `dotnet run` invocations, and parses the JSON output to interact with your instruments.

## Requirements

| Requirement       | Notes                                                                                                                                                                                          |
| ----------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Windows 10/11 x64 | The skill uses `visa32.dll` / `visa64.dll` via P/Invoke. Linux/macOS are not supported.                                                                                                        |
| .NET 8 SDK        | Install from [dot.net](https://dot.net). Verify with `dotnet --version`.                                                                                                                       |
| VISA driver       | NI-VISA, Keysight IO Libraries Suite, R&S VISA, or any vendor library that exports the IVI-Foundation VISA C API (`visa32.dll` / `visa64.dll`). Install from your instrument vendor's website. |

## Usage

Every subcommand follows this pattern:

```
dotnet run --project skills/visa/scripts/Visa -- <subcommand> [args]
```

The `--` separator is required. The project path accepts an absolute path, so you can invoke the skill from any working directory.

### List and identify instruments (G-S1, G-S2)

Works with GPIB, USB-TMC, Serial, TCPIP/LXI — any resource the installed VISA driver enumerates.

```powershell
dotnet run --project skills/visa/scripts/Visa -- list
# {"ok":true,"pattern":"?*INSTR","resources":["GPIB1::5::INSTR","USB0::0x0957::...::INSTR"]}

dotnet run --project skills/visa/scripts/Visa -- info GPIB1::5::INSTR
# {"ok":true,"address":"GPIB1::5::INSTR","identifier":"KEYSIGHT,34465A,MY12345,A.03.02"}
```

### Query, write, read

```powershell
dotnet run --project skills/visa/scripts/Visa -- query GPIB1::5::INSTR "*IDN?"
dotnet run --project skills/visa/scripts/Visa -- write GPIB1::5::INSTR "*RST"
dotnet run --project skills/visa/scripts/Visa -- read  GPIB1::5::INSTR
```

### Multi-command script

```powershell
# Via --cmd flags (repeatable):
dotnet run --project skills/visa/scripts/Visa -- script GPIB1::5::INSTR `
  --cmd "*RST" --cmd "CONF:VOLT:DC 10" --cmd "READ?"

# Via a script file (one command per line, # comments and blank lines ignored):
dotnet run --project skills/visa/scripts/Visa -- script GPIB1::5::INSTR --file measure.scpi

# Abort on first error:
dotnet run --project skills/visa/scripts/Visa -- script GPIB1::5::INSTR --cmd "*RST" --stop-on-error
```

### Device control

```powershell
dotnet run --project skills/visa/scripts/Visa -- clear        GPIB1::5::INSTR   # viClear
dotnet run --project skills/visa/scripts/Visa -- trigger      GPIB1::5::INSTR   # viAssertTrigger
dotnet run --project skills/visa/scripts/Visa -- serial-poll  GPIB1::5::INSTR   # viReadSTB
```

### GPIB bus control

```powershell
dotnet run --project skills/visa/scripts/Visa -- ifc          GPIB1             # Interface Clear
dotnet run --project skills/visa/scripts/Visa -- ren          GPIB1 on          # Remote Enable on
dotnet run --project skills/visa/scripts/Visa -- ren          GPIB1 off         # Remote Enable off
dotnet run --project skills/visa/scripts/Visa -- atn          GPIB1 on          # ATN assert
dotnet run --project skills/visa/scripts/Visa -- gpib-command GPIB1 --hex "3F 5F"  # UNL + UNT
```

### Serial port configuration (G-S3)

Serial flags are accepted by all instrument-session subcommands (`query`, `write`, `read`, `script`, `clear`, `trigger`, `serial-poll`, `get-attr`, `set-attr`, `read-block`, `write-block`). Unset flags leave the driver default in force.

```powershell
dotnet run --project skills/visa/scripts/Visa -- query ASRL1::INSTR "*IDN?" `
  --baud 9600 --data-bits 8 --parity none --stop-bits 1 --flow-control none
```

### VISA attribute access (G-S4)

Read or write VISA attributes by symbolic name or raw hex id.

```powershell
dotnet run --project skills/visa/scripts/Visa -- get-attr GPIB1::5::INSTR VI_ATTR_TMO_VALUE
# {"ok":true,"address":"...","attr":"VI_ATTR_TMO_VALUE","id":"0x3FFF001A","type":"u32","value":5000}

dotnet run --project skills/visa/scripts/Visa -- set-attr GPIB1::5::INSTR VI_ATTR_TMO_VALUE 10000
dotnet run --project skills/visa/scripts/Visa -- get-attr GPIB1::5::INSTR 0x3FFF001A --type u32
```

### USB-TMC control transfers (G-S5)

Low-level `viUsbControlIn` / `viUsbControlOut` for USB RAW and USB-TMC resources.

```powershell
dotnet run --project skills/visa/scripts/Visa -- usb-control-in  USB0::0x0957::0x0D0A::MY12345::INSTR `
  --request-type 0xA1 --request 0x01 --value 0 --index 0 --length 4
# {"ok":true,"address":"...","bytes_read":4,"hex":"00 00 00 00"}

dotnet run --project skills/visa/scripts/Visa -- usb-control-out USB0::0x0957::0x0D0A::MY12345::INSTR `
  --request-type 0x21 --request 0x09 --value 0x0300 --index 0 --hex "AA BB CC"
```

### Binary block transfer (G-S6)

`read-block` and `write-block` move binary payloads through `--out-file` / `--in-file`, auto-parsing and prepending IEEE 488.2 block headers with a `--raw` escape for instruments that use proprietary binary formats.

```powershell
# Read a binary IEEE 488.2 block (e.g. a scope waveform):
dotnet run --project skills/visa/scripts/Visa -- read-block GPIB1::1::INSTR "CURV?" --out-file wave.bin
# {"ok":true,"address":"...","command":"CURV?","out_file":"wave.bin","format":"definite","declared_length":2048,"bytes_written":2048}

# Read raw binary (instrument uses proprietary format, not IEEE 488.2):
dotnet run --project skills/visa/scripts/Visa -- read-block GPIB1::1::INSTR "CURV?" --out-file wave.bin --raw

# Write a binary block from file:
dotnet run --project skills/visa/scripts/Visa -- write-block GPIB1::1::INSTR --in-file wave.bin "TRACE:DATA "
# {"ok":true,"address":"...","in_file":"wave.bin","format":"definite","payload_bytes":2048,"bytes_written":2055}
```

### JSON output

Every invocation prints exactly one JSON object to stdout:

```json
{ "ok": true, "address": "GPIB1::5::INSTR", "identifier": "KEYSIGHT,34465A,..." }
```

On failure:

```json
{
  "ok": false,
  "error": "visa",
  "operation": "viRead",
  "status": "0xBFFF0015",
  "status_int": -1073807339,
  "description": "VI_ERROR_TMO: A timeout occurred",
  "address": "GPIB1::5::INSTR"
}
```

### Logging / debug output

Set `VISA_LOG_LEVEL` before running to control stderr trace output:

```powershell
$env:VISA_LOG_LEVEL = "quiet"   # no stderr output
$env:VISA_LOG_LEVEL = "info"    # one line per VISA call (default)
$env:VISA_LOG_LEVEL = "debug"   # + byte-level payloads
```

Stderr contains only human-readable trace lines. Stdout contains only the JSON envelope.

## Exit codes

| Code | Meaning                | What to do                                                                                                                                  |
| ---- | ---------------------- | ------------------------------------------------------------------------------------------------------------------------------------------- |
| 0    | Success                | Stdout JSON has `"ok": true`.                                                                                                               |
| 1    | VISA error             | Check `status` / `description` in the JSON. Common causes: timeout, resource not open, wrong address.                                       |
| 2    | Usage error            | Bad argument, unknown flag, missing required positional, or invalid value. Check the `description` field. Run the subcommand with `--help`. |
| 3    | VISA library not found | Install NI-VISA or another VISA implementation. Verify `visa32.dll` / `visa64.dll` is on the system path.                                   |
| 4    | Internal error         | A bug in the CLI. File an issue.                                                                                                            |

## Troubleshooting

**`error: "visa_library_missing"` (exit 3)**
No VISA driver is installed or the DLL is not on the system search path. Install NI-VISA from [ni.com](https://ni.com) or your instrument vendor's equivalent.

**`resources: []` from `list` — no instruments found**

- Check physical cables (GPIB cable, USB-TMC cable, etc.).
- Check the GPIB board index (GPIB0 vs GPIB1 — run `list` and look at the prefix).
- Try `--pattern "?*"` to list all resource types, not just instruments.
- Open NI-MAX (or vendor equivalent) to verify the driver sees the hardware.

**Timeout (`0xBFFF0015 VI_ERROR_TMO`) on `query` or `read`**

- The instrument didn't respond in time. Increase `--timeout` (e.g., `--timeout 10000`).
- Verify the address is correct with `list`.
- Confirm the instrument is powered on and not in a local/locked state.

**`error: "usage"` on a valid-looking command**

- Remember the `--` separator: `dotnet run --project skills/visa/scripts/Visa -- <subcommand>`.
- Flag values must be space-separated: `--timeout 5000`, not `--timeout=5000`.
- `--term-char` is not accepted on `read-block` or `write-block`.
- Board arguments for bus-control commands must be `GPIB<N>` or `GPIB<N>::INTFC`.

## Uninstall

Remove the plugin through your client's plugin management UI (in Claude Code, `/plugin uninstall visa@electronics-skills`). No registry entries, services, or system files are created by this plugin — only the `skills/visa/scripts/Visa/obj/`, `skills/visa/scripts/Visa/bin/`, `skills/visa/scripts/Visa.Core/obj/`, `skills/visa/scripts/Visa.Core/bin/`, `skills/visa/scripts/Visa.Tests/obj/`, and `skills/visa/scripts/Visa.Tests/bin/` build-cache directories inside the installed plugin, which can be deleted from the plugin root with:

```powershell
Remove-Item -Recurse -Force skills\visa\scripts\Visa\bin, skills\visa\scripts\Visa\obj,
  skills\visa\scripts\Visa.Core\bin, skills\visa\scripts\Visa.Core\obj,
  skills\visa\scripts\Visa.Tests\bin, skills\visa\scripts\Visa.Tests\obj
```

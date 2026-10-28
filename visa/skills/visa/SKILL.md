---
name: visa
description: Control VISA-addressable lab instruments (DMMs, scopes, power supplies, signal generators) over GPIB, USB-TMC, Serial/ASRL, or TCPIP/LXI — send SCPI commands, read measurements, control the bus, read/write VISA attributes, do USB-TMC control transfers, and move binary block payloads to/from files.
---

# visa-skill

## When to use this skill

Use this skill whenever the user needs to:
- Enumerate VISA resources (instruments) connected to the system
- Identify an instrument via `*IDN?`
- Send SCPI commands or read responses from a laboratory instrument
- Run a multi-command configure-then-measure sequence in one invocation
- Configure serial-port parameters (`--baud`, `--data-bits`, `--parity`, `--stop-bits`, `--flow-control`) for ASRL instruments
- Read or write VISA attributes by symbolic name or raw hex id (`get-attr`, `set-attr`)
- Perform USB-TMC low-level control transfers (`usb-control-in`, `usb-control-out`)
- Transfer binary block payloads to/from files, with optional IEEE 488.2 framing (`read-block`, `write-block`)
- Control the GPIB bus (Interface Clear, Remote Enable, ATN, raw command bytes)
- Read the instrument status byte via serial poll

## Prerequisites

1. **VISA driver installed** — NI-VISA, Keysight VISA, R&S VISA, or any vendor VISA library that exports the IVI-Foundation C API (`visa32.dll` / `visa64.dll`). Install from the vendor's website before using this skill.
2. **.NET 8 SDK on PATH** — `dotnet --version` should report 8.x or later.
3. **Windows 10/11 x64** — this skill is Windows-only.

## Invocation form

```
dotnet run --project "${CLAUDE_PLUGIN_ROOT}/skills/visa/scripts/Visa" -- <subcommand> [args]
```

The `--` separator is required. `${CLAUDE_PLUGIN_ROOT}` is substituted by Claude Code with this plugin's install directory, so the project path resolves to an absolute location and the command works from any working directory — including when this skill is invoked from another project or plugin.

## Subcommand reference (common commands)

| Subcommand | Synopsis | Key flags |
|---|---|---|
| `list` | Enumerate VISA resources | `--pattern <expr>` (default `?*INSTR`) |
| `info` | Open resource, send `*IDN?`, return identifier | `--timeout <ms>` |
| `query` | Write a command, read the response | `--timeout`, `--term-char`, `--term-char-en`, `--no-eoi` |
| `write` | Write a command without reading | `--timeout`, `--no-eoi` |
| `read` | Read once from a resource | `--timeout`, `--term-char`, `--term-char-en` |
| `script` | Multi-command session (write+query mix) | `--cmd` (repeatable) or `--file`, `--timeout`, `--term-char`, `--term-char-en`, `--no-eoi`, `--stop-on-error` |
| `clear` | Send device clear (`viClear`) | `--timeout` |
| `trigger` | Assert software trigger (`viAssertTrigger`) | `--timeout` |
| `serial-poll` | Read status byte via `viReadSTB` | `--timeout` |
| `read-block` | Send query, read IEEE 488.2 binary block to file | `--out-file` (required), `--raw`, `--timeout` |
| `write-block` | Write IEEE 488.2 binary block from file | `--in-file` (required), `[<command>]`, `--raw`, `--timeout` |

**Flag rules (all subcommands):**
- All flags are long-form (`--name value`). No short flags.
- `--flag=value` syntax is not supported; use `--flag value`.
- `--timeout` is in milliseconds; default is `5000`.
- `--term-char` accepts `NL` (10), `CR` (13), or a decimal integer 0–255 (uppercase alpha only). Not accepted on `read-block` or `write-block`. Supplying it also **enables** termchar matching for that read (so it is not a silent no-op on the EOI buses that default matching off); pass `--term-char-en 0` to override.
- `--term-char-en` accepts `0` or `1` and forces read termination-char matching off/on. **Default is interface-aware:** off for EOI/END-terminated buses (`GPIB`, `USB`, `VXI`/`PXI`, `TCPIP::…::INSTR`) — the instrument's EOI/END ends the read, which is the canonical GPIB behavior and avoids the trailing-junk-byte failure seen on instruments that assert EOI without a trailing newline — and on for `ASRL` (serial, no EOI line) and `TCPIP::…::SOCKET` (raw stream, no END), which would otherwise read until timeout. Override only for an instrument that deviates from its bus norm. Note `--no-eoi` is unrelated: it suppresses EOI on **writes**, not read termination.
- `--hex` accepts uppercase/lowercase hex with or without spaces; must be even digit count.
- Board arguments (`ifc`, `ren`, `atn`, `gpib-command`) accept `GPIB<N>` or `GPIB<N>::INTFC`.

### Shared serial-options flag group

Accepted by: `info`, `query`, `write`, `read`, `script`, `clear`, `trigger`, `serial-poll`, `get-attr`, `set-attr`, `read-block`, `write-block`.

Not accepted by: `list` (no session), `ifc`, `ren`, `atn`, `gpib-command` (GPIB board session), `usb-control-in`, `usb-control-out` (USB, not serial) — passing them there is a usage error.

| Flag | Accepted values |
|---|---|
| `--baud <n>` | Positive integer (e.g. `9600`, `115200`) |
| `--data-bits <n>` | `5`–`8` |
| `--parity <p>` | `none`, `odd`, `even`, `mark`, `space` (lowercase) |
| `--stop-bits <s>` | `1`, `1.5`, `2` |
| `--flow-control <f>` | `none`, `xon`, `rts`, `dtr` (lowercase) |

Each flag is optional; unset flags leave the driver default in force. Invalid values → usage error, exit 2.

### `read-block` / `write-block` details

- `read-block <address> <command> --out-file <path> [--raw] [--timeout <ms>] [serial flags]`  
  Sends `<command>` (with `\n` appended if absent), reads the binary response. Without `--raw`: parses the IEEE 488.2 `#NDDDDD` header and writes the payload bytes to `--out-file`; reports `format` (`definite`|`indefinite`), `declared_length`, and `bytes_written`. With `--raw`: writes the raw response bytes verbatim (use this for instruments with proprietary binary formats).

- `write-block <address> --in-file <path> [<command>] [--raw] [--timeout <ms>] [serial flags]`  
  Reads payload bytes from `--in-file`. Without `--raw`: prepends `BlockHeader.Format(len)` (e.g. `#42048`) and a trailing `\n`, then writes via `WriteRaw`. With `--raw`: writes file bytes verbatim (no IEEE 488.2 header). `<command>` is an optional ASCII prefix sent immediately before the block, with no auto-`\n`. Reports `format` (`definite`|`raw`), `payload_bytes`, and `bytes_written`.

## Common examples

```powershell
# List all connected instruments
dotnet run --project "${CLAUDE_PLUGIN_ROOT}/skills/visa/scripts/Visa" -- list

# Identify an instrument
dotnet run --project "${CLAUDE_PLUGIN_ROOT}/skills/visa/scripts/Visa" -- info GPIB1::5::INSTR

# Send a query and read the response
dotnet run --project "${CLAUDE_PLUGIN_ROOT}/skills/visa/scripts/Visa" -- query GPIB1::5::INSTR "*IDN?"

# Multi-step sequence: reset, configure, measure
dotnet run --project "${CLAUDE_PLUGIN_ROOT}/skills/visa/scripts/Visa" -- script GPIB1::5::INSTR `
  --cmd "*RST" --cmd "CONF:VOLT:DC" --cmd "READ?"

# Serial instrument query with baud rate
dotnet run --project "${CLAUDE_PLUGIN_ROOT}/skills/visa/scripts/Visa" -- query ASRL1::INSTR "*IDN?" --baud 9600

# Read a binary waveform block to file
dotnet run --project "${CLAUDE_PLUGIN_ROOT}/skills/visa/scripts/Visa" -- read-block GPIB1::1::INSTR "CURV?" --out-file wave.bin

# Read raw binary (instrument uses proprietary format)
dotnet run --project "${CLAUDE_PLUGIN_ROOT}/skills/visa/scripts/Visa" -- read-block GPIB1::1::INSTR "CURV?" --out-file wave.bin --raw

# Write a binary block from file
dotnet run --project "${CLAUDE_PLUGIN_ROOT}/skills/visa/scripts/Visa" -- write-block GPIB1::1::INSTR --in-file wave.bin "TRACE:DATA "
```

## JSON output (stdout)

Every invocation prints exactly one JSON object to stdout and exits.

**Success:**
```json
{ "ok": true, ... subcommand-specific fields ... }
```

**Error:**
```json
{
  "ok": false,
  "error": "visa | usage | visa_library_missing | internal",
  "operation": "viOpen | argparse | loadlibrary | unhandled | ...",
  "status": "0xBFFF0015",
  "status_int": -1073807339,
  "description": "VI_ERROR_TMO: A timeout occurred",
  ... context fields (address, command, board, etc.) ...
}
```

`status` / `status_int` appear only for `error: "visa"`.

## Exit-code table

| Code | Meaning |
|---|---|
| 0 | Success — stdout JSON has `"ok": true` |
| 1 | VISA error — a VISA C function returned a negative status |
| 2 | Usage error — bad argument, unknown flag, missing required arg, unreadable `--in-file`, block parse failure |
| 3 | VISA library not found — `visa32.dll` / `visa64.dll` not installed (`visa_library_missing`) |
| 4 | Internal error — unexpected exception |

## Stderr logging

Stderr contains one human-readable trace line per VISA call (format: `[visa] <op> <context> ok|err <0xSTATUS>`). Control verbosity with the `VISA_LOG_LEVEL` environment variable:
- `quiet` — no output
- `info` — one line per VISA call (default)
- `debug` — also logs byte payloads

## Advanced commands

The following commands are available but are uncommon in routine use. Read [`references/advanced.md`](references/advanced.md) before using any of them.

| Subcommand | Purpose |
|---|---|
| `ifc` | Pulse Interface Clear on a GPIB board |
| `ren` | Drive the GPIB Remote Enable line on/off |
| `atn` | Drive the GPIB ATN line on/off |
| `gpib-command` | Send raw GPIB command bytes via `viGpibCommand` |
| `get-attr` | Read a VISA attribute by symbolic name or hex id |
| `set-attr` | Write a VISA attribute by symbolic name or hex id |
| `usb-control-in` | Low-level USB-TMC control-pipe read (`viUsbControlIn`) |
| `usb-control-out` | Low-level USB-TMC control-pipe write (`viUsbControlOut`) |

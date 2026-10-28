# TDDCliContract — visa-skill CLI

**Owner:** kosmic@gmail.com
**Last updated:** 2026-06-13
**Parent TDD:** [TDD.md](TDD.md)

---

This sub-TDD is the **authoritative wire contract** between Claude and the `visa-skill` CLI. It is what `skills/visa/SKILL.md` summarizes for Claude's runtime context. If the parent TDD and this document disagree on contract-level detail, **this document wins** for the CLI surface; the parent TDD wins for architecture. The binary-block framing logic behind `read-block`/`write-block` is specified in [TDDBinaryTransfer.md](TDDBinaryTransfer.md); this document specifies their wire shapes.

## 1. Invocation form

```
dotnet run --project skills/visa/scripts/Visa -- <subcommand> [args]
```

The `--` separator is required so MSBuild stops parsing flags and forwards the rest to `Visa.exe`. The cwd of the calling process is the cwd for `dotnet run` and the spawned process; the CLI uses cwd only to resolve relative `--file` / `--out-file` / `--in-file` paths. `dotnet run --project <path>` accepts an absolute `<path>` regardless of cwd.

`Visa.csproj` is `Microsoft.NET.Sdk` console (`<OutputType>Exe</OutputType>`, `<TargetFramework>net8.0</TargetFramework>`, `<Nullable>enable</Nullable>`). No precompiled binary is checked in.

**Text encoding and line endings (text I/O subcommands).** Command strings and text responses are treated as **ASCII**: C# `string` ↔ bytes via `Encoding.ASCII`. Bytes ≥ 0x80 returned by an instrument become the ASCII replacement char (non-ASCII is not legal SCPI). When a command does not already end with one, a single `\n` (0x0A) is appended — not `\r\n`. Read termination is configured via `--term-char` (default `\n`). **Binary subcommands (`read-block`/`write-block`) bypass this entirely** — see [TDDBinaryTransfer.md](TDDBinaryTransfer.md).

## 2. JSON output contract

### 2.1 Success envelope

Every successful subcommand prints exactly one single-line JSON object to stdout:

```json
{ "ok": true, ... per-subcommand fields ... }
```

`System.Text.Json`, `WriteIndented = false`, `\n`-terminated. Numbers are JSON numbers, booleans are JSON booleans.

**Byte-count semantics.** `bytes_written` / `bytes_read` are wire-level counts from `viWrite` / `viRead` — they include the auto-appended `\n` on text writes and the termination char on text reads. The trimmed `response` string strips trailing `\r`/`\n`; raw byte counts do not.

**Read loop semantics (text).** A `viRead` may complete with `VI_SUCCESS_TERM_CHAR` (termchar matched), `VI_SUCCESS` (count satisfied), or `VI_SUCCESS_MAX_CNT` (buffer full, more pending). The text read loop ends on the first two, continues on `MAX_CNT`, accepts-and-ends on any other positive warning, and throws on negative status. (Binary read loop: [TDDBinaryTransfer.md](TDDBinaryTransfer.md) §3.)

### 2.2 Error envelope

Every failure prints exactly one JSON object:

```json
{
  "ok": false,
  "error": "<short kind>",
  "operation": "<VISA fn name or stage>",
  "status": "0xBFFF0015",
  "status_int": -1073807339,
  "description": "Timeout expired before operation completed.",
  ... per-subcommand context fields ...
}
```

- `ok` — always `false`.
- `error` — one of `"usage"`, `"visa"`, `"visa_library_missing"`, `"internal"` (maps 1:1 to exit codes, §5).
- `operation` — for `visa`: the failing VISA C function (`"viOpen"`, `"viWrite"`, `"viUsbControlIn"`, …). For `usage`: a stage name (`"argparse"`, `"validate"`, `"parse_block"`, `"dispatch"`). For `visa_library_missing`: `"loadlibrary"`. For `internal`: `"unhandled"`.
- `status` — for `visa`: unsigned 32-bit hex with `0x` prefix, uppercase, e.g. `"0xBFFF0015"`. Omitted otherwise.
- `status_int` — for `visa`: the same status as a signed `int`. Omitted otherwise.
- `description` — human-readable. For VISA errors, the `viStatusDesc` string. Otherwise a one-line message.
- Additional fields echo operation context (`address`, `command`, `board`, `attr`, `out_file`, …) per each subcommand's entry.

### 2.3 Stdout / stderr separation

- Stdout: exactly one JSON object per invocation, `\n`-terminated. Nothing else.
- Stderr: human-readable trace lines (§7), one per VISA call. No JSON on stderr.

## 3. Subcommand reference

Subcommands carried over from the predecessor (§3.1–3.13) retain identical contracts modulo the `[visa]` rebrand. New subcommands for this session: §3.14–3.19. The **shared serial-options flag group** (§3.0) is accepted by every instrument-session-opening subcommand.

### 3.0 Shared serial-options flag group (G-S3)

These flags are accepted by every subcommand that opens an **instrument** session: `info`, `query`, `write`, `read`, `script`, `clear`, `trigger`, `serial-poll`, `get-attr`, `set-attr`, `read-block`, `write-block`. They are **not** accepted by `list` (no session), the bus-control subcommands `ifc`/`ren`/`atn`/`gpib-command` (board INTFC session, not serial), or the USB control subcommands `usb-control-in`/`usb-control-out` (USB, not serial) — passing them there is a usage error. Each flag is optional; an unset flag is **not written**, leaving the driver/port default in force. When supplied, the value is applied via `viSetAttribute` immediately after `viOpen` + timeout/termchar setup, before any I/O.

| Flag | Maps to | Accepted values |
|---|---|---|
| `--baud <n>` | `VI_ATTR_ASRL_BAUD` (U32) | positive integer (e.g. `9600`, `115200`). |
| `--data-bits <n>` | `VI_ATTR_ASRL_DATA_BITS` (U16) | `5`–`8`. |
| `--parity <p>` | `VI_ATTR_ASRL_PARITY` (U16) | `none`(0), `odd`(1), `even`(2), `mark`(3), `space`(4). Lowercase only. |
| `--stop-bits <s>` | `VI_ATTR_ASRL_STOP_BITS` (U16) | `1`(→10), `1.5`(→15), `2`(→20). (VISA encodes stop bits ×10.) |
| `--flow-control <f>` | `VI_ATTR_ASRL_FLOW_CNTRL` (U16) | `none`(0), `xon`(1), `rts`(2), `dtr`(4). Lowercase only. |

Invalid values (out-of-range integer, unknown enum spelling, wrong case) → usage error, exit 2, `operation: "validate"`. Flag parsing lives in `Cli/SerialFlags` (CLI); range/enum validation and the VISA value encodings live in `Visa.Core.Protocol.SerialConfig` (both unit-tested, G-S7).

These flags do not appear in the success envelope unless the subcommand's entry says so; they are applied silently.

---

### 3.1 `list`

Enumerate VISA resources matching a search expression.

**Usage:** `list [--pattern <expr>]`

| Flag | Default | Description |
|---|---|---|
| `--pattern <expr>` | `?*INSTR` | VISA `viFindRsrc` search expression. |

**VISA calls:** `viOpenDefaultRM` (lazy, once), `viFindRsrc`, `viFindNext` repeatedly, `viClose` on the find list.

**Success stdout:**
```json
{ "ok": true, "pattern": "?*INSTR", "resources": ["GPIB0::5::INSTR", "USB0::0x0957::0x2C07::MY12345::INSTR", "ASRL3::INSTR"] }
```
`resources` is a possibly-empty array in driver enumeration order. `VI_ERROR_RSRC_NFOUND` is **not** an error — emit success with `resources: []`. Any other negative status is a VISA error.

**Error context fields:** `pattern`.

---

### 3.2 `info`

Open a resource, send `*IDN?`, return the identifier.

**Usage:** `info <address> [--timeout <ms>] [serial flags]`

| Arg / flag | Default | Description |
|---|---|---|
| `<address>` | (required) | Full VISA resource string. |
| `--timeout <ms>` | `5000` | VISA timeout. |

**VISA calls:** `viOpen`, attribute set (`TMO_VALUE`, `TERMCHAR=\n`, `TERMCHAR_EN` per the interface-aware default below, serial attrs if any), `viWrite("*IDN?\n")`, `viRead` (terminates on END/EOI, or termchar when enabled), `viClose`.

**Success stdout:**
```json
{ "ok": true, "address": "GPIB0::5::INSTR", "identifier": "KEYSIGHT,34465A,MY12345,A.03.02-..." }
```
`identifier` has trailing `\r\n` stripped. **Error context fields:** `address`.

---

### 3.3 `query`

Write a command, read the response.

**Usage:** `query <address> <command> [--timeout <ms>] [--term-char <int|"NL"|"CR">] [--term-char-en <0|1>] [--no-eoi] [serial flags]`

| Arg / flag | Default | Description |
|---|---|---|
| `<address>` | (required) | Resource string. |
| `<command>` | (required) | SCPI command; `\n` appended if missing. |
| `--timeout <ms>` | `5000` | VISA timeout. |
| `--term-char <val>` | `NL` (10) | Read termination char. Decimal `0–255`, `"NL"` (10), or `"CR"` (13). Lowercase/mixed alpha rejected. Supplying it also enables termchar matching (see below). |
| `--term-char-en <0\|1>` | interface-aware | Enable/disable read termchar matching (`TERMCHAR_EN`). See the read-termination model below. |
| `--no-eoi` | (off) | Suppress EOI on the **write** (`SEND_END_EN=0`). Does not affect read termination. |

**Read-termination model (applies to `info`, `query`, `read`, `script`).** `TERMCHAR_EN` defaults per interface: **off** for EOI/END-terminated buses — `GPIB`, `USB` (USBTMC `INSTR`), `VXI`/`PXI`, and `TCPIP::…::INSTR` (VXI-11/HiSLIP) — so the instrument's END indicator terminates the read (canonical IEEE 488.2 behavior; avoids the trailing-junk-byte failure on instruments that assert EOI without a trailing `\n`); **on** for `ASRL` (serial, no EOI line), `TCPIP::…::SOCKET` and `USB::…::RAW` (no END framing), and any unrecognized prefix (conservative). Precedence: an explicit `--term-char-en` wins; otherwise supplying `--term-char` enables matching; otherwise the interface default applies.

**VISA calls:** `viOpen`, set timeout + termchar + termchar-en + serial + (optional) send-EOI, `viWrite`, `viRead`, `viClose`.

**Success stdout:**
```json
{ "ok": true, "address": "GPIB0::5::INSTR", "command": "*IDN?", "response": "KEYSIGHT,34465A,...", "bytes_written": 6, "bytes_read": 52 }
```
`response` trailing `\r\n` stripped. **Error context fields:** `address`, `command`.

---

### 3.4 `write`

Write a command; do not read.

**Usage:** `write <address> <command> [--timeout <ms>] [--no-eoi] [serial flags]`

Flags identical to `query` minus the read-side `--term-char` / `--term-char-en` (write does not read).

**Success stdout:**
```json
{ "ok": true, "address": "GPIB0::5::INSTR", "command": "*RST", "bytes_written": 5 }
```
**Error context fields:** `address`, `command`.

---

### 3.5 `read`

Read once from an opened resource (open, read, close).

**Usage:** `read <address> [--timeout <ms>] [--term-char <val>] [--term-char-en <0|1>] [serial flags]`

`--term-char` / `--term-char-en` follow the read-termination model in §3.3.

**Success stdout:**
```json
{ "ok": true, "address": "GPIB0::5::INSTR", "response": "+1.23456789E+00", "bytes_read": 16 }
```
**Error context fields:** `address`.

---

### 3.6 `script`

Open a session, run a sequence of write/query operations, close. Commands ending with `?` (after trim) are queries; others are writes.

**Usage:** `script <address> ( --cmd <c1> [--cmd <c2>...] | --file <path> ) [--timeout <ms>] [--term-char <val>] [--term-char-en <0|1>] [--no-eoi] [--stop-on-error] [serial flags]`

| Arg / flag | Default | Description |
|---|---|---|
| `<address>` | (required) | Resource string. |
| `--cmd <c>` | — | Repeatable. One command per flag. At least one of `--cmd`/`--file` required. |
| `--file <path>` | — | UTF-8 text file, one command per line. Blank lines and `#`-prefixed lines skipped. Mutually exclusive with `--cmd`. Relative paths resolve against cwd. |
| `--timeout <ms>` | `5000` | Applies to each operation. |
| `--term-char <val>` | `NL` | Read termchar; supplying it enables matching (see §3.3). |
| `--term-char-en <0\|1>` | interface-aware | Enable/disable read termchar matching. See §3.3. |
| `--no-eoi` | (off) | Suppress EOI on every write (write-side; not read termination). |
| `--stop-on-error` | (off) | Abort and emit the error envelope on the first VISA failure. Default: capture per-command errors inline and continue. |

**VISA calls:** `viOpen` once, attribute set once (incl. serial), then per command `viWrite` (always) + `viRead` (only if `?`). `viClose` at the end.

**Success stdout (default, continue-on-error):**
```json
{
  "ok": true, "address": "GPIB0::5::INSTR",
  "results": [
    { "command": "*RST", "kind": "write", "bytes_written": 5 },
    { "command": "MEAS:VOLT:DC?", "kind": "query", "bytes_written": 14, "response": "+1.234E+00", "bytes_read": 11 }
  ]
}
```
Per-command failures (continue mode) appear inline as `results[i].error` using the §2.2 shape minus the `address`/`command` echo; the envelope stays `"ok": true`.

**With `--stop-on-error`:** envelope is `"ok": false`, §2.2 error fields apply, and `results` holds everything that succeeded before the failure plus the failed command's `error`. Envelope-level context: `address`, `command` (the failing one).

**Argument-validation errors** (`error: "usage"`, exit 2): neither `--cmd` nor `--file`; both supplied; `--file` unreadable; empty `--cmd` value. Messages per the predecessor wording.

---

### 3.7 `clear`

**Usage:** `clear <address> [--timeout <ms>] [serial flags]`
**VISA calls:** `viOpen`, `viClear`, `viClose`.
**Success:** `{ "ok": true, "address": "GPIB0::5::INSTR", "operation": "clear" }`
**Error context fields:** `address`.

### 3.8 `trigger`

**Usage:** `trigger <address> [--timeout <ms>] [serial flags]`
**VISA calls:** `viOpen`, `viAssertTrigger(VI_TRIG_PROT_DEFAULT)`, `viClose`.
**Success:** `{ "ok": true, "address": "GPIB0::5::INSTR", "operation": "trigger" }`
**Error context fields:** `address`.

### 3.9 `serial-poll`

Read the status byte via `viReadSTB`.
**Usage:** `serial-poll <address> [--timeout <ms>] [serial flags]`
**VISA calls:** `viOpen`, `viReadSTB`, `viClose`.
**Success:** `{ "ok": true, "address": "GPIB0::5::INSTR", "status_byte": 64, "status_byte_hex": "0x40", "rqs": true }`
- `status_byte` decimal 0–255; `status_byte_hex` two-digit lowercase with `0x`; `rqs` = `(status_byte & 0x40) != 0`.
**Error context fields:** `address`.

---

### 3.10–3.13 — GPIB bus-control commands (shared rules)

`ifc`, `ren`, `atn`, `gpib-command` operate on a **GPIB board**, not an instrument. They do **not** accept serial flags. The `<board>` argument is normalized by `Cli/BoardId`:

- `^GPIB[0-9]+$` (e.g. `GPIB0`) → `<input>::INTFC`.
- `^GPIB[0-9]+::INTFC$` → input verbatim.
- Anything else → usage error: `"board id must be 'GPIB<N>' or 'GPIB<N>::INTFC'; got '<input>'."` exit 2.

All four echo `board` (original input, verbatim) and `resource` (resolved INTFC string).

**3.10 `ifc`** — `ifc <board> [--timeout <ms>]`. `viOpen` INTFC, `viGpibSendIFC`, `viClose`. Success: `{ "ok": true, "board": "GPIB0", "resource": "GPIB0::INTFC", "operation": "ifc" }`. Error fields: `board`, `resource`.

**3.11 `ren`** — `ren <board> <on|off> [--timeout <ms>]`. `on`→`VI_GPIB_REN_ASSERT`, `off`→`VI_GPIB_REN_DEASSERT`. `viGpibControlREN(mode)`. Success adds `"operation": "ren", "state": "on"`. Error fields: `board`, `resource`, `state`.

**3.12 `atn`** — `atn <board> <on|off> [--timeout <ms>]`. Constants `VI_GPIB_ATN_ASSERT`/`VI_GPIB_ATN_DEASSERT`. Success adds `"operation": "atn", "state": "on"`. Error fields: `board`, `resource`, `state`.

**3.13 `gpib-command`** — `gpib-command <board> --hex <bytes> [--timeout <ms>]`. `--hex` accepts upper/lower, with/without spaces, even digit count. `viGpibCommand`. Success: `{ "ok": true, "board": "GPIB0", "resource": "GPIB0::INTFC", "operation": "gpib-command", "bytes_sent": 3, "hex": "404130" }` (`hex` normalized lowercase no-space). Error fields: `board`, `resource`, `hex`.

---

### 3.14 `get-attr` (new — G-S4)

Read a VISA attribute from a freshly-opened session.

**Usage:** `get-attr <address> --attr <name|0xHEX> [--type <u32|u16|u8|bool|string>] [--timeout <ms>] [serial flags]`

| Arg / flag | Default | Description |
|---|---|---|
| `<address>` | (required) | Resource string. |
| `--attr <a>` | (required) | Symbolic name (e.g. `VI_ATTR_TMO_VALUE`) resolved via `Visa.Core.Protocol.AttrRegistry`, **or** a raw `0xHHHHHHHH` attribute id. |
| `--type <t>` | registry type, else `u32` | Read type. Required only when `--attr` is a raw hex id whose type the registry does not know; for known names the registry type wins and `--type` (if given) must match or it is a usage error. |

**Resolution & typing (`Visa.Core.Protocol.AttrRegistry`, unit-tested — G-S7).** Known names map to `(id, type)`. The set of known names is **exactly the `VI_ATTR_*` rows of the two §6 tables**, with the types listed there; any other attribute must be addressed by raw hex id. A raw hex id resolves to that id with type from `--type` or default `u32`. Read path by type: `u32`/`u16`/`u8` → numeric `viGetAttribute` variant; `bool` → numeric read, value `!= 0`; `string` → buffer `viGetAttribute` variant.

**VISA calls:** `viOpen`, `viGetAttribute`, `viClose`.

**Success stdout:**
```json
{ "ok": true, "address": "ASRL3::INSTR", "attr": "VI_ATTR_ASRL_BAUD", "attr_id": "0x3FFF0021", "type": "u32", "value": 9600 }
```
- `attr` echoes the input token; `attr_id` is the resolved id as `0xHHHHHHHH` (uppercase); `type` is the effective read type. `value` is rendered by type: `u32`/`u16`/`u8` → JSON number; `bool` → JSON boolean (`true`/`false`); `string` → JSON string.

**Error context fields:** `address`, `attr`. Unknown name with no hex form, or `--type` mismatch → usage error exit 2.

---

### 3.15 `set-attr` (new — G-S4)

Write a VISA attribute on a freshly-opened session. **Caveat:** the session is closed immediately after; the effect persists only for attributes the driver retains across sessions. Primarily diagnostic (parent §4).

**Usage:** `set-attr <address> --attr <name|0xHEX> --value <v> [--type <u32|u16|u8|bool>] [--timeout <ms>] [serial flags]`

| Arg / flag | Default | Description |
|---|---|---|
| `<address>` | (required) | Resource string. |
| `--attr <a>` | (required) | As in `get-attr`. |
| `--value <v>` | (required) | Value to write, parsed per the resolved type: numeric types accept decimal or `0x`-hex; `bool` accepts `true`/`false` (lowercase) or `0`/`1`. |
| `--type <t>` | registry type, else `u32` | As in `get-attr`, except `string` is rejected: string attributes are read-only in v1 — `set-attr` with an effective type of `string` (via `--type string` or a string-typed registry name) is a usage error, exit 2 (§6 note). |

**VISA calls:** `viOpen`, `viSetAttribute`, `viClose`.

**Success stdout:**
```json
{ "ok": true, "address": "ASRL3::INSTR", "attr": "VI_ATTR_ASRL_BAUD", "attr_id": "0x3FFF0021", "type": "u32", "value": 115200 }
```
`value` echoes the parsed value (JSON number / boolean / string). **Error context fields:** `address`, `attr`. Bad `--value` for the type → usage error exit 2.

---

### 3.16 `usb-control-in` (new — G-S5)

Low-level USB-TMC control-pipe read (`viUsbControlIn`). For USB resources only.

**Usage:** `usb-control-in <address> --request-type <n> --request <n> --value <n> --index <n> --length <n> [--timeout <ms>]`

| Flag | Description |
|---|---|
| `--request-type <n>` | `bmRequestType` (signed 16-bit; decimal or `0x`-hex). |
| `--request <n>` | `bRequest` (signed 16-bit). |
| `--value <n>` | `wValue` (unsigned 16-bit, `0–65535`). |
| `--index <n>` | `wIndex` (unsigned 16-bit). |
| `--length <n>` | `wLength` — number of bytes to read back (`0–65535`). |
| `--timeout <ms>` | default `5000`. |

Flag parsing and range validation for the five control params live in `Cli/UsbControlFlags` (shared with `usb-control-out`, unit-tested — G-S7): signed 16-bit for `--request-type`/`--request`, unsigned 16-bit for `--value`/`--index`/`--length`; decimal or `0x`-hex accepted; out-of-range or malformed → usage error, exit 2.

**VISA calls:** `viOpen`, `viUsbControlIn`, `viClose`.

**Success stdout:**
```json
{ "ok": true, "address": "USB0::0x0957::0x2C07::MY12345::INSTR", "bytes_read": 2, "hex": "0140" }
```
- `hex` — bytes read back, normalized lowercase no-space. `bytes_read` — `retCnt` from `viUsbControlIn`.
- The returned data is small control-pipe metadata, so it is returned **inline as hex** (not to a file). **Error context fields:** `address`.

---

### 3.17 `usb-control-out` (new — G-S5)

Low-level USB-TMC control-pipe write (`viUsbControlOut`).

**Usage:** `usb-control-out <address> --request-type <n> --request <n> --value <n> --index <n> [--hex <bytes>] [--timeout <ms>]`

| Flag | Description |
|---|---|
| `--request-type`, `--request`, `--value`, `--index` | As in `usb-control-in` (parsed by the shared `Cli/UsbControlFlags`). |
| `--hex <bytes>` | Optional data payload to send (even-digit hex, upper/lower, spaces allowed). `wLength` is derived from the byte count. If omitted, a zero-length control-out is sent. |
| `--timeout <ms>` | default `5000`. |

**VISA calls:** `viOpen`, `viUsbControlOut`, `viClose`.

**Success stdout:**
```json
{ "ok": true, "address": "USB0::...::INSTR", "bytes_sent": 2, "hex": "0140" }
```
`hex` normalized; `bytes_sent` = derived `wLength`. **Error context fields:** `address`, `hex`.

---

### 3.18 `read-block` (new — G-S6)

Send a query and read a binary IEEE 488.2 block to a file. Framing logic: [TDDBinaryTransfer.md](TDDBinaryTransfer.md) §4.

**Usage:** `read-block <address> <command> --out-file <path> [--raw] [--timeout <ms>] [serial flags]`

| Arg / flag | Default | Description |
|---|---|---|
| `<address>` | (required) | Resource string. |
| `<command>` | (required) | Query command sent before the read (e.g. `CURV?`); `\n` appended if missing. |
| `--out-file <path>` | (required) | Destination file for the payload bytes. Created/truncated. Relative resolves against cwd. |
| `--raw` | (off) | Skip 488.2 parsing; write the entire raw response to `--out-file`. |
| `--timeout <ms>` | `5000` | VISA timeout. |

`--term-char` is **not** accepted (binary read disables termchar) — passing it is a usage error.

**VISA calls:** `viOpen`, set timeout + serial, `viWrite(command)`, `ReadRaw` (termchar matching disabled), `viClose`.

**Success stdout:**
```json
{ "ok": true, "address": "USB0::...::INSTR", "command": "CURV?", "out_file": "wave.bin", "format": "definite", "declared_length": 2048, "bytes_written": 2048 }
```
- `format` ∈ `"definite"` | `"indefinite"` | `"raw"`. `declared_length` is the parsed definite length, or `null` for indefinite/raw. `bytes_written` = payload bytes written to the file.
- Parse failure (no valid header and `--raw` not set) → usage error `operation: "parse_block"`, exit 2, file untouched, `description` suggests `--raw`.

**Error context fields:** `address`, `command`, `out_file`.

---

### 3.19 `write-block` (new — G-S6)

Write a binary IEEE 488.2 block from a file. Framing logic: [TDDBinaryTransfer.md](TDDBinaryTransfer.md) §5.

**Usage:** `write-block <address> --in-file <path> [<command>] [--raw] [--timeout <ms>] [serial flags]`

| Arg / flag | Default | Description |
|---|---|---|
| `<address>` | (required) | Resource string. |
| `--in-file <path>` | (required) | Source file of payload bytes. Must exist/readable. Relative resolves against cwd. |
| `<command>` | — | Optional ASCII prefix sent before the block (e.g. `"TRACE:DATA "`). Sent verbatim, no auto-`\n`. |
| `--raw` | (off) | Write the file bytes verbatim (no 488.2 header). |
| `--timeout <ms>` | `5000` | VISA timeout. |

**VISA calls:** `viOpen`, set timeout + serial, `WriteRaw(prefix + header + payload + EOI \n)`, `viClose`. (`--raw`: `prefix + payload` — **no trailing `\n` is appended**; EOI is asserted on the final payload byte by the normal write path.)

**Success stdout:**
```json
{ "ok": true, "address": "USB0::...::INSTR", "in_file": "wave.bin", "format": "definite", "payload_bytes": 2048, "bytes_written": 2055 }
```
- `format` ∈ `"definite"` | `"raw"`. `payload_bytes` = file byte count. `bytes_written` = total wire bytes (prefix + header + payload + terminator).
- `--in-file` missing/unreadable → usage error exit 2 with the IO reason.

**Error context fields:** `address`, `in_file`.

---

## 4. Argument parsing rules

Uniform across subcommands (carried over, extended):

- First positional after the subcommand is the address (or board for bus-control). Flags may appear anywhere after.
- All flags long-form (`--flag-name`). No short flags.
- Flag values are the next argv element; `--flag=value` is not supported.
- Repeatable flags: only `--cmd` on `script`. Every other flag passed twice is a usage error.
- Unknown flags (including a serial flag on a subcommand that does not accept it, or `--term-char` on a block subcommand) → usage error.
- Missing required argument → usage error.
- Extra positional beyond what the subcommand consumes → `{ "ok": false, "error": "usage", "operation": "argparse", "description": "Unexpected argument 'foo'." }` exit 2.
- `--help` on any subcommand: one-paragraph description + flag table to stderr, exit 0 with `{ "ok": true, "subcommand": "<name>", "help": "..." }` on stdout.
- Top-level `--help` and the bare `dotnet run --project skills/visa/scripts/Visa --` (no args): usage summary listing every subcommand to stderr, `{ "ok": true, "help": "...", "subcommands": ["list","info",...,"read-block","write-block"] }` to stdout, exit 0.
- Unknown subcommand: `{ "ok": false, "error": "usage", "operation": "dispatch", "description": "Unknown subcommand 'foo'. Run with --help for the list." }` exit 2.

## 5. Exit-code table

| Code | Class (`error`) | Trigger |
|---|---|---|
| 0 | — | Stdout JSON `"ok": true`. |
| 1 | `visa` | Any VISA C function returned a negative status. |
| 2 | `usage` | Argv parse failure, unknown subcommand, missing/extra arg, bad flag value, invalid hex, bad enum, invalid serial value, bad `--attr`/`--type`/`--value`, block parse failure, unreadable `--in-file`. |
| 3 | `visa_library_missing` | `DllNotFoundException` / `BadImageFormatException` loading `visa32`/`visa64`. |
| 4 | `internal` | Any unhandled exception not classified above. |

Note: `script` without `--stop-on-error` may have per-command VISA errors and still exit 0.

## 6. Attribute & constants reference

Defined as `internal const uint` (or `ushort`) in `Visa.Core/Session/VisaNative.cs`. Carried over from the predecessor (the **Type** column gives the attribute's native VISA type for `VI_ATTR_*` rows; `—` marks plain constants that are not attributes):

| Name | Value | Type | Used for |
|---|---|---|---|
| `VI_ATTR_TMO_VALUE` | `0x3FFF001A` | U32 | Per-session timeout. |
| `VI_ATTR_TERMCHAR` | `0x3FFF0018` | U8 | Read termination char. |
| `VI_ATTR_TERMCHAR_EN` | `0x3FFF0038` | Boolean | Enable termchar matching. Default is interface-aware (see §3.3); set 0 for binary reads. |
| `VI_ATTR_SEND_END_EN` | `0x3FFF0016` | Boolean | Send EOI on writes; cleared by `--no-eoi`. |
| `VI_FIND_BUFLEN` | `256` | — | Buffer size for `viFindRsrc`/`viFindNext`. |
| `VI_TRIG_PROT_DEFAULT` | `0` | — | `viAssertTrigger` protocol. |
| `VI_GPIB_REN_DEASSERT` / `_ASSERT` | `0` / `1` | — | `viGpibControlREN` mode. |
| `VI_GPIB_ATN_DEASSERT` / `_ASSERT` | `0` / `1` | — | `viGpibControlATN` mode. |

New this session (serial, G-S3):

| Name | Value | Type | Used for |
|---|---|---|---|
| `VI_ATTR_ASRL_BAUD` | `0x3FFF0021` | U32 | Serial baud rate. |
| `VI_ATTR_ASRL_DATA_BITS` | `0x3FFF0022` | U16 | Serial data bits (5–8). |
| `VI_ATTR_ASRL_PARITY` | `0x3FFF0023` | U16 | Serial parity (0–4). |
| `VI_ATTR_ASRL_STOP_BITS` | `0x3FFF0024` | U16 | Serial stop bits (10/15/20 = 1/1.5/2). |
| `VI_ATTR_ASRL_FLOW_CNTRL` | `0x3FFF0025` | U16 | Serial flow control (0/1/2/4). |

(Values match the IVI-Foundation VISA spec `visatype.h`. The builder must confirm each constant against the installed VISA header during the serial task; if any differs, that is a design question for the designer, not a silent change.)

**`AttrRegistry` contents (G-S4):** the registry contains **exactly the `VI_ATTR_*` rows of the two tables above** — the four carried-over attributes and the five ASRL attributes — with the types listed in their Type columns. Every other attribute is reachable only as a raw hex id with an explicit (or defaulted `u32`) `--type`.

P/Invoke signatures (**StdCall**, `ExactSpelling = true`, `visa32`/`visa64` via the existing `DllImportResolver`). VISA exports are `__stdcall` (`_VI_FUNC`) on Win32; the predecessor declared `CallingConvention.Cdecl`, which only worked because the x64 ABI ignores the designation — this session corrects every signature to `CallingConvention.StdCall` (correct on both bitnesses) during the carry-over. Carried over: `viOpenDefaultRM`, `viOpen`, `viClose`, `viSetAttribute(uint,uint,uint)`, `viWrite`, `viRead`, `viStatusDesc`, `viFindRsrc`, `viFindNext`, `viClear`, `viAssertTrigger`, `viReadSTB`, `viGpibSendIFC`, `viGpibControlREN`, `viGpibControlATN`, `viGpibCommand`. New this session:

```
int viGetAttribute(uint vi, uint attrName, out uint  attrValue);   // u32 attrs
int viGetAttribute(uint vi, uint attrName, out ushort attrValue);  // u16 attrs
int viGetAttribute(uint vi, uint attrName, out byte  attrValue);   // u8  attrs
int viGetAttribute(uint vi, uint attrName, byte[] attrValue);      // string attrs (caller-sized buffer)
int viUsbControlOut(uint vi, short bmReqType, short bRequest, ushort wValue, ushort wIndex, ushort wLength, [In] byte[] buf);
int viUsbControlIn (uint vi, short bmReqType, short bRequest, ushort wValue, ushort wIndex, ushort wLength, [Out] byte[] buf, out ushort retCnt);
```

> Note on `viGetAttribute` overloads: VISA's `viGetAttribute` is a single C entry taking a `void*` out-buffer that must be the **size of the attribute's native type**. The four managed declarations above are the same `viGetAttribute` symbol marshalled with width-matched out-buffers; the builder picks the overload by the resolved attribute `type` (`u32`→`uint`, `u16`→`ushort`, `u8`→`byte`, `string`→`byte[]`). `bool` reads use the `u16` overload and compare `!= 0`. Do **not** pass a 32-bit out-param for a 16-bit attribute — the buffer-size mismatch is a real defect. `viSetAttribute(uint,uint,uint)` (already present) handles U32/U16/U8/bool set via widening to `uint` (VISA set is always by `ViAttrState`). Known and accepted: on 64-bit VISA `ViAttrState` is a 64-bit type, so the third parameter is technically under-declared as `uint`; this is safe on Win-x64 (32-bit register writes zero-extend to 64 bits) and is carried over deliberately from the predecessor — the builder must **not** widen it. String `set-attr` is a non-goal for v1 — `set-attr` with an effective `string` type is a usage error (no writable string attributes are in scope).

## 7. Logging format (stderr)

One line per VISA call:

```
[visa] <opname> <context> <result> <status>
```

- `<opname>`: VISA function name (`viOpen`, `viWrite`, `viUsbControlIn`, …).
- `<context>`: short, op-specific — resource address + payload summary (e.g. `USB0::...::INSTR 2048 bytes` for a block read).
- `<result>`: `ok` or `err`.
- `<status>`: hex status `0xHHHHHHHH`; for meaningful warning codes (e.g. `VI_SUCCESS_TERM_CHAR = 0x3FFF0005`) append a symbolic suffix in parentheses: `0x3FFF0005 (termchar)`.

Verbosity via `VISA_LOG_LEVEL`: `quiet` (no output), `info` (default — one line per VISA call), `debug` (also log write/read byte payloads as hex; binary block payloads are logged as a byte count + leading bytes only, not the full capture).

## 8. Versioning

This document and the parent TDD describe **v1** of the visa-skill. Adding a new subcommand, flag, or error class is a TDD amendment opened as a refactor session under `docs/build-session/<session-name>/`; the build agent must not invent these inline.

## 9. SKILL.md structure — progressive disclosure

`skills/visa/SKILL.md` summarizes this contract for Claude's runtime context. To keep the always-loaded doc small, the subcommand surface is split in two:

**Inline in `SKILL.md` (common commands):** `list`, `info`, `query`, `write`, `read`, `script`, `clear`, `trigger`, `serial-poll`, `read-block`, `write-block`, plus the shared serial-options flag group (§3.0). `SKILL.md` follows the predecessor's structure: frontmatter (name `visa`, description covering all transports **including the advanced capabilities** so the skill still triggers for them), when-to-use, prerequisites, invocation form, the subcommand table for the inline commands, flag rules, common examples, the JSON output contract, the exit-code table, and stderr logging (`VISA_LOG_LEVEL`).

**Extracted to `skills/visa/references/advanced.md` (uncommon commands, loaded on demand):** `ifc`, `ren`, `atn`, `gpib-command` (GPIB bus control), `get-attr`, `set-attr` (attribute access), `usb-control-in`, `usb-control-out` (USB-TMC control transfers). The reference file documents these at the same depth as the inline table (synopsis, key flags, success-envelope fields, one example each) and restates only the rules that differ from `SKILL.md` (e.g. board-id normalization, attr/type resolution, hex rules).

`SKILL.md` must contain an **"Advanced commands"** section with one line per extracted command (name + one-phrase purpose) and an explicit instruction to read `references/advanced.md` before using any of them. The wire contract for every command — inline or extracted — remains this document; both skill docs are summaries.

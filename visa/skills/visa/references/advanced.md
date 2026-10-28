# visa — advanced commands

This file documents the eight commands listed in the "Advanced commands" section of [`SKILL.md`](../SKILL.md). Read `SKILL.md` first for prerequisites, invocation form, flag rules, JSON output format, and exit codes — those rules apply here unchanged. Only rules that differ from `SKILL.md` are restated below.

---

## GPIB bus-control commands

`ifc`, `ren`, `atn`, and `gpib-command` operate on a **GPIB board**, not an instrument. The positional argument is a `<board>`, not an instrument address, and **serial flags are not accepted** (usage error if passed).

**Board-id normalization:** `GPIB<N>` is accepted and expanded to `GPIB<N>::INTFC` internally. `GPIB<N>::INTFC` is also accepted verbatim. Anything else is a usage error (exit 2).

All four commands echo `board` (your input, verbatim) and `resource` (the resolved `GPIB<N>::INTFC` string) in both success and error envelopes.

---

### `ifc` — Interface Clear

Pulses the GPIB Interface Clear line (`viGpibSendIFC`), unaddressing all devices.

**Synopsis:** `ifc <board> [--timeout <ms>]`

**Key flags:** `--timeout` (ms, default 5000). No serial flags.

**Success envelope:**
```json
{ "ok": true, "board": "GPIB1", "resource": "GPIB1::INTFC", "operation": "ifc" }
```

**Example:**
```powershell
dotnet run --project "${CLAUDE_PLUGIN_ROOT}/skills/visa/scripts/Visa" -- ifc GPIB1
```

---

### `ren` — Remote Enable

Drives the GPIB Remote Enable line on or off (`viGpibControlREN`).

**Synopsis:** `ren <board> <on|off> [--timeout <ms>]`

**Key flags:** positional `<on|off>` (required); `--timeout`. No serial flags.

**Success envelope:**
```json
{ "ok": true, "board": "GPIB1", "resource": "GPIB1::INTFC", "operation": "ren", "state": "on" }
```

**Example:**
```powershell
dotnet run --project "${CLAUDE_PLUGIN_ROOT}/skills/visa/scripts/Visa" -- ren GPIB1 on
dotnet run --project "${CLAUDE_PLUGIN_ROOT}/skills/visa/scripts/Visa" -- ren GPIB1 off
```

---

### `atn` — Attention

Drives the GPIB ATN line on or off (`viGpibControlATN`).

**Synopsis:** `atn <board> <on|off> [--timeout <ms>]`

**Key flags:** positional `<on|off>` (required); `--timeout`. No serial flags.

**Success envelope:**
```json
{ "ok": true, "board": "GPIB1", "resource": "GPIB1::INTFC", "operation": "atn", "state": "on" }
```

**Example:**
```powershell
dotnet run --project "${CLAUDE_PLUGIN_ROOT}/skills/visa/scripts/Visa" -- atn GPIB1 on
```

---

### `gpib-command` — Raw GPIB command bytes

Sends raw GPIB command bytes via `viGpibCommand`. Use to issue Talker/Listener address bytes, universal commands (UNL, UNT, etc.), or other GPIB bus-level bytes that no higher-level VISA call exposes.

**Synopsis:** `gpib-command <board> --hex <bytes> [--timeout <ms>]`

**Key flags:**

| Flag | Description |
|---|---|
| `--hex <bytes>` | (required) Bytes to send. Uppercase or lowercase hex, with or without spaces, even digit count. E.g. `"3F 5F"` or `"3f5f"`. |
| `--timeout <ms>` | Default 5000. |

No serial flags.

**Success envelope:**
```json
{ "ok": true, "board": "GPIB1", "resource": "GPIB1::INTFC", "operation": "gpib-command", "bytes_sent": 2, "hex": "3f5f" }
```
`hex` in the response is normalized to lowercase, no spaces.

**Example:**
```powershell
# Send UNL (0x3F) + UNT (0x5F) — unlisten / untalk
dotnet run --project "${CLAUDE_PLUGIN_ROOT}/skills/visa/scripts/Visa" -- gpib-command GPIB1 --hex "3F 5F"
```

---

## Attribute access

`get-attr` and `set-attr` accept the same `<address>` and serial flags as the common instrument-session commands. The `--attr` flag identifies the attribute by symbolic name or raw hex id.

**Attribute resolution:**
- Symbolic names (e.g. `VI_ATTR_TMO_VALUE`) are resolved via the built-in registry. The registry knows the exact set of `VI_ATTR_*` constants documented in the VISA specification; any attribute outside that set must be addressed by raw hex id.
- Raw hex ids (`0xHHHHHHHH`) resolve to that numeric id. `--type` must be supplied when the registry does not know the type of the id, or defaults to `u32` if absent.
- For known symbolic names, `--type` (if given) must match the registry type or it is a usage error (exit 2).

---

### `get-attr` — Read a VISA attribute

**Synopsis:** `get-attr <address> --attr <name|0xHEX> [--type <u32|u16|u8|bool|string>] [--timeout <ms>] [serial flags]`

**Key flags:**

| Flag | Description |
|---|---|
| `--attr <a>` | (required) Symbolic name or `0x`-prefixed hex id. |
| `--type <t>` | Read type: `u32` (default for raw ids), `u16`, `u8`, `bool`, `string`. Required for unknown raw ids; must match registry type for known names. |
| `--timeout <ms>` | Default 5000. |

**Success envelope:**
```json
{ "ok": true, "address": "GPIB1::5::INSTR", "attr": "VI_ATTR_TMO_VALUE", "attr_id": "0x3FFF001A", "type": "u32", "value": 5000 }
```
- `attr` — echoes your input token.
- `attr_id` — resolved id as `0xHHHHHHHH` (uppercase hex).
- `value` — JSON number for `u32`/`u16`/`u8`, JSON boolean for `bool`, JSON string for `string`.

**Example:**
```powershell
# Read timeout by name
dotnet run --project "${CLAUDE_PLUGIN_ROOT}/skills/visa/scripts/Visa" -- get-attr GPIB1::5::INSTR VI_ATTR_TMO_VALUE

# Read the same attribute by raw hex id
dotnet run --project "${CLAUDE_PLUGIN_ROOT}/skills/visa/scripts/Visa" -- get-attr GPIB1::5::INSTR 0x3FFF001A --type u32
```

---

### `set-attr` — Write a VISA attribute

**Caveat:** The session is closed immediately after the write. The effect persists only for attributes the VISA driver retains across sessions. `set-attr` is primarily diagnostic.

**`string` type is not writable in v1:** Using `--type string` or naming a string-typed registry attribute is a usage error (exit 2).

**Synopsis:** `set-attr <address> --attr <name|0xHEX> --value <v> [--type <u32|u16|u8|bool>] [--timeout <ms>] [serial flags]`

**Key flags:**

| Flag | Description |
|---|---|
| `--attr <a>` | (required) As in `get-attr`. |
| `--value <v>` | (required) Value to write. Numeric types accept decimal or `0x`-hex. `bool` accepts `true`/`false` or `0`/`1`. |
| `--type <t>` | `u32` (default), `u16`, `u8`, `bool`. `string` is rejected. |
| `--timeout <ms>` | Default 5000. |

**Success envelope:**
```json
{ "ok": true, "address": "GPIB1::5::INSTR", "attr": "VI_ATTR_TMO_VALUE", "attr_id": "0x3FFF001A", "type": "u32", "value": 10000 }
```
`value` echoes the parsed value.

**Example:**
```powershell
# Set timeout to 10 seconds
dotnet run --project "${CLAUDE_PLUGIN_ROOT}/skills/visa/scripts/Visa" -- set-attr GPIB1::5::INSTR VI_ATTR_TMO_VALUE 10000
```

---

## USB-TMC control transfers

`usb-control-in` and `usb-control-out` perform low-level USB control-pipe transfers (`viUsbControlIn` / `viUsbControlOut`). These are for USB RAW and USB-TMC resources only.

**Serial flags are not accepted** — these commands open the USB resource directly and serial configuration does not apply.

**Flag parsing:** `--request-type` and `--request` are signed 16-bit integers (range −32768..32767, or equivalently `0x0000..0xFFFF` in hex). `--value`, `--index`, and `--length` are unsigned 16-bit integers (0..65535). Decimal or `0x`-prefixed hex accepted; out-of-range or malformed → usage error (exit 2).

---

### `usb-control-in` — USB control-pipe read

**Synopsis:** `usb-control-in <address> --request-type <n> --request <n> --value <n> --index <n> --length <n> [--timeout <ms>]`

**Key flags:**

| Flag | Description |
|---|---|
| `--request-type <n>` | `bmRequestType` — signed 16-bit. |
| `--request <n>` | `bRequest` — signed 16-bit. |
| `--value <n>` | `wValue` — unsigned 16-bit (0–65535). |
| `--index <n>` | `wIndex` — unsigned 16-bit. |
| `--length <n>` | `wLength` — bytes to read back, unsigned 16-bit. Required. |
| `--timeout <ms>` | Default 5000. |

**Success envelope:**
```json
{ "ok": true, "address": "USB0::0x0957::0x0D0A::MY12345::INSTR", "bytes_read": 4, "hex": "00000140" }
```
`hex` is lowercase, no spaces. Data is returned inline (not to a file) because USB control-pipe payloads are small metadata.

**Example:**
```powershell
dotnet run --project "${CLAUDE_PLUGIN_ROOT}/skills/visa/scripts/Visa" -- usb-control-in USB0::0x0957::0x0D0A::MY12345::INSTR `
  --request-type 0xA1 --request 0x01 --value 0 --index 0 --length 4
```

---

### `usb-control-out` — USB control-pipe write

**Synopsis:** `usb-control-out <address> --request-type <n> --request <n> --value <n> --index <n> [--hex <bytes>] [--timeout <ms>]`

**Key flags:**

| Flag | Description |
|---|---|
| `--request-type`, `--request`, `--value`, `--index` | As in `usb-control-in`. |
| `--hex <bytes>` | Optional data payload. Even-digit hex, upper/lower, spaces allowed. `wLength` is derived from the byte count. If omitted, a zero-length control-out is sent. |
| `--timeout <ms>` | Default 5000. |

**Success envelope:**
```json
{ "ok": true, "address": "USB0::0x0957::0x0D0A::MY12345::INSTR", "bytes_sent": 3, "hex": "aabbcc" }
```
`hex` normalized lowercase no-space; `bytes_sent` = derived `wLength`.

**Example:**
```powershell
dotnet run --project "${CLAUDE_PLUGIN_ROOT}/skills/visa/scripts/Visa" -- usb-control-out USB0::0x0957::0x0D0A::MY12345::INSTR `
  --request-type 0x21 --request 0x09 --value 0x0300 --index 0 --hex "AA BB CC"
```

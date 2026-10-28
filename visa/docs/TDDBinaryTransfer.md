# TDDBinaryTransfer — visa-skill binary block transfer

**Owner:** kosmic@gmail.com
**Last updated:** 2026-06-13
**Parent TDD:** [TDD.md](TDD.md)

---

This sub-TDD specifies **G-S6** — binary block transfer. It covers the IEEE 488.2 arbitrary-block framing, the binary read/write VISA paths, the `--raw` escape, and the `read-block` / `write-block` algorithms. The wire-level JSON shapes for the two subcommands live in [TDDCliContract.md](TDDCliContract.md) §3; this document is the authoritative source for the **framing and I/O logic** behind them.

## 1. Why binary transfer is different from text I/O

The carried-over text path (`query` / `read`) reads ASCII with termination-character matching enabled (`VI_ATTR_TERMCHAR_EN=1`), stops on the termchar byte, and trims trailing `\r\n`. Binary block payloads (waveforms, screenshots, spectra) can contain **any byte value, including the termchar (0x0A) and 0x00**, so:

- Termchar matching MUST be disabled for binary reads (`VI_ATTR_TERMCHAR_EN=0`); the read terminates on EOI / declared length, not on a byte value.
- The bytes MUST NOT pass through `Encoding.ASCII` — they are moved as raw `byte[]`, never as `string`.
- The payload is delivered to a **file** (`--out-file` / `--in-file`), never embedded in the JSON envelope (per the parent §7 and the user's file-only decision). The JSON reports counts and metadata only.

> **Layering (G-S8).** The framing logic (`BlockHeader`) and the raw session paths (`ReadRaw`/`WriteRaw`) both live in the reusable `Visa.Core` library, not in the CLI: `BlockHeader` is `Visa.Core.Protocol.BlockHeader`; the raw read/write are methods on `Visa.Core.Session.IVisaSession` / `VisaSession`. The CLI's `ReadBlockCommand`/`WriteBlockCommand` only orchestrate argv → file I/O → these Core APIs.

## 2. IEEE 488.2 arbitrary block format

Instruments return binary data wrapped in one of two IEEE 488.2 arbitrary-block forms:

### 2.1 Definite-length block

```
#  N  D D ... D   <payload bytes>   [trailing terminator]
│  │  └────────┘  └──────────────┘
│  │   N digits     exactly <declared> bytes
│  │   giving the
│  │   byte count
│  └─ a single ASCII digit 1..9 = how many length digits follow
└─ literal '#'
```

Example: `#42048<2048 bytes>` → `N=4`, the next 4 ASCII digits `2048` give the payload length, followed by exactly 2048 payload bytes. A trailing terminator (often `\n`, sometimes with EOI) may follow the payload and is **not** part of the payload.

### 2.2 Indefinite-length block

```
#  0   <payload bytes...>   <LF with EOI asserted>
│  │   └────────────────┘
│  │    payload, length unknown up front
│  └─ digit '0' signals indefinite length
└─ literal '#'
```

`#0` means "read until the instrument asserts EOI on the final byte (conventionally an `\n`)". The terminating `\n` is **not** part of the payload.

### 2.3 `Visa.Core.Protocol.BlockHeader` (pure, unit-tested — G-S7)

`BlockHeader` is pure logic over a `byte[]`/`ReadOnlySpan<byte>`; it performs no I/O. It provides:

- **Parse:** given the accumulated response bytes, locate and validate the leading header. Behavior:
  - First non-whitespace byte must be `#` (0x23). Leading ASCII whitespace before `#` is skipped (some instruments emit a leading space). If the first non-whitespace byte is not `#` → parse failure (caller maps to a usage/parse error suggesting `--raw`).
  - If the digit after `#` is `1`–`9` (`N`): read the next `N` ASCII digits as a base-10 `declared length`; the payload starts immediately after those digits and is exactly `declared` bytes long. Returns `(payloadOffset, declaredLength, isIndefinite=false)`.
  - If the digit after `#` is `0`: indefinite length. Payload starts immediately after `#0`; payload end is `(total bytes) − 1` trailing terminator **iff** the last byte is `\n` or `\r`, else all remaining bytes. Returns `(payloadOffset, declaredLength=null, isIndefinite=true)`.
  - Malformed (`#` not followed by a digit; fewer than `N` length digits available; declared length exceeds bytes actually read) → parse failure with a specific message.
- **Format (for `write-block`):** given a payload length, produce the definite-length header bytes `#<N><digits>` where `N` is the digit count of the decimal length. E.g. length `2048` → `#42048`. Only definite-length headers are generated on write (indefinite-length writes are a non-goal).

`BlockHeader` does not strip or validate trailing terminators beyond the indefinite-length rule above; for definite-length the declared count is authoritative and trailing bytes after `payloadOffset + declared` are ignored.

## 3. VISA session binary paths

Added to `Visa.Core.Session.IVisaSession` / `VisaSession` (parent §5.1):

- **`ReadRaw(out uint bytesRead) -> byte[]`** — binary read loop:
  1. Save current termchar-enable state; set `VI_ATTR_TERMCHAR_EN = 0`.
  2. Read in a loop with a pooled buffer (reuse the predecessor's 1024-byte `ArrayPool` pattern, or a larger buffer for throughput), appending each chunk to a growable byte accumulator (`MemoryStream` or a `List<byte>`-equivalent). Loop semantics mirror the text read loop but over raw bytes: continue on `VI_SUCCESS_MAX_CNT`; stop on `VI_SUCCESS` (EOI / count satisfied); negative status throws via `VisaStatus.Throw`.
  3. Restore the prior termchar-enable state before returning (so the session is left consistent; in practice the session is closed immediately after).
  4. Return the accumulated raw bytes and total count. No ASCII conversion, no trimming.
- **`WriteRaw(byte[] bytes) -> uint`** — already present on the predecessor interface; writes the bytes verbatim via `viWrite`, returns the wire byte count. Reused unchanged for `write-block`.

The 488.2 parse is applied by the **command handler** to the bytes `ReadRaw` returns, via `Visa.Core.Protocol.BlockHeader` — the session layer stays framing-agnostic.

## 4. `read-block` algorithm

`read-block <address> <command> --out-file <path> [--timeout <ms>] [--raw] [serial flags]`

1. Validate `<address>`, `<command>`, and required `--out-file`. (Missing `--out-file` → usage error.)
2. Open the session; apply serial options if supplied; set timeout.
3. `viWrite` the command (ASCII, auto-append `\n` if absent — same rule as text write).
4. `ReadRaw` → full raw response bytes `R`.
5. If `--raw`: write **all** of `R` to `--out-file`; `format = "raw"`, `declared_length = null`, `bytes_written = R.Length`.
6. Else: `BlockHeader.Parse(R)`:
   - On success: write the payload slice (`R[payloadOffset ..]`, length = `declaredLength` for definite, or the indefinite-length-trimmed slice) to `--out-file`; `format = "definite"` or `"indefinite"`, `declared_length = declaredLength` (null for indefinite), `bytes_written` = payload byte count.
   - On parse failure: emit a **usage** error (`error: "usage"`, operation `"parse_block"`, exit 2) whose `description` names the failure and suggests `--raw`; nothing is written to the file.
7. Success envelope per [TDDCliContract.md](TDDCliContract.md) §3 (`read-block`).

File write: open `--out-file` for create/truncate, write the payload bytes, flush, close. Relative paths resolve against cwd.

## 5. `write-block` algorithm

`write-block <address> --in-file <path> [<command>] [--timeout <ms>] [--raw] [serial flags]`

> `<command>` is an **optional** ASCII prefix (e.g. `"TRACE:DATA "`) sent immediately before the block. Provide it when the instrument expects a SCPI header preceding the data; omit it when the resource expects bare block bytes.

1. Validate `<address>` and required `--in-file` (file must exist and be readable → else usage error with the IO reason). `<command>` is optional.
2. Read the file bytes `P` into memory.
3. Build the write buffer `W`:
   - ASCII prefix bytes for `<command>` if supplied (no auto-`\n`; the caller includes any needed separator inside the prefix string).
   - If `--raw`: append `P` verbatim (no 488.2 header and **no trailing `\n`** — the file bytes end the message; EOI is asserted on the final byte by the normal write path).
   - Else: append `BlockHeader.Format(P.Length)` then `P`, then a trailing `\n` with EOI so the instrument sees end-of-message — same EOI behavior as a normal write.
4. Open the session; apply serial options; set timeout; `WriteRaw(W)`.
5. Success envelope per [TDDCliContract.md](TDDCliContract.md) §3 (`write-block`) — reports `in_file`, `payload_bytes` (`P.Length`), `bytes_written` (`W` wire count), and `format` (`"definite"` or `"raw"`).

## 6. Edge cases and rules

- **Empty payload on write** — `--in-file` of zero bytes: definite header `#10` then EOI; valid. `--raw` writes only the prefix (if any).
- **Declared length exceeds bytes read** (instrument under-delivered, e.g. timeout mid-block) — `BlockHeader.Parse` reports the shortfall; handler emits the parse error. (The underlying `viRead` timeout, if it fires first, surfaces as a normal VISA error from `ReadRaw`.)
- **Indefinite-length on `read-block`** — supported on read (auto-detected). **Not** supported on `write-block` (definite-length only; a non-goal to generate `#0` writes).
- **`--term-char` is not offered on block subcommands** — binary reads disable termchar matching by design; accepting the flag would be misleading. Passing it is a usage error (unknown flag).
- **Binary payloads never touch the JSON envelope** — only counts, the file path, and the detected `format` appear in JSON.

## 7. Test coverage (G-S7)

`Protocol/BlockHeaderTests` (xUnit, no driver) covers, at minimum:
- Definite parse: `#42048…` → offset 6, length 2048; single-digit `#15` → offset 3, length 5.
- Indefinite parse: `#0<payload>\n` → indefinite, trailing `\n` excluded.
- Leading-whitespace tolerance before `#`.
- Malformed: no `#`; `#` not followed by a digit; fewer than `N` digits present; declared length > available bytes.
- Format: lengths 0, 5, 2048, and a length whose digit count rolls over (e.g. 9 → `#19`, 10 → `#210`).

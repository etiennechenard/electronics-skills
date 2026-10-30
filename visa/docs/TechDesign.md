# Technical Design Document — visa

**Owner:** kosmic@gmail.com
**Last updated:** 2026-06-13

> Main TechDesign — describes the **current state** of the `visa` plugin as shipped by the initial build session. The CLI wire contract and the binary-transfer framing design are split into sibling sub-TechDesigns (see §11). The session's design intent (with mid-build amendments) is preserved in the project's own build-session record, which is not shipped with this plugin.

---

## 1. Requirements

**Problem this software solves.**
Claude needs to drive *any* VISA-addressable laboratory instrument — over GPIB, USB-TMC, Serial/RS-232, or TCPIP/LXI — including configuring serial ports, reading/writing arbitrary VISA attributes, performing low-level USB-TMC control transfers, and moving **binary block** payloads (waveforms, screen captures, spectra) to and from disk. The predecessor `gpib` plugin covers text SCPI I/O and GPIB bus control but is GPIB-branded, cannot configure serial ports, exposes no attribute access, and explicitly excludes binary transfer. This session produces a generic, rebranded `visa` plugin that keeps every existing capability and adds the missing ones.

**Users and use cases.**
The only direct caller is Claude Code, which reads `skills/visa/SKILL.md`, learns the CLI surface, and shells out via the `Bash`/`PowerShell` tool, parsing the single JSON object printed to stdout. Uncommon commands (GPIB bus control, attribute access, USB control transfers) are documented in `skills/visa/references/advanced.md`, which Claude loads on demand when those capabilities are needed ([TechDesignCliContract.md](TechDesignCliContract.md) §9). The human operator invokes the CLI directly only when debugging.

**Required capabilities (functional).**

- Enumerate VISA resources matching a search expression (default `?*INSTR`).
- Open any VISA-addressable instrument by resource string (GPIB, USB, Serial/ASRL, TCPIP/LXI, PXI).
- Synchronous text write, text read, and write-then-read query. Read termination is interface-aware by default — EOI/END for buses that carry it (GPIB, USB-TMC, VXI/PXI, TCPIP INSTR), termination-char matching for those that do not (ASRL serial, TCPIP SOCKET, USB RAW) — with per-call override (`--term-char`, `--term-char-en`).
- Multi-command sequences against a single opened session in one invocation (`script`).
- SCPI device clear, assert trigger, serial-poll status byte.
- GPIB bus-level control on a board: Interface Clear, Remote Enable, ATN, raw command-byte send.
- **Serial/RS-232 (ASRL) port configuration** — baud rate, data bits, parity, stop bits, flow control — applied on the same session that performs I/O.
- **Generic VISA attribute access** — read (`get-attr`) and write (`set-attr`) any VISA attribute by symbolic name or raw hex id.
- **Low-level USB-TMC control transfers** — `usb-control-in` / `usb-control-out` over the USB control pipe, plus access to USB RAW resources by resource string.
- **Binary block transfer** — `read-block` and `write-block` moving IEEE 488.2 arbitrary block data via files (`--out-file` / `--in-file`), with automatic 488.2 header parse/prepend and a `--raw` escape hatch.
- Single JSON object per invocation on stdout; exit code reflects the success / failure class.

**Hard constraints (non-functional).**

- Windows-only (Windows 10/11 x64). Loads `visa32.dll` / `visa64.dll` via P/Invoke, picking the bitness that matches the running process.
- .NET 8 SDK on `PATH`; the tool runs via `dotnet run`, not as a precompiled binary.
- Requires a system-installed VISA implementation (NI-VISA, Keysight, R&S — any conforming `visa32`/`visa64`). No VISA binaries are vendored.
- Single invocation form: `dotnet run --project skills/visa/scripts/Visa -- <subcommand> [args]`.
- Every subcommand is self-contained: open resource (if applicable), do its work, close, print exactly one JSON object, exit. No daemon, no persistent state, no shared resource manager between invocations.
- Pure-C# logic (argv parsing, JSON envelope, hex parsing, board-id normalization, 488.2 header parse/format, attribute registry, serial/USB param parsing) is unit-tested without a VISA driver.

**Existing systems and integrations.**

- **Predecessor `gpib` plugin** (sibling marketplace plugin at `../gpib`). This session re-creates its full behavior under the `visa` name in a fresh project tree, then extends it. The `gpib` plugin itself is left untouched; `visa` is a new, independent plugin.
- **NI-VISA / vendor VISA library** — external dependency, loaded by name via the `DllImportResolver`.
- **Claude Code plugin system** — manifest at `plugin.json`; skill at `skills/visa/SKILL.md`.

## 2. Context

`visa` is a standalone plugin in the user's plugin marketplace, modeled on the existing `gpib` plugin. It has no runtime dependency on other plugins; it shares only the Windows + .NET 8 + PowerShell toolchain assumption with sibling plugins. The C# CLI under `skills/visa/scripts/Visa/` is run via `dotnet run`; the first invocation pays a JIT/build cost, subsequent invocations from the same shell hit the build cache.

Invocation flow: Claude reads `skills/visa/SKILL.md`, constructs a `dotnet run` invocation, runs it via a shell tool, and parses the stdout JSON to act on the response.

## 3. Goals

Each goal carries a stable greppable ID; sections implementing a goal reference it by token.

- **G-S1 — Rebrand.** The entire surface is `visa`, not `gpib`: namespace `Visa`, project `skills/visa/scripts/Visa`, test project `skills/visa/scripts/Visa.Tests`, solution `skills/visa/scripts/visa.sln`, plugin name `visa`, skill `skills/visa/SKILL.md`, env var `VISA_LOG_LEVEL`, stderr log prefix `[visa]`. No `gpib`/`Gpib` identifier survives except the GPIB-specific bus-control subcommand names and VISA `viGpib*` function names (which are inherently GPIB).
- **G-S2 — Parity.** Every subcommand from the predecessor — `list`, `info`, `query`, `write`, `read`, `script`, `clear`, `trigger`, `serial-poll`, `ifc`, `ren`, `atn`, `gpib-command` — exists with the identical JSON success/error envelope shape and exit-code mapping (modulo the `[visa]` log prefix and field-name rebrand). See [TechDesignCliContract.md](TechDesignCliContract.md).
- **G-S3 — Serial config.** A shared serial-options flag group (`--baud`, `--data-bits`, `--parity`, `--stop-bits`, `--flow-control`) is accepted by every instrument-session-opening subcommand and applied via `VI_ATTR_ASRL_*` before I/O.
- **G-S4 — Attribute access.** `get-attr` reads and `set-attr` writes VISA attributes by symbolic name or raw hex id, with typed value handling. String attributes are read-only: `set-attr --type string` is out of scope for v1 (usage error — see [TechDesignCliContract.md](TechDesignCliContract.md) §6).
- **G-S5 — USB-TMC control.** `usb-control-in` / `usb-control-out` expose `viUsbControlIn` / `viUsbControlOut`; USB RAW resources are openable by resource string.
- **G-S6 — Binary transfer.** `read-block` / `write-block` move binary payloads through `--out-file` / `--in-file`, auto-parsing/prepending IEEE 488.2 block headers with a `--raw` escape. See [TechDesignBinaryTransfer.md](TechDesignBinaryTransfer.md).
- **G-S7 — Test coverage.** The xUnit project covers all new pure-C# logic: 488.2 header parse/format, attribute name→id/type registry, serial-flag parsing, USB control-param parsing — alongside the carried-over `Cli/` + `Json/` + `VisaLogger` tests.
- **G-S8 — Reusable core library.** The VISA wrapper layer is isolated in a standalone `Visa.Core` class library (net8.0, no CLI dependency) so other .NET projects can reference it directly instead of copy-pasting the P/Invoke layer (as this session does from `gpib`). The `Visa` CLI exe and `Visa.Tests` both reference `Visa.Core`. The boundary: `Visa.Core` owns all VISA semantics (the session wrapper, P/Invoke surface, IEEE 488.2 framing, ASRL value encodings, attribute id/type tables); the `Visa` CLI owns argv parsing, command handlers, and JSON output. `VisaLogger` takes its prefix and level as injected configuration — the CLI supplies `[visa]` + the `VISA_LOG_LEVEL`-derived level at startup; the library itself reads no environment variables.

## 4. Non-goals

- No software simulator. Testing happens against real instruments (plus pure-C# unit tests).
- No locking (`viLock` / `viUnlock`).
- No async events / SRQ handler installation (`viInstallHandler`, `viEnableEvent`, `viWaitOnEvent`). Synchronous serial poll is the only SRQ path.
- No formatted I/O (`viPrintf`, `viScanf`).
- No register-based I/O (`viIn8/16/32`, `viOut8/16/32`, `viMoveIn/Out`).
- No cross-platform support (Linux / macOS deferred).
- No precompiled binary distribution.
- No persistent session handles across invocations.
- No `CLAUDE.md` at the project root.
- `set-attr` does **not** guarantee a persistent effect: each invocation opens a fresh session, sets the attribute, and closes. Only attributes the driver retains across sessions persist; the rest reset on close. `set-attr` is primarily diagnostic. (Documented, not a defect.)

## 5. Architecture

```
   Claude Code session
          │  reads
          ▼
   skills/visa/SKILL.md
          │  invokes (via Bash/PowerShell tool)
          ▼
   dotnet run --project skills/visa/scripts/Visa -- <subcommand> [args]
          │
          ▼
   ┌──────────────────────────────────────────────────────────┐
   │ skills/visa/scripts/Visa  (CLI exe — references Visa.Core)            │
   │                                                           │
   │  Program.cs                                               │
   │     │  uses Cli/ArgParser, dispatches to subcommand       │
   │     ▼                                                     │
   │  Commands/  (one handler per subcommand)                  │
   │     │  uses Cli/ helpers + Json/ + Visa.Core              │
   │     ├──> Cli/   (argv → typed config; CLI-only)           │
   │     │     ArgParser  BoardId  HexBytes  TermChar          │
   │     │     ScriptClassification  SerialFlags               │
   │     │     UsbControlFlags  UsageException                 │
   │     └──> Json/  ResultWriter (success / error envelopes)  │
   └──────────────────────────────┬───────────────────────────┘
                                   │  project reference
                                   ▼
   ┌──────────────────────────────────────────────────────────┐
   │ skills/visa/scripts/Visa.Core  (class library — reusable, no CLI dep) │
   │                                                           │
   │  Session/  (VISA wrapper)                                 │
   │     IVisaSession  VisaSession  VisaBoard                  │
   │     VisaNative  VisaStatus  VisaException                 │
   │     VisaLogger  (stderr trace; prefix+level injected)     │
   │  Protocol/  (pure VISA semantics — testable w/o driver)   │
   │     BlockHeader (IEEE 488.2)  SerialConfig  AttrRegistry  │
   └──────────────────────────────┬───────────────────────────┘
                                   │  P/Invoke
                                   ▼
                  visa32.dll / visa64.dll  (NI-VISA / vendor VISA)

   ┌──────────────────────────────────────────────────────────┐
   │ skills/visa/scripts/Visa.Tests  (xUnit — references Visa + Visa.Core) │
   │   covers Cli/ + Json/ (CLI) and Protocol/ + VisaLogger    │
   │   (Core); no VISA driver required.                        │
   └──────────────────────────────────────────────────────────┘
```

The structure derives from the predecessor `gpib` plugin (proven design), rebranded to `Visa` (G-S1), with the VISA wrapper extracted into a standalone `Visa.Core` class library (G-S8), three new units of pure VISA logic (`BlockHeader`, `SerialConfig`, `AttrRegistry`), and a new CLI-side USB control-flag parser (`UsbControlFlags`). The reuse boundary is strict: `Visa.Core` (namespaces `Visa.Core.Session`, `Visa.Core.Protocol`) knows VISA but nothing about the command line; the `Visa` CLI (namespaces `Visa`, `Visa.Cli`, `Visa.Commands`, `Visa.Json`) knows the command line and delegates all VISA work to `Visa.Core`.

### 5.1 Component responsibilities

Components are grouped by project. The `Visa` CLI depends on `Visa.Core`; `Visa.Core` depends on nothing but the VISA driver.

#### Visa.Core (class library — namespaces `Visa.Core.Session`, `Visa.Core.Protocol`) — G-S8

**`Session/IVisaSession.cs`** — Single-session abstraction. Carries the predecessor surface (`Open`, `SetTimeout`, `SetSendEndEnabled`, `SetTermChar`, `SetTermCharEnabled`, `Write`, `WriteRaw`, `ReadLine`, `Clear`, `AssertTrigger`, `ReadStb`) plus this session's additions: `ApplySerial(SerialConfig)` (G-S3), `GetAttribute`/`SetAttribute` typed accessors (G-S4), `UsbControlIn`/`UsbControlOut` (G-S5), and `ReadRaw` / `WriteRaw` binary paths (G-S6). `SetTermCharEnabled` toggles `VI_ATTR_TERMCHAR_EN`, letting a caller override the interface-aware read-termination default. `ReadRaw` reads bytes with termchar matching disabled, until EOI/count, returning the raw byte buffer; the 488.2 parse happens in `Protocol/BlockHeader`, not in the session.

**`Session/VisaSession.cs`** — Concrete `IVisaSession`. Owns the per-process default-RM lazy init, `viOpen`, attribute sets, the text read loop (carried over), and the new operations above. `Open` sets `TERMCHAR=\n` and then `TERMCHAR_EN` from the interface-aware default computed by the internal `TerminatesOnEnd(address)` classifier — off for EOI/END-terminated buses (GPIB, USB-TMC, VXI/PXI, TCPIP INSTR) so the instrument's END terminates the read, on for those without an end indicator (ASRL, TCPIP SOCKET, USB RAW) and any unrecognized prefix (conservative) — then applies a supplied `SerialConfig`. Implements `IDisposable` so each caller's `using` guarantees `viClose`. `ReadRaw` saves and forces `VI_ATTR_TERMCHAR_EN=0` for the read duration (binary data may contain the termchar byte), restoring the prior value afterward. Detail in [TechDesignBinaryTransfer.md](TechDesignBinaryTransfer.md); read-termination contract in [TechDesignCliContract.md](TechDesignCliContract.md) §3.3.

**`Session/VisaBoard.cs`** — Static helpers for GPIB board-level ops (`SendIfc`, `ControlRen`, `ControlAtn`, `Command`) targeting an INTFC resource. (Carried over.)

**`Session/VisaNative.cs`** — P/Invoke surface + the `DllImportResolver` selecting `visa32`/`visa64`. Carries the predecessor P/Invokes; adds `viUsbControlIn`, `viUsbControlOut`, and the width-matched `viGetAttribute` overloads needed for U16/U8/string attribute access (G-S4, G-S5). New attribute constants (`VI_ATTR_ASRL_*`) per [TechDesignCliContract.md](TechDesignCliContract.md) §6.

**`Session/VisaStatus.cs` / `Session/VisaException.cs`** — Status-code constants, `IsError`/`IsWarning` predicates, the `Throw` helper attaching the driver's `viStatusDesc` text. (Carried over.)

**`Session/VisaLogger.cs`** — Configurable stderr tracer (G-S8). Takes its **prefix and level as injected configuration** (e.g. a static `Configure(prefix, level)` called once at process start); it does **not** read environment variables. `Trace(operation, context, status)` + a payload-byte variant for debug, called immediately after each P/Invoke. Stderr only, format in [TechDesignCliContract.md](TechDesignCliContract.md) §7. The CLI configures it with prefix `[visa]` and the level derived from `VISA_LOG_LEVEL`.

**`Protocol/BlockHeader.cs`** (**new, G-S6**) — Pure IEEE 488.2 arbitrary-block framing: parse a leading `#NDDD` definite-length header (and detect `#0` indefinite-length) from a byte span returning `(payloadOffset, declaredLength?, isIndefinite)`; format a definite-length header for a given payload length. No I/O. Detail in [TechDesignBinaryTransfer.md](TechDesignBinaryTransfer.md).

**`Protocol/SerialConfig.cs`** (**new, G-S3**) — Typed serial parameters (baud `uint`, data-bits `byte`, parity enum, stop-bits enum, flow-control enum), each carrying its `VI_ATTR_ASRL_*` id + encoded value, plus the enum value encodings (e.g. parity `even`→2, stop-bits `1.5`→15). Holds only the parameters actually set (unset → not written, driver default stands). Validates ranges/enum values, raising on bad input. Consumed by `VisaSession.ApplySerial`. The string→enum spelling map is here (VISA knowledge); the CLI flag *names* live in `Visa.Cli.SerialFlags`.

**`Protocol/AttrRegistry.cs`** (**new, G-S4**) — Static map from symbolic attribute name (e.g. `VI_ATTR_TMO_VALUE`) to `(uint id, AttrType type)` where `AttrType ∈ {U32, U16, U8, Boolean, String}`; `Resolve(token, typeOverride?)` accepts a known name or a raw `0xHHHHHHHH` hex id, returning `(id, type)`. For raw-hex ids the type defaults to `U32` unless `typeOverride` is given; for known names a conflicting `typeOverride` is an error. The registry's contents are **exactly the `VI_ATTR_*` rows of the attribute tables in [TechDesignCliContract.md](TechDesignCliContract.md) §6**, with the types listed there; all other attributes are reachable only via raw hex id + `--type`. No I/O. Detail in [TechDesignCliContract.md](TechDesignCliContract.md) §3 (get-attr/set-attr).

#### Visa (CLI exe — namespaces `Visa`, `Visa.Cli`, `Visa.Commands`, `Visa.Json`)

**`Program.cs`** — Entry point. Parses argv into `(subcommand, args)`, dispatches to the matching `ICommandHandler` via a registry, catches `VisaException` / `DllNotFoundException` / `UsageException` / unhandled exceptions at the top level, maps each to an error envelope via `ResultWriter`, returns the §6 exit code. Configures `VisaLogger` (prefix `[visa]`, level from `VISA_LOG_LEVEL`) at startup.

**`Commands/<Subcommand>Command.cs`** — One handler per subcommand in [TechDesignCliContract.md](TechDesignCliContract.md) §3. Each validates its own args (raising `UsageException`), opens/finds the appropriate VISA resource via `Visa.Core`, performs the operation, builds the success envelope. New handlers this session: `GetAttrCommand`, `SetAttrCommand`, `UsbControlInCommand`, `UsbControlOutCommand`, `ReadBlockCommand`, `WriteBlockCommand` (G-S4, G-S5, G-S6). `NotImplementedHandler.cs` is the default registry entry used during incremental development.

**`Cli/` — argv → typed config (CLI-only, unit-tested):**
- `ArgParser` — argv → positional list + flag dictionary, with caller-expressible mutual-exclusion rules. (Carried over.)
- `BoardId` — normalize `GPIB0` → `GPIB0::INTFC`; reject non-board inputs. (Carried over.)
- `HexBytes` — parse/normalize/format hex strings. (Carried over.)
- `TermChar` — `Parse` accepts `NL` / `CR` (uppercase) / decimal `0..255` → single byte (carried over); `ParseEnable` accepts `0` / `1` → bool for `--term-char-en`.
- `ScriptClassification` — classify a trimmed command as `write` or `query` by trailing `?`. (Carried over.)
- `SerialFlags` (**new, G-S3**) — parse the shared serial flag group (`--baud`, `--data-bits`, `--parity`, `--stop-bits`, `--flow-control`) into a `Visa.Core.Protocol.SerialConfig`, raising `UsageException` on bad values. Owns the CLI flag names; the value semantics live in `SerialConfig`.
- `UsbControlFlags` (**new, G-S5**) — parse the shared USB control flag group (`--request-type`, `--request`, `--value`, `--index`, `--length`) into typed values (`short` for request-type/request, `ushort` for value/index/length), raising `UsageException` on out-of-range or malformed input. Used by both `usb-control-in` and `usb-control-out`. Pure parsing, unit-tested (G-S7).
- `UsageException` — thrown on malformed argv; caught in `Program.cs` → `error: "usage"` envelope, exit 2. (Carried over.)

**`Json/ResultWriter.cs`** — `WriteSuccess` / `WriteError`; `System.Text.Json`, `WriteIndented=false`, one object per invocation, `\n`-terminated. (Carried over.)

### 5.2 Data flow (happy path — `read-block`)

Walking through `read-block USB0::...::INSTR "CURV?" --out-file wave.bin`:

1. Claude invokes `dotnet run --project skills/visa/scripts/Visa -- read-block USB0::...::INSTR "CURV?" --out-file wave.bin`.
2. `Program.Main` parses argv, resolves `read-block`, instantiates `ReadBlockCommand`.
3. `ReadBlockCommand.Run` validates address/command/`--out-file`, parses optional `--timeout`, serial flags, `--raw`.
4. It opens a `VisaSession`, applies serial options if any, writes the command (`viWrite "CURV?\n"`), then `ReadRaw` accumulates the binary response with termchar matching off.
5. Unless `--raw`, `Protocol/BlockHeader` parses the leading `#NDDD` header → payload offset + declared length; the payload bytes are written to `wave.bin`.
6. Success envelope `{ ok, address, command, out_file, format, declared_length, bytes_written }` via `ResultWriter.WriteSuccess`; exit 0.

### 5.3 Threading / concurrency model

Single-threaded, identical to the predecessor. Each invocation runs `Program.Main` synchronously, opens at most one VISA session, exits. No async, no background work. The static default-RM field is per-process; since each invocation is a fresh process, lifetime is bounded by the invocation. `using` on `VisaSession` guarantees `viClose` on every path.

## 6. CLI contract

The full subcommand surface, flags, JSON schemas, exit-code table, attribute/constant reference, and stderr format live in **[TechDesignCliContract.md](TechDesignCliContract.md)**.

Exit-code summary (full table in the sub-TechDesign §5):

| Exit | Meaning |
|---|---|
| 0 | Success. Stdout JSON `"ok": true`. |
| 1 | VISA error (negative status). Stdout JSON `"ok": false`. |
| 2 | Usage error (unknown subcommand, missing arg, bad flag value). |
| 3 | VISA library not loadable (`DllNotFoundException` / `BadImageFormatException` on `visa32`/`visa64`). |
| 4 | Internal error (unexpected exception). |

## 7. Persistence

None at the application layer. Every invocation is stateless: no session handles, caches, or databases. The **only** disk I/O is the binary-transfer file payloads — `read-block --out-file <path>` writes the payload bytes to the named file; `write-block --in-file <path>` reads payload bytes from the named file (G-S6). Files are read/written verbatim as bytes; relative paths resolve against the calling process's cwd. The CLI does not manage, clean up, or index these files.

## 8. Security

The skill takes resource strings, commands, file paths, and flag values on its own command line, constructed by the calling Claude session. No authentication, encryption, or secret handling. VISA addresses are passed to the driver verbatim; no shell expansion is performed by the CLI. Instrument payloads are not logged to disk by default. File-path flags (`--out-file` / `--in-file`) are used as given — the CLI does not sandbox paths; the calling session is responsible for not pointing them at sensitive locations. The skill is trusted at the same level as any local executable Claude can run.

## 9. Logging and observability

Stderr is the sole log sink. `Visa.Core.Session/VisaLogger` writes one line per VISA call from the wrapper layer, format in [TechDesignCliContract.md](TechDesignCliContract.md) §7, prefixed `[visa]` (the prefix is injected by the CLI at startup, not hard-coded in the library — G-S8). Stdout never contains log output — always exactly one JSON envelope. Verbosity via `VISA_LOG_LEVEL`: `quiet` (nothing), `info` (default — one line per VISA call), `debug` (full request/response payloads at byte level). No log file; redirect stderr to persist.

## 10. Directory layout (target)

```
visa/                                           (project root)
├── plugin.json                                 (Claude Code plugin manifest; name: "visa")
├── README.md                                   (install + usage, human-facing)
├── .gitignore                                  (.NET stack baseline)
├── skills/
│   └── visa/
│       ├── SKILL.md                            (Claude-facing CLI documentation — common commands)
│       ├── references/
│       │   └── advanced.md                     (on-demand: bus-control, attr access, USB control — TechDesignCliContract §9)
│       └── scripts/
│           ├── visa.sln                                (three-project solution: Visa.Core + Visa + Visa.Tests)
│           ├── Visa.Core/                              (reusable class library — G-S8)
│           │   ├── Visa.Core.csproj                    (Microsoft.NET.Sdk, net8.0, library, Nullable; no Exe)
│           │   ├── Session/
│           │   │   ├── IVisaSession.cs
│           │   │   ├── VisaSession.cs
│           │   │   ├── VisaBoard.cs
│           │   │   ├── VisaNative.cs
│           │   │   ├── VisaStatus.cs
│           │   │   ├── VisaException.cs
│           │   │   └── VisaLogger.cs                   (prefix+level injected; reads no env vars)
│           │   └── Protocol/
│           │       ├── BlockHeader.cs                  (new — G-S6; IEEE 488.2 framing)
│           │       ├── SerialConfig.cs                 (new — G-S3; ASRL value encodings)
│           │       └── AttrRegistry.cs                 (new — G-S4; attribute id/type table)
│           ├── Visa/                                   (CLI exe — references Visa.Core)
│           │   ├── Visa.csproj                         (Microsoft.NET.Sdk, net8.0, Exe, Nullable; refs Visa.Core)
│           │   ├── Program.cs                          (argv parse + dispatch; configures VisaLogger)
│           │   ├── Cli/
│           │   │   ├── ArgParser.cs
│           │   │   ├── BoardId.cs
│           │   │   ├── HexBytes.cs
│           │   │   ├── ScriptClassification.cs
│           │   │   ├── TermChar.cs
│           │   │   ├── SerialFlags.cs                  (new — G-S3; argv → Visa.Core.Protocol.SerialConfig)
│           │   │   ├── UsbControlFlags.cs              (new — G-S5; argv → typed USB control params)
│           │   │   └── UsageException.cs
│           │   ├── Commands/
│           │   │   ├── ICommandHandler.cs
│           │   │   ├── NotImplementedHandler.cs
│           │   │   ├── ListCommand.cs
│           │   │   ├── InfoCommand.cs
│           │   │   ├── QueryCommand.cs
│           │   │   ├── WriteCommand.cs
│           │   │   ├── ReadCommand.cs
│           │   │   ├── ScriptCommand.cs
│           │   │   ├── ClearCommand.cs
│           │   │   ├── TriggerCommand.cs
│           │   │   ├── SerialPollCommand.cs
│           │   │   ├── IfcCommand.cs
│           │   │   ├── RenCommand.cs
│           │   │   ├── AtnCommand.cs
│           │   │   ├── GpibCommandCommand.cs
│           │   │   ├── GetAttrCommand.cs               (new — G-S4)
│           │   │   ├── SetAttrCommand.cs               (new — G-S4)
│           │   │   ├── UsbControlInCommand.cs          (new — G-S5)
│           │   │   ├── UsbControlOutCommand.cs         (new — G-S5)
│           │   │   ├── ReadBlockCommand.cs             (new — G-S6)
│           │   │   └── WriteBlockCommand.cs            (new — G-S6)
│           │   └── Json/
│           │       └── ResultWriter.cs
│           └── Visa.Tests/
│               ├── Visa.Tests.csproj                   (xUnit; references Visa + Visa.Core)
│               ├── SmokeTest.cs
│               ├── ProgramTests.cs
│               ├── Cli/
│               │   ├── ArgParserTests.cs
│               │   ├── BoardIdTests.cs
│               │   ├── HexBytesTests.cs
│               │   ├── ScriptClassificationTests.cs
│               │   ├── TermCharTests.cs
│               │   ├── SerialFlagsTests.cs             (new — G-S7)
│               │   └── UsbControlFlagsTests.cs         (new — G-S7)
│               ├── Protocol/
│               │   ├── BlockHeaderTests.cs             (new — G-S7)
│               │   ├── SerialConfigTests.cs            (new — G-S7)
│               │   └── AttrRegistryTests.cs            (new — G-S7)
│               ├── Json/
│               │   └── ResultWriterTests.cs
│               └── Session/
│                   ├── VisaLoggerTests.cs
│                   └── TerminatesOnEndTests.cs           (read-termination interface classifier)
└── docs/
    ├── TechDesign.md                                  (MAIN TechDesign — this document; current state of the app)
    ├── TechDesignCliContract.md                       (main sub-TechDesign — authoritative wire contract)
    └── TechDesignBinaryTransfer.md                    (main sub-TechDesign — binary block framing)
```

Everything under the project root was produced by the initial build session. The `docs/TechDesign.md`, `docs/TechDesignCliContract.md`, and `docs/TechDesignBinaryTransfer.md` main docs were produced by the phase 5 consolidation pass and describe the current state of the application; the per-session artifacts of that session (`docs/build-session/initial/` in the project's own history) are the frozen design record and are not shipped with this plugin.

## 11. Sub-TechDesigns

| Doc | Purpose |
|---|---|
| [TechDesignCliContract.md](TechDesignCliContract.md) | Authoritative wire contract: every subcommand, every flag, every JSON envelope, every exit code, the VISA attribute & constant references, and the stderr log format. |
| [TechDesignBinaryTransfer.md](TechDesignBinaryTransfer.md) | IEEE 488.2 arbitrary-block framing (definite `#NDDD` and indefinite `#0`), the binary raw-read loop and termchar handling, the `--raw` semantics, file payload I/O, and `read-block`/`write-block` algorithms. |

## 12. Future work

- **Async events / SRQ handler installation.** A `wait-srq` subcommand on `viInstallHandler` / `viWaitOnEvent`.
- **Locking.** `lock` / `unlock` subcommands for multi-process resource sharing.
- **Cross-platform.** Linux support via NI-VISA-for-Linux / linux-gpib (`libvisa.so`); requires reworking the `DllImportResolver`.
- **Software simulator.** A `SIM::<TYPE>::INSTR` resource scheme bypassing the real driver, enabling CI without hardware.
- **Streaming binary transfer.** Chunked/streamed block reads for captures larger than memory.

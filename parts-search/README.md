# parts-search

A plugin for researching electronic parts against the official **DigiKey**, **Mouser**, and **Newark** (element14) APIs, installed from the `electronics-skills` plugin marketplace. Claude reads the skill, constructs `dotnet run` invocations, and parses the normalized results — stock, unit price at your quantity, price breaks, lifecycle status, and product URLs where the vendor provides them, in one consistent shape, priced in CAD (Canadian market; Newark results carry no product URL — the element14 API returns none).

One CLI covers the whole lookup loop: `search` (by MPN, distributor part code, or keyword), `alternatives` (variants and substitute candidates with explainable reasons), `details` (full parameter table and datasheets), `check` (batch re-validation of a shortlist of MPNs), and `key` (one-time setup and live verification of your API keys). Search-only — no BOM import, no ordering.

## Requirements

| Requirement       | Notes                                                                                                                                                                                         |
| ----------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Windows 10/11 x64 | Keys are stored in the Windows Credential Manager via P/Invoke. Linux/macOS are not supported.                                                                                                |
| .NET 10 SDK       | Install from [dot.net](https://dot.net). Verify with `dotnet --version`.                                                                                                                      |
| Vendor API keys   | Free-tier keys from DigiKey, Mouser, and element14. One-time setup per vendor — see below. The CLI runs with zero keys (every vendor reports `no_key`), so you can set keys up one at a time. |

## Key setup (one-time, about a minute per vendor)

Keys are stored as generic credentials in the **Windows Credential Manager** under the `parts-search/` prefix (DPAPI-protected per user) — you never create environment variables by hand, and no key is ever written to a file or a repo.

**DigiKey** — needs an OAuth2 *client id* and *client secret*.

Where to get them: the [DigiKey Developer Portal](https://developer.digikey.com) — sign in with your (free) DigiKey account, register a new app under **My Apps** with the **Product Information API v4** enabled, and copy the client id and client secret it generates.

```powershell
dotnet run --project skills/parts-search/scripts/PartsSearch -- key set digikey
```

**Mouser** — one **Search** API key.

Where to get it: the [Mouser API hub](https://www.mouser.com/api-hub/) — sign in with your (free) Mouser account and request the **Search API** key; it is shown under your account's **APIs** section (and emailed to you).

```powershell
dotnet run --project skills/parts-search/scripts/PartsSearch -- key set mouser
```

> ⚠ **Mouser issues separate Search and Cart/Order keys.** This tool needs the **Search** key. A Cart/Order key authenticates without error but **silently returns zero results** — if `key test` reports `empty (suspect key type)` for mouser, or searches return `status: "no_results"`, that is almost always the wrong key type.

**Newark** — one element14 catalogue API key (Newark is element14's US arm; queries run against the Canadian store).

Where to get it: the [element14 Partner Portal](https://partner.element14.com) — register with your Newark or element14 account, then copy your Product Search API key from the portal's **My API Keys** section.

```powershell
dotnet run --project skills/parts-search/scripts/PartsSearch -- key set newark
```

Prompts read console input with echo disabled (nothing is displayed; pasted values still register in any terminal); run these in your own terminal (a non-interactive shell refuses them). Empty input re-prompts.

**Verify** what is stored and whether the keys actually work:

```powershell
dotnet run --project skills/parts-search/scripts/PartsSearch -- key list
# per-vendor/per-secret configured state + resolved source (env / credential manager / none) — never values

dotnet run --project skills/parts-search/scripts/PartsSearch -- key test
# one live limit-1 search per configured vendor: pass | empty (suspect key type) | rejected | error | no_key
```

Remove a vendor's stored secrets again with `key clear <digikey|mouser|newark>`.

## Environment variables (override / CI path)

Stored secrets can be overridden per secret by environment variables — the CI path. **When both are set, the environment variable wins** for that secret, so you can mix (e.g. a stored DigiKey credential with an env-var Mouser key). Empty values count as absent.

| Vendor  | Variables                                    |
| ------- | -------------------------------------------- |
| DigiKey | `DIGIKEY_CLIENT_ID`, `DIGIKEY_CLIENT_SECRET` |
| Mouser  | `MOUSER_API_KEY`                             |
| Newark  | `ELEMENT14_API_KEY`                          |

`key list` shows which source each secret resolved from, and `key set` prints a warning when an existing environment variable will shadow the secret you just stored. The `key` commands write secrets only to Credential Manager entries — never environment variables, never secret files (`key test` also performs its documented response-cache write-through).

## Usage

Every command follows this pattern (the examples run from this repo's root, hence the relative project path — from any other directory substitute the absolute path to `skills/parts-search/scripts/PartsSearch`, which is what the skill does via `${CLAUDE_PLUGIN_ROOT}`):

```
dotnet run --project skills/parts-search/scripts/PartsSearch -- <command> [flags]
```

The `--` separator is required. Default output is markdown; add `--format json` for the machine contract (exactly one JSON object on stdout).

```powershell
# Exact-MPN search at a build quantity (default: all configured vendors)
dotnet run --project skills/parts-search/scripts/PartsSearch -- search --q "TPIC6B595DWR" --qty 10
# Parts matching "TPIC6B595DWR" at qty 10 — as of 2026-10-23 19:40 UTC
#
# ### digikey — 1 result (cached)
# | MPN | Mfr | Stock | Unit @10 (CAD) | Lifecycle | Match | Description |
# |---|---|---:|---:|---|---|---|
# | [TPIC6B595DWR](https://www.digikey.ca/…) | Texas Instruments | 12,500 | 1.38 | active | exact | Power Driver … |

# A Digi-Key part number or Newark order code resolves through the same command
dotnet run --project skills/parts-search/scripts/PartsSearch -- search --q "HM1705-ND"
dotnet run --project skills/parts-search/scripts/PartsSearch -- search --q "83F8917"

# Variants and substitute candidates, each with a reason
dotnet run --project skills/parts-search/scripts/PartsSearch -- alternatives --q "TPIC6B595DWR"

# One part's full parameter table and datasheets
dotnet run --project skills/parts-search/scripts/PartsSearch -- details --vendor digikey --q "TPIC6B595DWR"

# Batch stock re-validation at order quantity
dotnet run --project skills/parts-search/scripts/PartsSearch -- check --mpn "TPIC6B595DWR" --mpn "HM1705-ND" --qty 10

# Force a fresh fetch (skips the cache read and write for this invocation)
dotnet run --project skills/parts-search/scripts/PartsSearch -- search --q "TPIC6B595DWR" --no-cache
```

Two query behaviors are built in:

- **Digi-Key keyword narrowing** — Digi-Key AND-matches every keyword, so long natural-language queries return zero products. When a multi-word query comes back empty, the search retries with the last token dropped until something matches; the outcome carries `narrowed_to` (JSON) or a `- keywords narrowed to "…"` bullet (md/table) naming the query that produced the parts. Single-token queries (MPNs) never narrow.
- **Mouser part-number routing** — Mouser's keyword endpoint fuzzy-matches, so an unknown part number comes back as unrelated hits. Queries that look like a part number (single token of at least three characters containing a digit) go to Mouser's by-number endpoint, where an unknown part is simply `no_results`.

## Caching

Vendor responses are cached under `%LOCALAPPDATA%\parts-search\cache\` for **24 hours** (keyed by vendor, request kind, query, limit, and market; quantity is not part of the key). The DigiKey OAuth token is cached alongside until near expiry. Entries older than 7 days are pruned automatically on each run.

Emptying the folder by hand is always safe — worst case is a cold cache and a fresh token on the next run:

```powershell
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\parts-search\cache\*"
```

Outside that cache folder the tool writes nowhere else except its `parts-search/*` Credential Manager entries — and nothing is ever written inside any repository.

## Logging / debug output

Stderr carries one human-readable trace line per vendor interaction (`[parts-search] digikey search cache=MISS http=200 ms=412 results=3`); stdout stays machine-clean. Control with the `PARTS_SEARCH_LOG_LEVEL` environment variable:

```powershell
$env:PARTS_SEARCH_LOG_LEVEL = "quiet"   # no stderr output
$env:PARTS_SEARCH_LOG_LEVEL = "info"    # one line per interaction (default)
$env:PARTS_SEARCH_LOG_LEVEL = "debug"   # + request URLs (key params masked), byte counts, cache keys
```

Secrets are never logged: the program itself never writes a secret (the terminal echo of a pasted value is your own keystrokes), `key list` shows presence and source only, and in `debug` every URL query parameter whose name contains `key` is masked to `***`.

## Exit codes

| Code | Meaning        | What to do                                                                                                                                                               |
| ---- | -------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| 0    | Success        | Stdout JSON has `"ok": true`, whatever the per-vendor statuses. Zero configured keys is also 0 (every vendor `no_key`).                                                  |
| 1    | Unused in v1   | —                                                                                                                                                                        |
| 2    | Usage error    | Bad flag, unknown flag, missing `--q`/`--mpn`, invalid `--vendor` for `details`, unknown `key` subcommand or vendor token. Check `description` in the JSON error object. |
| 4    | Internal error | A bug in the CLI. The JSON error object carries the exception message.                                                                                                   |

Per-vendor problems never change the exit code: a vendor without a key reports `status: "no_key"`, a failed vendor call reports `status: "error"` with a message (401/403 messages name the environment variable to check or point at `key set <vendor>`), and the invocation still exits 0.

## Uninstall

Remove the plugin through your client's plugin management UI (in Claude Code, `/plugin uninstall parts-search@electronics-skills`). Nothing is installed system-wide; the only residue is the `%LOCALAPPDATA%\parts-search\cache\` folder (safe to delete) and any `parts-search/*` Credential Manager entries (remove with `key clear <vendor>` per vendor).

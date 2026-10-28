---
name: parts-search
description: Search electronic parts across DigiKey, Mouser, and Newark (element14) by MPN, distributor part code, or keyword — normalized stock, unit price at quantity, price breaks, lifecycle status, and product URLs where the vendor provides them, with an as-of timestamp; find variants and substitute candidates with explainable reasons; pull one part's full parameter table and datasheets; re-validate a shortlist of MPNs in one batch check; and set up, list, test, or clear vendor API keys in the Windows Credential Manager.
---

# parts-search-skill

## When to use this skill

Use this skill whenever the user needs to:
- Look up availability, pricing, and lifecycle for electronic parts by MPN (`TPIC6B595DWR`), distributor part code (Digi-Key `HM1705-ND`, Newark order code `83F8917`), or free keywords when no part number is known ("reverse mount green 1206 LED")
- Price a part at a build quantity, including noticing when a larger bracket quantity is cheaper overall (`cheaper_at` hints)
- Find variants of a known part and substitute candidates from the same category, each with an explainable `reason`
- Get one part's full detail: parameter table and datasheet links
- Re-validate a shortlist of exact MPNs across all vendors in one invocation (pre-order stock check)
- Set up, list, test, or clear the vendor API keys (one-time setup, Windows Credential Manager)

## Prerequisites

1. **.NET 10 SDK on PATH** — `dotnet --version` should report 10.x or later.
2. **Windows 10/11 x64** — this skill is Windows-only.
3. **One-time `key set` per vendor** — vendors without a configured key report `status: "no_key"` and are simply skipped; the CLI still works with zero keys. Keys live in the Windows Credential Manager, entered once via `parts-search key set <vendor>` (prompts are hidden — echo-disabled input; pasted values register; about a minute per vendor). Direct the user to run this in their own terminal — it needs an interactive console.

## Invocation form

```
dotnet run --project "${CLAUDE_PLUGIN_ROOT}/skills/parts-search/scripts/PartsSearch" -- <command> [flags]
```

The `--` separator is required. `${CLAUDE_PLUGIN_ROOT}` is substituted with this plugin's install directory, so the project path resolves to an absolute location and the command works from any working directory — including when this skill is invoked from another project or plugin.

## Command reference

| Command | Purpose | Key flags |
|---|---|---|
| `search` | Parts by MPN, distributor code, or keywords; the core lookup | `--q` (required), `--vendor`, `--qty`, `--limit`, `--format`, `--no-cache` |
| `alternatives` | Pivot part plus same-base-MPN variants and same-category substitute candidates | same flags as `search` (`--limit` caps the alternatives list — the exact match and variants are always included) |
| `details` | One resolved part with full parameter table and datasheet links | `--vendor` (exactly one, no `all`), `--q` (required), `--qty`, `--format`, `--no-cache`; no `--limit` |
| `check` | One exact-match search per MPN per vendor — batch stock re-validation | `--mpn` (repeatable, ≥ 1), `--qty`, `--vendor`, `--format`, `--no-cache`; no `--limit` |
| `key set <vendor>` | Store a vendor's secrets (hidden prompts) | vendor token: `digikey` \| `mouser` \| `newark` |
| `key list` | Per-vendor/per-secret configured state + source — never values | — |
| `key test` | Live probe per configured vendor (one limit-1 search on the fixed query `resistor`) | — |
| `key clear <vendor>` | Delete this tool's Credential Manager entries for a vendor | vendor token: `digikey` \| `mouser` \| `newark` |

**Flag rules (all commands):**
- Long-form flags (`--name value`) only. No short flags; `--flag=value` syntax is not supported.
- `--vendor` takes `all` (default) or a comma-list such as `digikey,mouser`; tokens are `digikey`, `mouser`, `newark`. `details` requires exactly one vendor — `all` and comma-lists are usage errors there.
- `--qty` is the quantity for unit-price computation, integer 1–100000, default `1`.
- `--limit` caps results kept per vendor, integer 1–50, default `10`. Not accepted by `details` (one product) or `check` (exact matches only).
- `--format` is `md` (default), `table`, or `json`. `json` is the machine contract: exactly one JSON object on stdout.
- `--no-cache` bypasses both the cache read and the cache write for this invocation.

## Common examples

```powershell
# Exact-MPN search at a build quantity
dotnet run --project "${CLAUDE_PLUGIN_ROOT}/skills/parts-search/scripts/PartsSearch" -- search --q "TPIC6B595DWR" --qty 10

# Re-validate a Digi-Key part number before ordering
dotnet run --project "${CLAUDE_PLUGIN_ROOT}/skills/parts-search/scripts/PartsSearch" -- search --q "HM1705-ND"

# Keyword hunt when no MPN is known
dotnet run --project "${CLAUDE_PLUGIN_ROOT}/skills/parts-search/scripts/PartsSearch" -- search --q "reverse mount green 1206 LED"

# Only two vendors, machine-readable output
dotnet run --project "${CLAUDE_PLUGIN_ROOT}/skills/parts-search/scripts/PartsSearch" -- search --q "TPIC6B595DWR" --vendor digikey,mouser --format json

# Variants and substitute candidates for a known part
dotnet run --project "${CLAUDE_PLUGIN_ROOT}/skills/parts-search/scripts/PartsSearch" -- alternatives --q "TPIC6B595DWR"

# Full parameters and datasheets from one vendor
dotnet run --project "${CLAUDE_PLUGIN_ROOT}/skills/parts-search/scripts/PartsSearch" -- details --vendor digikey --q "TPIC6B595DWR"

# Batch re-validation of a shortlist at order quantity
dotnet run --project "${CLAUDE_PLUGIN_ROOT}/skills/parts-search/scripts/PartsSearch" -- check --mpn "TPIC6B595DWR" --mpn "HM1705-ND" --qty 10

# One-time key setup (interactive prompts — the user runs this)
dotnet run --project "${CLAUDE_PLUGIN_ROOT}/skills/parts-search/scripts/PartsSearch" -- key set digikey

# Verify what is configured and whether the keys work
dotnet run --project "${CLAUDE_PLUGIN_ROOT}/skills/parts-search/scripts/PartsSearch" -- key list
dotnet run --project "${CLAUDE_PLUGIN_ROOT}/skills/parts-search/scripts/PartsSearch" -- key test
```

## Output reading

Default output is markdown: one summary line carrying the global `as_of` timestamp, one section per vendor with results, one status line per vendor without them. With `--format json` the same data arrives as exactly one JSON object on stdout:

```json
{
  "ok": true, "command": "search", "query": "TPIC6B595DWR", "qty": 10,
  "as_of": "2026-10-23T19:40:12Z",
  "vendors": [
    { "vendor": "digikey", "status": "ok", "cached": true,
      "parts": [ { "mpn": "TPIC6B595DWR", "vendor_part_number": "296-1275-1-ND",
                   "description": "…", "manufacturer": "Texas Instruments",
                   "stock": 12500, "unit_price": 1.38, "currency": "CAD",
                   "price_breaks": [ { "qty": 1, "price": 1.62 }, { "qty": 10, "price": 1.38 } ],
                   "cheaper_at": null, "lifecycle": "active", "packaging": "Cut Tape (CT)",
                   "category": "Integrated Circuits (ICs)", "url": "…", "match": "exact" } ] },
    { "vendor": "mouser", "status": "no_key", "parts": [] },
    { "vendor": "newark", "status": "error", "error": "http 503 (1 retry)", "parts": [] }
  ]
}
```

- **Vendor `status`** is `ok`, `no_results`, `no_key` (no key configured — run `key set <vendor>`), or `error` (that vendor only; never fails the invocation). Zero configured keys is not an error: exit 0, every vendor `no_key`.
- **`narrowed_to`** appears when Digi-Key keyword narrowing fired: the parts came from the shortened query named in the field, because the full query legitimately matched nothing (see below). `match` is still classified against the full query. In md/table output it surfaces as a `- keywords narrowed to "…" — no results for the full query` bullet.
- **`match`** is the query confidence: `exact` (query equals the MPN or distributor code, case-insensitive) or `keyword`.
- **`unit_price` / `currency`** are the price at the requested `--qty` (from the price breaks) and its ISO currency — CAD everywhere. `null` when the vendor returned no pricing.
- **`cheaper_at`** appears when ordering a larger bracket costs strictly less in total: `{ "qty": 10, "unit_price": 2.60, "total": 26.00, "savings": 4.80 }`. In md/table output it surfaces as a `- tip: buy 10 × <MPN> instead of 8 …` bullet — recommend the cheaper quantity.
- **`lifecycle`** is `active | nrnd | eol | obsolete | unknown` — flag `nrnd`/`eol`/`obsolete` parts to the user before they design one in.
- **`as_of`** is the data timestamp; cite it when reporting prices (see the caching caveat below).
- **`alternatives`** adds `relation` (`exact | variant | alternative`) and a formulaic `reason` to every part: `"mpn equals query"`, `"same base MPN as TPIC6B595DWR"`, `"same category (Integrated Circuits (ICs)), same manufacturer"`.
- **`details`** adds `parameters` (the vendor's own name/value list, passed through as-is) and `datasheets` (URL list) to the single returned part.
- **`check`** returns one `lines[]` entry per `--mpn`, each with the same vendor-outcome shape and exact matches only — a vendor line with `status: "ok"` and empty `parts` means *searched, no exact match*.

## Query routing and narrowing

Two vendor behaviors are built in so a reasonable query gets useful answers the first time:

- **Digi-Key keyword narrowing**: Digi-Key's keyword API AND-matches every keyword, so long natural-language queries (`"dc power jack 2.1mm panel mount solder lug"`) return zero products. When a multi-word query comes back empty, each retry drops the last token until something matches (`narrowed_to` names the query that produced the parts). Every intermediate fetch is paced and cached like any other; a single-token query (an MPN) never narrows — empty means empty.
- **Mouser part-number routing**: Mouser's keyword endpoint fuzzy-matches, so a part number it does not carry comes back as a page of unrelated hits (a PIC32 for `T821M140A1S100CEU`). Part-number-shaped queries — a single token of at least three characters containing a digit — go to Mouser's by-number endpoint instead, where an unknown part simply returns `no_results`. Pass real words when you want Mouser's similar-parts browse.

## Caching and freshness

Vendor responses are cached for ~24 h (keyed by vendor, request kind, query, limit, and market; quantity is not part of the key, so one cached response serves any `--qty`). A fresh-looking answer can therefore be hours old: **always quote `as_of`** when reporting stock or prices, and re-run with `--no-cache` when the user needs current data — it forces the fetch and skips both the cache read and write. `key test` never reads the cache, so it always exercises the current key.

## Finding alternatives

`alternatives` output is explainable, not verified: `variant` means the same base MPN from the same manufacturer, and `alternative` means the same category with a different base MPN — or, when either side carries no category, a sufficiently similar description — with the `reason` field naming the rule that fired. The pivot row itself appears as a `variant` of its own base MPN when it was resolved by keyword (no exact match). Candidates are **not** verified drop-in replacements; compare them on the returned data (package, specifications, lifecycle), and pull a promising candidate's full parameter table with `details --vendor <v> --q <code>` before recommending it.

## Exit codes and errors

| Code | Meaning |
|---|---|
| 0 | Success — a well-formed envelope was produced, whatever the per-vendor statuses (even every vendor erroring) |
| 1 | Unused in v1 |
| 2 | Usage error — unknown flag, missing `--q`/`--mpn`, bad flag value, invalid `--vendor` for `details`, unknown `key` subcommand or vendor token |
| 4 | Internal error — unexpected exception |

Usage and internal errors print the same JSON error object to **stdout** regardless of `--format`:

```json
{ "ok": false, "command": "search", "error": "usage", "description": "…" }
```

(`error` is `usage` for exit 2, `internal` for exit 4.) Parse `ok` first, then branch on it.

## Stderr trace

Human-readable `[parts-search] …` trace lines (cache hits, HTTP status, timings) go to stderr; stdout stays machine-clean. Control with `PARTS_SEARCH_LOG_LEVEL` (`quiet | info | debug`, default `info`). Secret values are never logged; in `debug`, URL query parameters whose name contains `key` are masked.

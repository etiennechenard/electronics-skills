# electronics-skills

Agent skills for electronics — design, simulate, source parts, and drive lab instruments — shipped as one plugin marketplace serving both Claude Code and ZCode. Its declared name — the one both harnesses install under, from [`.claude-plugin/marketplace.json`](.claude-plugin/marketplace.json) — is **`electronics-skills`**, whatever this repository's folder happens to be called. ZCode reads this same manifest (its loader probes `.claude-plugin/marketplace.json` first by design), so one listing serves both.

## The plugins

| Plugin | What it does |
|---|---|
| [`micro-cap`](micro-cap/README.md) | Drive Micro-Cap 12 headlessly on Windows: write SPICE netlists, run AC/transient/DC/distortion analyses and digital/mixed-signal logic in batch mode, and parse the numeric output files. |
| [`parts-search`](parts-search/README.md) | Research parts against the official DigiKey, Mouser, and Newark (element14) APIs — normalized stock, unit price at quantity, price breaks, lifecycle status, substitute candidates, datasheets — plus one-time vendor API-key setup in the Windows Credential Manager. |
| [`pcb-as-code`](pcb-as-code/README.md) | Build hardware design-as-code: KiCad 10+ schematics and PCBs generated from a C# single source of truth, verified pin-by-pin, autorouted headlessly with FreeRouting, gated by ERC/DRC, exported to fab gerbers/BOM/renders — with an `hw-review` skill that sweeps read-only reviewer agents over changed docs, design source, schematics and boards. |
| [`visa`](visa/README.md) | Control VISA-addressable lab instruments (DMMs, scopes, power supplies, signal generators) over GPIB, USB-TMC, Serial/ASRL, and TCPIP/LXI: SCPI query/write/read, multi-command scripts, GPIB bus control, attribute access, USB-TMC control transfers, binary block transfers. |

All four run on either harness — there is one edition of each plugin, not per-harness forks. Their runtime prerequisites (the .NET SDKs, the VISA driver, Micro-Cap 12 itself, vendor API keys) live in each plugin's own README.

## Add the marketplace to Claude Code

**Prerequisites:** the **Claude Code CLI** on PATH (install from [claude.ai/code](https://claude.ai/code) or via npm — `npm install -g @anthropic-ai/claude-code`), and **Git** on PATH (Claude Code clones this repository when adding the marketplace).

**Windows (PowerShell) or macOS/Linux (Terminal):**
```
claude plugin marketplace add https://github.com/etiennechenard/electronics-skills
```

Or via SSH on either platform:
```
claude plugin marketplace add git@github.com:etiennechenard/electronics-skills.git
```

Or from a local checkout, no GitHub involved:
```
claude plugin marketplace add C:\path\to\electronics-skills
```

After installation, the marketplace lands under its **declared name** (`electronics-skills`), not the repository folder name. Install whichever plugins you need:

```
claude plugin install visa@electronics-skills
claude plugin install micro-cap@electronics-skills
claude plugin install parts-search@electronics-skills
claude plugin install pcb-as-code@electronics-skills
```

Or from inside Claude Code: run `/plugin` and enable them under the `electronics-skills` entry.

## Update (Claude Code)

```
claude plugin marketplace update
```

## Add the marketplace to ZCode

**Prerequisites:** ZCode with plugin support, and **Git** on PATH (only needed for GitHub/Git-URL sources — a local-directory source needs none).

```
zcode plugins marketplace add https://github.com/etiennechenard/electronics-skills
zcode plugins install visa@electronics-skills
```

Or point the **`+`** button on **Settings → Plugin Management → Discover** at the repository URL, or at a local checkout of it. The listing shows all four plugins — install whichever you need. Installed plugins live under `%USERPROFILE%\.zcode\cli\plugins\cache\electronics-skills\<plugin>\<version>\`.

## Update (ZCode)

Refresh or update the marketplace and its installed plugins from **Settings → Plugin Management** (the **Discover** and **Installed** tabs).

## Next steps

Runtime prerequisites and full usage live in each plugin's own README: **[micro-cap](micro-cap/README.md)**, **[parts-search](parts-search/README.md)**, **[pcb-as-code](pcb-as-code/README.md)**, and **[visa](visa/README.md)**.

## Adding a plugin (maintainers)

1. Create a `<plugin>/` folder with its own `.claude-plugin/plugin.json` manifest (`name` — lowercase, matching `^[a-z0-9][a-z0-9._-]{0,127}$` — plus `version` and `description`; `skills/`, `commands/`, `agents/` subfolders as needed). Versions and authorship are read from this manifest, so the marketplace listing never needs touching on a version bump.
2. Add one entry to [`.claude-plugin/marketplace.json`](.claude-plugin/marketplace.json): `name`, `source` (`./<plugin>`), and a short `description` for the Discover listing.
3. Link the new README from the table above and from **Next steps**.

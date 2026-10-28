using System.Globalization;
using System.Text.Json;

namespace PartsSearch;

public static class Render
{
    public static string Envelope(Envelope envelope, OutputFormat format)
    {
        if (format == OutputFormat.Json)
            return JsonSerializer.Serialize(envelope, WireJson.Options);

        var blocks = new List<string>
        {
            $"Parts matching \"{envelope.Query}\" at qty {envelope.Qty} — as of {Timestamp(envelope.AsOf)}",
        };
        var pendingNonOk = new List<string>();
        foreach (var outcome in envelope.Vendors)
        {
            if (outcome.Status == VendorStatus.Ok)
            {
                FlushNonOk(pendingNonOk, blocks);
                blocks.Add(VendorBlock(envelope, outcome, format));
            }
            else
            {
                pendingNonOk.Add(NonOkLine(outcome));
            }
        }
        FlushNonOk(pendingNonOk, blocks);
        return Join(blocks);
    }

    public static string Check(CheckEnvelope envelope, OutputFormat format)
    {
        if (format == OutputFormat.Json)
            return JsonSerializer.Serialize(envelope, WireJson.Options);

        var matches = new List<(string Mpn, VendorId Vendor, PartResult Part)>();
        var notes = new List<string>();
        foreach (var line in envelope.Lines)
        {
            foreach (var outcome in line.Vendors)
            {
                var vendor = Name(outcome.Vendor);
                if (outcome.Status == VendorStatus.Ok && outcome.Parts.Count > 0)
                {
                    foreach (var part in outcome.Parts)
                        matches.Add((line.Mpn, outcome.Vendor, part));
                }
                else if (outcome.Status == VendorStatus.Ok)
                {
                    notes.Add($"- {line.Mpn} @ {vendor}: no exact match");
                }
                else if (outcome.Status == VendorStatus.NoKey)
                {
                    notes.Add($"- {line.Mpn} @ {vendor}: no key configured (run: parts-search key set {vendor})");
                }
                else
                {
                    notes.Add($"- {line.Mpn} @ {vendor}: error — {outcome.Error}");
                }
            }
        }

        var currency = matches.Select(m => m.Part.Currency).FirstOrDefault(c => c is not null);
        var unitHeader = currency is null ? $"Unit @{envelope.Qty}" : $"Unit @{envelope.Qty} ({currency})";
        var headers = new[] { "MPN", "Vendor", "Stock", unitHeader, "Lifecycle" };
        var rows = matches.Select(m => new[]
        {
            Cell(m.Mpn, m.Part.Url, format),
            Name(m.Vendor),
            m.Part.Stock?.ToString("N0", CultureInfo.InvariantCulture) ?? "",
            m.Part.UnitPrice?.ToString(CultureInfo.InvariantCulture) ?? "",
            m.Part.Lifecycle.ToString().ToLowerInvariant(),
        }).ToList();

        var tableLines = new List<string>(Table(headers, rows, [false, false, true, true, false], format));
        tableLines.AddRange(notes);

        return Join(
        [
            $"Checked {Pluralize(envelope.Lines.Count, "part", "parts")} at qty {envelope.Qty} — as of {Timestamp(envelope.AsOf)}",
            string.Join("\n", tableLines),
        ]);
    }

    public static string Error(ErrorObject error) => JsonSerializer.Serialize(error, WireJson.Options);

    private static string VendorBlock(Envelope envelope, VendorOutcome outcome, OutputFormat format)
    {
        var lines = new List<string>
        {
            $"### {Name(outcome.Vendor)} — {Pluralize(outcome.Parts.Count, "result", "results")}{(outcome.Cached == true ? " (cached)" : "")}",
        };

        if (outcome.NarrowedTo is { } narrowed)
            lines.Add($"- keywords narrowed to \"{narrowed}\" — no results for the full query");

        if (envelope.Command == CommandKind.Alternatives)
        {
            foreach (var (title, relation) in new[]
                     {
                         ("Exact match", RelationKind.Exact),
                         ("Variants", RelationKind.Variant),
                         ("Alternatives", RelationKind.Alternative),
                     })
            {
                var group = outcome.Parts.Where(p => p.Relation == relation).ToList();
                if (group.Count == 0) continue;
                lines.Add($"#### {title}");
                lines.AddRange(PartsTable(envelope, group, format));
            }
        }
        else
        {
            lines.AddRange(PartsTable(envelope, outcome.Parts, format));
            if (envelope.Command == CommandKind.Details)
            {
                foreach (var part in outcome.Parts)
                {
                    if (part.Parameters is { Count: > 0 } parameters)
                    {
                        var rows = parameters.Select(p => new[] { p.Name, p.Value }).ToList();
                        lines.AddRange(Table(["Parameter", "Value"], rows, [false, false], format));
                    }
                    if (part.Datasheets is { Count: > 0 } datasheets)
                    {
                        lines.AddRange(datasheets.Select(url => $"- datasheet: {url}"));
                    }
                }
            }
        }

        lines.AddRange(Tips(envelope.Qty, outcome.Parts));
        return string.Join("\n", lines);
    }

    private static IEnumerable<string> PartsTable(Envelope envelope, IReadOnlyList<PartResult> parts, OutputFormat format)
    {
        var currency = parts.Select(p => p.Currency).FirstOrDefault(c => c is not null);
        var unitHeader = currency is null ? $"Unit @{envelope.Qty}" : $"Unit @{envelope.Qty} ({currency})";
        var headers = new[] { "MPN", "Mfr", "Stock", unitHeader, "Lifecycle", "Match", "Description" };
        var rows = parts.Select(p => new[]
        {
            Cell(p.Mpn, p.Url, format),
            p.Manufacturer,
            p.Stock?.ToString("N0", CultureInfo.InvariantCulture) ?? "",
            p.UnitPrice?.ToString(CultureInfo.InvariantCulture) ?? "",
            p.Lifecycle.ToString().ToLowerInvariant(),
            p.Match.ToString().ToLowerInvariant(),
            Describe(p),
        }).ToList();
        return Table(headers, rows, [false, false, true, true, false, false, false], format);
    }

    private static IEnumerable<string> Table(
        IReadOnlyList<string> headers, IReadOnlyList<string[]> rows, IReadOnlyList<bool> rightAlign, OutputFormat format) =>
        format == OutputFormat.Md
            ? PipeTable(headers, rows, rightAlign)
            : AlignedTable(headers, rows, rightAlign);

    private static IEnumerable<string> PipeTable(
        IReadOnlyList<string> headers, IReadOnlyList<string[]> rows, IReadOnlyList<bool> rightAlign)
    {
        yield return "| " + string.Join(" | ", headers) + " |";
        yield return "|" + string.Join("|", headers.Select((_, i) => rightAlign[i] ? "---:" : "---")) + "|";
        foreach (var row in rows)
            yield return "| " + string.Join(" | ", row) + " |";
    }

    private static IEnumerable<string> AlignedTable(
        IReadOnlyList<string> headers, IReadOnlyList<string[]> rows, IReadOnlyList<bool> rightAlign)
    {
        var widths = new int[headers.Count];
        for (var i = 0; i < headers.Count; i++)
            widths[i] = Math.Max(headers[i].Length, rows.Count == 0 ? 0 : rows.Max(r => r[i].Length));

        yield return AlignedLine(headers);
        foreach (var row in rows)
            yield return AlignedLine(row);

        string AlignedLine(IReadOnlyList<string> cells) => string.Join("  ", Enumerable.Range(0, headers.Count)
            .Select(i => rightAlign[i] ? cells[i].PadLeft(widths[i]) : cells[i].PadRight(widths[i]))).TrimEnd();
    }

    private static IEnumerable<string> Tips(int qty, IReadOnlyList<PartResult> parts) =>
        parts.Where(p => p.CheaperAt is not null).Select(p =>
        {
            var hint = p.CheaperAt!;
            var currentTotal = (hint.Total + hint.Savings).ToString(CultureInfo.InvariantCulture);
            return $"- tip: buy {hint.Qty} × {p.Mpn} instead of {qty} — total {hint.Total.ToString(CultureInfo.InvariantCulture)} vs {currentTotal} {p.Currency} (save {hint.Savings.ToString(CultureInfo.InvariantCulture)})";
        });

    private static string NonOkLine(VendorOutcome outcome) => outcome.Status switch
    {
        VendorStatus.NoResults => $"- {Name(outcome.Vendor)}: no results",
        VendorStatus.NoKey => $"- {Name(outcome.Vendor)}: no key configured (run: parts-search key set {Name(outcome.Vendor)})",
        _ => $"- {Name(outcome.Vendor)}: error — {outcome.Error}",
    };

    private static void FlushNonOk(List<string> pending, List<string> blocks)
    {
        if (pending.Count == 0) return;
        blocks.Add(string.Join("\n", pending));
        pending.Clear();
    }

    private static string Cell(string mpn, string? url, OutputFormat format) =>
        format == OutputFormat.Md && !string.IsNullOrEmpty(url) ? $"[{mpn}]({url})" : mpn;

    private static string Describe(PartResult part) =>
        part.Reason is null ? Truncate(part.Description) : $"{Truncate(part.Description)} ({part.Reason})";

    private static string Truncate(string text) =>
        text.Length > 60 ? text[..60].TrimEnd() + " …" : text;

    private static string Name(VendorId vendor) => vendor.ToString().ToLowerInvariant();

    private static string Pluralize(int count, string singular, string plural) =>
        count == 1 ? $"{count} {singular}" : $"{count} {plural}";

    private static string Timestamp(DateTimeOffset asOf) =>
        $"{asOf.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC";

    private static string Join(IReadOnlyList<string> blocks) => string.Join("\n\n", blocks) + "\n";
}

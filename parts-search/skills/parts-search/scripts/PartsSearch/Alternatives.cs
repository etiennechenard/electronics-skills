namespace PartsSearch;

// The classifier behind the alternatives command: pivot selection, the relation rules,
// ranking, cross-stage dedup, and the deterministic stage-2 query build — pure over
// constructed results.
internal static class AlternativesFinder
{
    // First exact match in stage 1, else the top result; no stage-1 results means the
    // vendor stays without a pivot and gets no stage 2.
    public static PartResult? SelectPivot(IReadOnlyList<PartResult> stage1) =>
        stage1.FirstOrDefault(part => part.Match == MatchKind.Exact) ?? stage1.FirstOrDefault();

    // The pivot's manufacturer plus the first six whitespace-separated tokens of its
    // description, lowercased, single-space-joined, MPN excluded — deterministic so the
    // stage-2 cache key is stable.
    public static string BuildStage2Query(PartResult pivot)
    {
        var mpn = pivot.Mpn.ToLowerInvariant();
        var tokens = pivot.Description
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.ToLowerInvariant())
            .Where(token => token != mpn)
            .Take(6);
        return $"{pivot.Manufacturer} {string.Join(' ', tokens)}".TrimEnd().ToLowerInvariant();
    }

    // Classifies one vendor's stage-1 and stage-2 results against the pivot: relation and
    // reason per part, unclassifiable parts dropped, cross-stage dedup by vendor part
    // number with the stage-1 occurrence winning, ranked same-manufacturer first, then
    // stock descending (nulls last), then the vendor's relevance order.
    public static IReadOnlyList<PartResult> Classify(IReadOnlyList<PartResult> stage1, IReadOnlyList<PartResult> stage2)
    {
        var pivot = SelectPivot(stage1);
        if (pivot is null) return [];

        var seenVendorPartNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var classified = new List<PartResult>();
        foreach (var part in stage1.Concat(stage2))
        {
            if (!seenVendorPartNumbers.Add(part.VendorPartNumber)) continue;
            if (ClassifyAgainstPivot(pivot, part) is not (var relation, var reason)) continue;
            classified.Add(part with { Relation = relation, Reason = reason });
        }
        return Rank(pivot, classified);
    }

    private static (RelationKind Relation, string Reason)? ClassifyAgainstPivot(PartResult pivot, PartResult part)
    {
        if (part.Match == MatchKind.Exact)
            return (RelationKind.Exact, "mpn equals query");

        if (SameManufacturer(pivot, part) && SameBaseMpn(pivot.Mpn, part.Mpn))
            return (RelationKind.Variant, $"same base MPN as {pivot.Mpn}");

        if (DifferentBaseMpn(pivot, part))
        {
            if (SameCategory(pivot, part))
            {
                return (RelationKind.Alternative, SameManufacturer(pivot, part)
                    ? $"same category ({pivot.Category}), same manufacturer"
                    : $"same category ({pivot.Category})");
            }
            if ((pivot.Category is null || part.Category is null)
                && DescriptionOverlap(pivot.Description, part.Description) >= 0.4)
            {
                return (RelationKind.Alternative, $"description overlap with {pivot.Mpn}");
            }
        }

        return null;
    }

    private static IReadOnlyList<PartResult> Rank(PartResult pivot, List<PartResult> parts) =>
        parts
            .OrderBy(part => SameManufacturer(pivot, part) ? 0 : 1)
            .ThenByDescending(part => part.Stock ?? -1)
            .ToList();

    private static bool SameManufacturer(PartResult pivot, PartResult part) =>
        string.Equals(pivot.Manufacturer, part.Manufacturer, StringComparison.OrdinalIgnoreCase);

    private static bool SameCategory(PartResult pivot, PartResult part) =>
        pivot.Category is not null
        && part.Category is not null
        && string.Equals(pivot.Category, part.Category, StringComparison.OrdinalIgnoreCase);

    private static bool DifferentBaseMpn(PartResult pivot, PartResult part) =>
        !SameBaseMpn(pivot.Mpn, part.Mpn);

    // Same base MPN: case-insensitive prefix match after stripping at most three trailing
    // alphanumerics from either side, so packaging/suffix forms of one base match while
    // differently-numbered family members (TPIC6B… vs TPIC6C…) do not.
    internal static bool SameBaseMpn(string left, string right)
    {
        var a = left.ToUpperInvariant();
        var b = right.ToUpperInvariant();
        for (var stripLeft = 0; stripLeft <= 3; stripLeft++)
        {
            var aTrimmed = TrimTrailingAlphanumeric(a, stripLeft);
            if (aTrimmed.Length == 0) return false;
            for (var stripRight = 0; stripRight <= 3; stripRight++)
            {
                var bTrimmed = TrimTrailingAlphanumeric(b, stripRight);
                if (bTrimmed.Length == 0) return false;
                if (aTrimmed.StartsWith(bTrimmed, StringComparison.Ordinal)
                    || bTrimmed.StartsWith(aTrimmed, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static string TrimTrailingAlphanumeric(string text, int count)
    {
        var end = text.Length;
        for (var removed = 0; removed < count && end > 0 && char.IsAsciiLetterOrDigit(text[end - 1]); removed++)
            end--;
        return text[..end];
    }

    // Jaccard overlap of the two descriptions' lowercase whitespace tokens of length >= 3.
    internal static double DescriptionOverlap(string left, string right)
    {
        var a = OverlapTokens(left).ToHashSet();
        var b = OverlapTokens(right).ToHashSet();
        if (a.Count == 0 || b.Count == 0) return 0;
        return (double)a.Intersect(b).Count() / a.Union(b).Count();
    }

    private static IEnumerable<string> OverlapTokens(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.ToLowerInvariant())
            .Where(token => token.Length >= 3);
}

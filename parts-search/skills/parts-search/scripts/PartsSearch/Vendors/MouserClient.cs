using System.Text.Json;

namespace PartsSearch;

public sealed class MouserClient : IVendorClient
{
    private const string CurrencyApiParameter = "currencyCode=CAD";
    private const string SearchByKeywordUrl = "https://api.mouser.com/api/v2/search/keyword";
    private const string SearchByPartUrl = "https://api.mouser.com/api/v2/search/partnumber";
    private const int MaxRecords = 50;

    public VendorId VendorId => VendorId.Mouser;

    public bool IsConfigured(KeyConfig keyConfig) =>
        keyConfig.GetSecret(VendorId.Mouser, CredentialTargets.ApiKeyField) is not null;

    // Mouser's keyword endpoint fuzzy-matches, so a part number it does not carry comes
    // back as a page of unrelated hits (a PIC32 for "T821M140A1S100"); part-number-shaped
    // queries go to the by-number endpoint instead, where an unknown part is simply empty.
    internal static bool LooksLikePartNumber(string query)
    {
        var trimmed = query.Trim();
        return trimmed.Length >= 3
            && !trimmed.Contains(' ')
            && trimmed.Any(char.IsAsciiDigit);
    }

    public RawHttpRequest BuildSearchRequest(string query, int limit)
    {
        // The trimmed form goes on the wire for both endpoints: the routing predicate and
        // the cache key normalize whitespace, and the by-number endpoint exact-matches,
        // so a padded mouserPartNumber could never match the part it names.
        var trimmed = query.Trim();
        if (LooksLikePartNumber(trimmed))
        {
            return new RawHttpRequest(
                HttpMethod.Post.Method,
                $"{SearchByPartUrl}?{CurrencyApiParameter}",
                new Dictionary<string, string>(),
                JsonSerializer.Serialize(new
                {
                    SearchByPartRequest = new
                    {
                        mouserPartNumber = trimmed,
                        partSearchOptions = "",
                    },
                }),
                "application/json");
        }

        return new RawHttpRequest(
            HttpMethod.Post.Method,
            $"{SearchByKeywordUrl}?{CurrencyApiParameter}",
            new Dictionary<string, string>(),
            JsonSerializer.Serialize(new
            {
                SearchByKeywordRequest = new
                {
                    keyword = trimmed,
                    records = Math.Min(limit, MaxRecords),
                    startingRecord = 0,
                },
            }),
            "application/json");
    }

    public Task<RawHttpRequest> ApplySecretsAsync(RawHttpRequest request, KeyConfig keyConfig, CancellationToken cancellationToken)
    {
        var apiKey = keyConfig.GetSecret(VendorId.Mouser, CredentialTargets.ApiKeyField)
            ?? throw new InvalidOperationException("mouser api key is not configured");
        var separator = request.Url.Contains('?') ? '&' : '?';
        var stamped = request with { Url = $"{request.Url}{separator}apiKey={Uri.EscapeDataString(apiKey)}" };
        return Task.FromResult(stamped);
    }

    public IReadOnlyList<PartResult> ParseResponse(string rawJson, string query)
    {
        using var document = JsonDocument.Parse(rawJson);
        if (!document.RootElement.TryGetProperty("SearchResults", out var searchResults)
            || searchResults.ValueKind != JsonValueKind.Object
            || !searchResults.TryGetProperty("Parts", out var parts)
            || parts.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        return parts.EnumerateArray().Select(MapPart).ToList();
    }

    public RawHttpRequest BuildDetailsRequest(string vendorCode)
    {
        return new RawHttpRequest(
            HttpMethod.Post.Method,
            $"https://api.mouser.com/api/v2/search/partnumber?{CurrencyApiParameter}",
            new Dictionary<string, string>(),
            JsonSerializer.Serialize(new
            {
                SearchByPartRequest = new
                {
                    mouserPartNumber = vendorCode,
                    partSearchOptions = "",
                },
            }),
            "application/json");
    }

    public PartResult ParseDetailsResponse(string rawJson)
    {
        using var document = JsonDocument.Parse(rawJson);
        var parts = document.RootElement.GetProperty("SearchResults").GetProperty("Parts");
        // JsonElement is a struct: an empty array's FirstOrDefault is default(JsonElement),
        // distinguishable by ValueKind, not by a null check.
        var first = parts.EnumerateArray().FirstOrDefault();
        return first.ValueKind != JsonValueKind.Undefined
            ? MapPart(first) with
            {
                // The by-number response has no spec table; ProductAttributes (packaging,
                // standard pack qty, …) is the parameter-like data it does carry.
                Parameters = MapAttributes(first),
                Datasheets = Json.GetString(first, "DataSheetUrl") is { Length: > 0 } url ? [url] : [],
            }
            : throw new InvalidOperationException("mouser details response carries no part");
    }

    private static IReadOnlyList<PartParameter> MapAttributes(JsonElement part)
    {
        var parameters = new List<PartParameter>();
        if (Json.GetArray(part, "ProductAttributes") is { } attributes)
        {
            foreach (var attribute in attributes)
            {
                var name = Json.GetString(attribute, "AttributeName");
                var value = Json.GetString(attribute, "AttributeValue");
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value))
                    parameters.Add(new PartParameter(name, value));
            }
        }
        return parameters;
    }

    // The by-number endpoint never takes a records cap, and both endpoints parse the
    // same SearchResults.Parts shape, so ParseResponse is shared.

    // Mouser reports availability as prose with a leading quantity ("17042 In Stock",
    // "Call"); stock is the first integer sequence in the string, null when none.
    internal static int? ParseStock(string? availability)
    {
        if (availability is null) return null;
        var start = 0;
        while (start < availability.Length && !char.IsDigit(availability[start])) start++;
        if (start == availability.Length) return null;
        var end = start;
        while (end < availability.Length && char.IsDigit(availability[end])) end++;
        return int.Parse(availability[start..end], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static PartResult MapPart(JsonElement part)
    {
        var breaks = new List<PriceBreak>();
        string? currency = null;
        if (Json.GetArray(part, "PriceBreaks") is { } priceBreaks)
        {
            foreach (var brk in priceBreaks)
            {
                var quantity = PriceNormalizer.Parse(brk.GetProperty("Quantity"));
                var price = PriceNormalizer.Parse(brk.GetProperty("Price"));
                if (quantity is > 0 && price is not null)
                    breaks.Add(new PriceBreak((int)quantity, price.Value));
                currency ??= Json.GetString(brk, "Currency");
            }
        }

        return new PartResult
        {
            Mpn = Json.GetString(part, "ManufacturerPartNumber") ?? "",
            VendorPartNumber = Json.GetString(part, "MouserPartNumber") ?? "",
            Description = Json.GetString(part, "Description") ?? "",
            Manufacturer = Json.GetString(part, "Manufacturer") ?? "",
            Stock = ParseStock(Json.GetString(part, "Availability")),
            UnitPrice = SearchService.UnitPriceAt(breaks, 1),
            Currency = currency,
            PriceBreaks = breaks,
            Lifecycle = LifecycleMapper.Map(Json.GetString(part, "LifecycleStatus")),
            Packaging = FirstPackaging(part),
            Category = Json.GetString(part, "Category"),
            Url = Json.GetString(part, "ProductDetailUrl") ?? "",
            Match = MatchKind.Keyword,
        };
    }

    // ProductAttributes repeats "Packaging" once per purchasable form; the first entry is
    // the default ordering form's packaging.
    private static string? FirstPackaging(JsonElement part)
    {
        if (Json.GetArray(part, "ProductAttributes") is not { } attributes) return null;
        foreach (var attribute in attributes)
        {
            if (string.Equals(Json.GetString(attribute, "AttributeName"), "Packaging", StringComparison.OrdinalIgnoreCase))
                return Json.GetString(attribute, "AttributeValue");
        }
        return null;
    }
}

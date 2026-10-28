using System.Text.Json;

namespace PartsSearch;

// The shared lifecycle table: case-insensitive substring match against the vendor's
// status text, first hit wins; absent or unmatched text maps to Unknown.
internal static class LifecycleMapper
{
    internal static LifecycleStage Map(string? status)
    {
        if (status is null) return LifecycleStage.Unknown;
        var text = status.ToLowerInvariant();
        if (text.Contains("not recommended") || text.Contains("nrnd")) return LifecycleStage.Nrnd;
        if (text.Contains("end of life") || text.Contains("eol") || text.Contains("last time buy") || text.Contains("discontinued"))
            return LifecycleStage.Eol;
        if (text.Contains("obsolete")) return LifecycleStage.Obsolete;
        if (text.Contains("active")) return LifecycleStage.Active;
        return LifecycleStage.Unknown;
    }
}

// Vendor prices arrive as JSON numbers or as currency-prefixed/annotated strings depending
// on endpoint and version; both normalize to the decimal PartResult fields.
internal static class PriceNormalizer
{
    internal static decimal? Parse(JsonElement? element)
    {
        if (element is not { } value) return null;
        if (value.ValueKind == JsonValueKind.Number)
        {
            return value.GetDecimal();
        }
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (string.IsNullOrWhiteSpace(text)) return null;
            var start = 0;
            while (start < text.Length && !char.IsDigit(text[start]) && text[start] != '.') start++;
            var end = start;
            while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '.' || text[end] == ',')) end++;
            var numeric = text[start..end].Replace(",", "");
            return decimal.TryParse(numeric, System.Globalization.CultureInfo.InvariantCulture, out var price)
                ? price
                : null;
        }
        return null;
    }
}

public sealed class DigiKeyClient : IVendorClient
{
    // The decided market constants — Canadian market, CAD everywhere — defined once.
    private const string Currency = "CAD";

    private const string SearchBaseUrl = "https://api.digikey.com/products/v4/search/keyword";
    private const string DetailsBaseUrl = "https://api.digikey.com/products/v4/search";

    private readonly DigiKeyAuth _auth;

    public DigiKeyClient(DigiKeyAuth auth)
    {
        _auth = auth;
    }

    public VendorId VendorId => VendorId.Digikey;

    public bool IsConfigured(KeyConfig keyConfig) =>
        keyConfig.GetSecret(VendorId.Digikey, CredentialTargets.ClientIdField) is not null
        && keyConfig.GetSecret(VendorId.Digikey, CredentialTargets.ClientSecretField) is not null;

    public RawHttpRequest BuildSearchRequest(string query, int limit)
    {
        return new RawHttpRequest(
            HttpMethod.Post.Method,
            $"{SearchBaseUrl}?limit={limit}&offset=0",
            LocaleHeaders(),
            JsonSerializer.Serialize(new { Keywords = query, Limit = limit, Offset = 0 }),
            "application/json");
    }

    // The keyword API AND-matches every keyword, so a long natural-language query can
    // legitimately return zero products; each step drops the last token. Only a
    // multi-word query can narrow — a single token has nothing left to drop.
    public string? NarrowSearchQuery(string query)
    {
        var tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Length >= 2 ? string.Join(' ', tokens[..^1]) : null;
    }

    public async Task<RawHttpRequest> ApplySecretsAsync(RawHttpRequest request, KeyConfig keyConfig, CancellationToken cancellationToken)
    {
        var token = await _auth.GetTokenAsync(cancellationToken);
        var stamped = request with
        {
            Headers = new Dictionary<string, string>(request.Headers)
            {
                ["Authorization"] = $"Bearer {token}",
                ["X-DIGIKEY-Client-Id"] = keyConfig.GetSecret(VendorId.Digikey, CredentialTargets.ClientIdField) ?? "",
            },
        };
        return stamped;
    }

    public IReadOnlyList<PartResult> ParseResponse(string rawJson, string query)
    {
        using var document = JsonDocument.Parse(rawJson);
        if (!document.RootElement.TryGetProperty("Products", out var products)
            || products.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        return products.EnumerateArray().Select(MapProduct).ToList();
    }

    public RawHttpRequest BuildDetailsRequest(string vendorCode)
    {
        return new RawHttpRequest(
            HttpMethod.Get.Method,
            $"{DetailsBaseUrl}/{Uri.EscapeDataString(vendorCode)}/productdetails",
            LocaleHeaders(),
            null,
            null);
    }

    public PartResult ParseDetailsResponse(string rawJson)
    {
        using var document = JsonDocument.Parse(rawJson);
        var product = document.RootElement.GetProperty("Product");
        var part = MapProduct(product);

        var parameters = new List<PartParameter>();
        if (product.TryGetProperty("Parameters", out var parameterElements)
            && parameterElements.ValueKind == JsonValueKind.Array)
        {
            foreach (var parameter in parameterElements.EnumerateArray())
            {
                parameters.Add(new PartParameter(
                    Json.GetString(parameter, "ParameterText") ?? "",
                    Json.GetString(parameter, "ValueText") ?? ""));
            }
        }

        var datasheets = new List<string>();
        var datasheetUrl = Json.GetString(product, "DatasheetUrl");
        if (!string.IsNullOrEmpty(datasheetUrl))
            datasheets.Add(datasheetUrl);

        return part with { Parameters = parameters, Datasheets = datasheets };
    }

    // The Digi-Key part number and the ordering-form price breaks live on the package
    // variations; the variation whose first break is the smallest quantity is the
    // small-quantity ordering form and carries the product's canonical pricing.
    private static PartResult MapProduct(JsonElement product)
    {
        var variations = Json.GetArray(product, "ProductVariations") ?? [];
        var pricing = variations
            .Where(v => Json.GetArray(v, "StandardPricing") is { Count: > 0 })
            .Select(v => (Variation: v, FirstBreak: Json.GetArray(v, "StandardPricing")![0]))
            .OrderBy(entry => PriceNormalizer.Parse(entry.FirstBreak.GetProperty("BreakQuantity")) ?? decimal.MaxValue)
            .FirstOrDefault();

        var variation = pricing.Variation;
        var breaks = new List<PriceBreak>();
        if (pricing.Variation.ValueKind == JsonValueKind.Object)
        {
            foreach (var brk in Json.GetArray(pricing.Variation, "StandardPricing")!)
            {
                var quantity = PriceNormalizer.Parse(brk.GetProperty("BreakQuantity"));
                var price = PriceNormalizer.Parse(brk.GetProperty("UnitPrice"));
                if (quantity is > 0 && price is not null)
                    breaks.Add(new PriceBreak((int)quantity, price.Value));
            }
        }

        return new PartResult
        {
            Mpn = Json.GetString(product, "ManufacturerProductNumber") ?? "",
            VendorPartNumber = Json.GetString(variation, "DigiKeyProductNumber")
                ?? Json.GetString(product, "ManufacturerProductNumber") ?? "",
            Description = Json.GetString(Json.GetObject(product, "Description"), "ProductDescription") ?? "",
            Manufacturer = Json.GetString(Json.GetObject(product, "Manufacturer"), "Name") ?? "",
            Stock = Json.GetOptionalInt(product, "QuantityAvailable"),
            UnitPrice = PriceNormalizer.Parse(Json.GetProperty(product, "UnitPrice")),
            Currency = Currency,
            PriceBreaks = breaks,
            Lifecycle = LifecycleMapper.Map(Json.GetString(Json.GetObject(product, "ProductStatus"), "Status")),
            Packaging = Json.GetString(Json.GetObject(variation, "PackageType"), "Name"),
            Category = Json.GetString(Json.GetObject(product, "Category"), "Name"),
            Url = Json.GetString(product, "ProductUrl") ?? "",
            Match = MatchKind.Keyword,
        };
    }

    private static IReadOnlyDictionary<string, string> LocaleHeaders() => new Dictionary<string, string>
    {
        ["X-DIGIKEY-Locale-Site"] = "CA",
        ["X-DIGIKEY-Locale-Language"] = "en",
        ["X-DIGIKEY-Locale-Currency"] = "CAD",
    };
}

// Small JsonElement accessors keeping the vendor mappers null-tolerant and readable.
internal static class Json
{
    internal static JsonElement? GetProperty(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        return element.TryGetProperty(name, out var value) ? value : null;
    }

    internal static string? GetString(JsonElement element, string name) =>
        GetProperty(element, name) is { } value && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    internal static JsonElement GetObject(JsonElement element, string name) =>
        GetProperty(element, name) is { } value && value.ValueKind == JsonValueKind.Object
            ? value
            : default;

    internal static List<JsonElement>? GetArray(JsonElement element, string name)
    {
        if (GetProperty(element, name) is not { } value || value.ValueKind != JsonValueKind.Array) return null;
        return value.EnumerateArray().ToList();
    }

    internal static int? GetOptionalInt(JsonElement element, string name)
    {
        if (GetProperty(element, name) is not { } value) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetInt32(),
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }
}

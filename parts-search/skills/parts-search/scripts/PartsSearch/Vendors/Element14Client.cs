using System.Text.Json;

namespace PartsSearch;

public sealed class Element14Client : IVendorClient
{
    // The decided Canadian-market store; the CAD currency is the store's own pricing and
    // is not echoed per price in the response.
    private const string StoreId = "canada.newark.com";
    private const string Currency = "CAD";

    public VendorId VendorId => VendorId.Newark;

    public bool IsConfigured(KeyConfig keyConfig) =>
        keyConfig.GetSecret(VendorId.Newark, CredentialTargets.ApiKeyField) is not null;

    public RawHttpRequest BuildSearchRequest(string query, int limit)
    {
        return new RawHttpRequest(
            HttpMethod.Get.Method,
            $"https://api.element14.com/catalog/products?term=any:{Uri.EscapeDataString(query)}" +
            $"&storeInfo.id={StoreId}&resultsSettings.responseGroup=large&resultsSettings.offset=0" +
            $"&resultsSettings.numberOfResults={limit}&callInfo.responseDataFormat=json",
            new Dictionary<string, string>(),
            null,
            null);
    }

    public Task<RawHttpRequest> ApplySecretsAsync(RawHttpRequest request, KeyConfig keyConfig, CancellationToken cancellationToken)
    {
        var apiKey = keyConfig.GetSecret(VendorId.Newark, CredentialTargets.ApiKeyField)
            ?? throw new InvalidOperationException("newark api key is not configured");
        var separator = request.Url.Contains('?') ? '&' : '?';
        var stamped = request with { Url = $"{request.Url}{separator}callInfo.apiKey={Uri.EscapeDataString(apiKey)}" };
        return Task.FromResult(stamped);
    }

    // Every query kind — keyword, MPN, order code — rides the field-wide any: search;
    // exactness of an order code is decided by match classification (SKU equals query).
    public IReadOnlyList<PartResult> ParseResponse(string rawJson, string query)
    {
        using var document = JsonDocument.Parse(rawJson);
        var products = FindProducts(document.RootElement);
        if (products is null) return [];
        return products.Select(MapProduct).ToList();
    }

    public RawHttpRequest BuildDetailsRequest(string vendorCode)
    {
        return new RawHttpRequest(
            HttpMethod.Get.Method,
            $"https://api.element14.com/catalog/products?term=id:{Uri.EscapeDataString(vendorCode)}" +
            $"&storeInfo.id={StoreId}&resultsSettings.responseGroup=large&callInfo.responseDataFormat=json",
            new Dictionary<string, string>(),
            null,
            null);
    }

    public PartResult ParseDetailsResponse(string rawJson)
    {
        using var document = JsonDocument.Parse(rawJson);
        var products = FindProducts(document.RootElement)
            ?? throw new InvalidOperationException("element14 details response carries no products");
        if (products.Count == 0)
            throw new InvalidOperationException("element14 details response carries no products");

        var part = MapProduct(products[0]);

        var parameters = new List<PartParameter>();
        if (Json.GetArray(products[0], "attributes") is { } attributes)
        {
            foreach (var attribute in attributes)
            {
                parameters.Add(new PartParameter(
                    Json.GetString(attribute, "attributeLabel") ?? "",
                    Json.GetString(attribute, "attributeValue") ?? ""));
            }
        }

        var datasheets = new List<string>();
        if (Json.GetArray(products[0], "datasheets") is { } datasheetElements)
        {
            foreach (var datasheet in datasheetElements)
            {
                if (Json.GetString(datasheet, "url") is { Length: > 0 } url)
                    datasheets.Add(url);
            }
        }

        return part with { Parameters = parameters, Datasheets = datasheets };
    }

    // The response wrapper names the search kind (keywordSearchReturn for any: searches,
    // premierFarnellPartNumberReturn for id: lookups); the product shape is shared.
    private static List<JsonElement>? FindProducts(JsonElement root)
    {
        foreach (var wrapper in new[] { "keywordSearchReturn", "premierFarnellPartNumberReturn" })
        {
            if (Json.GetObject(root, wrapper) is { } section
                && Json.GetArray(section, "products") is { Count: > 0 } products)
            {
                return products;
            }
        }
        return null;
    }

    private static PartResult MapProduct(JsonElement product)
    {
        var breaks = new List<PriceBreak>();
        if (Json.GetArray(product, "prices") is { } prices)
        {
            foreach (var priceRange in prices)
            {
                var from = PriceNormalizer.Parse(priceRange.GetProperty("from"));
                var cost = PriceNormalizer.Parse(priceRange.GetProperty("cost"));
                if (from is > 0 && cost is not null)
                    breaks.Add(new PriceBreak((int)from, cost.Value));
            }
        }

        return new PartResult
        {
            Mpn = Json.GetString(product, "translatedManufacturerPartNumber") ?? "",
            VendorPartNumber = Json.GetString(product, "sku") ?? "",
            Description = Json.GetString(product, "displayName") ?? "",
            Manufacturer = Json.GetString(product, "brandName") ?? "",
            Stock = Json.GetOptionalInt(Json.GetObject(product, "stock"), "level"),
            UnitPrice = SearchService.UnitPriceAt(breaks, 1),
            Currency = Currency,
            PriceBreaks = breaks,
            Lifecycle = LifecycleMapper.Map(Json.GetString(product, "productStatus")),
            Packaging = null,
            Category = null,
            // The response carries no product URL field; the MPN renders as plain text.
            Url = null,
            Match = MatchKind.Keyword,
        };
    }
}

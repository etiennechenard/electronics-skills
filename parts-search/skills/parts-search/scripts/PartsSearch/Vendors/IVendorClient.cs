namespace PartsSearch;

public interface IVendorClient
{
    VendorId VendorId { get; }

    bool IsConfigured(KeyConfig keyConfig);

    // Pure request build: the returned URL, headers, and body carry no secrets —
    // ApplySecretsAsync stamps them, immediately before the send.
    RawHttpRequest BuildSearchRequest(string query, int limit);

    // The query to retry with when a keyword search for `query` legitimately returns
    // zero results (Digi-Key AND-matches every keyword, so long natural-language
    // queries come back empty); null means this vendor does not narrow — the default.
    string? NarrowSearchQuery(string query) => null;

    Task<RawHttpRequest> ApplySecretsAsync(RawHttpRequest request, KeyConfig keyConfig, CancellationToken cancellationToken);

    IReadOnlyList<PartResult> ParseResponse(string rawJson, string query);

    RawHttpRequest BuildDetailsRequest(string vendorCode);

    PartResult ParseDetailsResponse(string rawJson);
}

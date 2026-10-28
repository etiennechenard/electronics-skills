using System.Diagnostics;
using System.Text;

namespace PartsSearch;

public sealed class SearchService
{
    // The canonical vendors the envelope always enumerates, in fetch and output order;
    // vendors beyond these reach the flow only through the registry.
    private static readonly VendorId[] CanonicalOrder = [VendorId.Digikey, VendorId.Mouser, VendorId.Newark];

    private readonly IReadOnlyDictionary<VendorId, IVendorClient> _clients;
    private readonly List<VendorId> _registryOrder = [];
    private readonly KeyConfig _keyConfig;
    private readonly ResponseCache _cache;
    private readonly RateLimiter _rateLimiter;
    private readonly HttpSend _http;
    private readonly IClock _clock;

    public SearchService(IEnumerable<IVendorClient> clients, KeyConfig keyConfig, ResponseCache cache,
        RateLimiter rateLimiter, HttpSend http, IClock clock)
    {
        _clients = clients.ToDictionary(c => c.VendorId);
        _registryOrder.AddRange(clients.Select(c => c.VendorId));
        _keyConfig = keyConfig;
        _cache = cache;
        _rateLimiter = rateLimiter;
        _http = http;
        _clock = clock;
    }

    public async Task<Envelope> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
    {
        var outcomes = new List<VendorOutcome>();
        foreach (var vendor in ResolveRequestedVendors(request.Vendors))
            outcomes.Add(await SearchVendorAsync(vendor, request, cancellationToken));

        return new Envelope
        {
            Command = CommandKind.Search,
            Query = request.Query,
            Qty = request.Qty,
            AsOf = _clock.UtcNow,
            Vendors = outcomes,
        };
    }

    // Both alternatives stages fetch at the search default so a small --limit cannot
    // fetch past an exact match ranked below the top relevance hit.
    private const int AlternativesFetchLimit = 10;

    private async Task<VendorOutcome> SearchVendorAsync(VendorId vendor, SearchRequest request, CancellationToken cancellationToken)
    {
        var (outcome, _) = await SearchVendorCoreAsync(vendor, request.Query, request.Qty, request.Limit, request.NoCache, cancellationToken);
        return outcome;
    }

    public async Task<Envelope> AlternativesAsync(AlternativesRequest request, CancellationToken cancellationToken = default)
    {
        var outcomes = new List<VendorOutcome>();
        foreach (var vendor in ResolveRequestedVendors(request.Vendors))
            outcomes.Add(await AlternativesVendorAsync(vendor, request, cancellationToken));

        return new Envelope
        {
            Command = CommandKind.Alternatives,
            Query = request.Query,
            Qty = request.Qty,
            AsOf = _clock.UtcNow,
            Vendors = outcomes,
        };
    }

    // The check command: one exact-match search per MPN per requested vendor, strictly
    // sequential in flag order, cached and paced across lines; per-line vocabulary is
    // exactly ok | no_key | error, with "searched, no exact match" collapsed to ok + [].
    public async Task<CheckEnvelope> CheckAsync(CheckRequest request, CancellationToken cancellationToken = default)
    {
        var vendors = ResolveRequestedVendors(request.Vendors);
        var lines = new List<CheckLine>();
        foreach (var mpn in request.Mpns)
        {
            var lineVendors = new List<VendorOutcome>();
            foreach (var vendor in vendors)
                lineVendors.Add(await CheckLineVendorAsync(vendor, mpn, request, cancellationToken));
            lines.Add(new CheckLine { Mpn = mpn, Vendors = lineVendors });
        }

        return new CheckEnvelope
        {
            Qty = request.Qty,
            AsOf = _clock.UtcNow,
            Lines = lines,
        };
    }

    private async Task<VendorOutcome> CheckLineVendorAsync(VendorId vendor, string mpn, CheckRequest request, CancellationToken cancellationToken)
    {
        var (outcome, parts) = await SearchVendorCoreAsync(vendor, mpn, request.Qty, limit: 10, noCache: request.NoCache, cancellationToken);
        if (outcome.Status == VendorStatus.Ok)
        {
            var exact = parts.Where(part => part.Match == MatchKind.Exact).ToList();
            // Check's vocabulary is exact matches for the requested MPN; narrowing
            // metadata from a multi-token MPN never renders in check output, so it goes.
            return outcome with { Parts = exact, NarrowedTo = null };
        }
        if (outcome.Status == VendorStatus.NoResults)
            return outcome with { Status = VendorStatus.Ok, Cached = outcome.Cached, Parts = [] };
        return outcome;
    }

    // The details command: resolve one part through the standard search path, then expand
    // it via the vendor's details endpoint — cached under kind:"details" behind the pacing
    // gate, with `cached` covering both calls and --qty pricing the expanded part.
    public async Task<Envelope> DetailsAsync(DetailsRequest request, CancellationToken cancellationToken = default)
    {
        var outcome = await DetailsVendorAsync(request.Vendor, request, cancellationToken);
        return new Envelope
        {
            Command = CommandKind.Details,
            Query = request.Query,
            Qty = request.Qty,
            AsOf = _clock.UtcNow,
            Vendors = [outcome],
        };
    }

    private async Task<VendorOutcome> DetailsVendorAsync(VendorId vendor, DetailsRequest request, CancellationToken cancellationToken)
    {
        var name = Name(vendor);
        if (!_clients.TryGetValue(vendor, out var client) || !client.IsConfigured(_keyConfig))
        {
            TraceLog.Debug($"{name} configured=no");
            return NoKey(vendor);
        }
        TraceLog.Debug($"{name} configured=yes");

        try
        {
            // Resolve: the standard search path (the search-default limit leaves the
            // pivot rule a field to scan for the first exact match).
            var (stage1, stage1Parts) = await SearchVendorCoreAsync(vendor, request.Query, request.Qty, limit: 10, noCache: request.NoCache, cancellationToken);
            if (stage1.Status != VendorStatus.Ok)
                return stage1;

            var pivot = AlternativesFinder.SelectPivot(stage1Parts);
            if (pivot is null)
                return stage1;

            var expanded = await ExpandAsync(vendor, name, client, pivot, request, cancellationToken);
            return stage1 with { Cached = stage1.Cached == true && expanded.Cached, Parts = [expanded.Part] };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TraceLog.Info($"{name} details error={ex.Message}");
            return Error(vendor, ex.Message);
        }
    }

    private async Task<(PartResult Part, bool Cached)> ExpandAsync(
        VendorId vendor, string name, IVendorClient client, PartResult pivot, DetailsRequest request, CancellationToken cancellationToken)
    {
        var cacheKeyQuery = pivot.VendorPartNumber;
        if (!request.NoCache && _cache.Read(vendor, CacheKind.Details, cacheKeyQuery, limit: 1) is { } hit)
        {
            TraceLog.Info($"{name} details cache=HIT");
            // The cache key excludes qty, so a served response must be re-priced and
            // re-classified for the current query just like a fresh fetch.
            return (AttachQueryData(client.ParseDetailsResponse(hit.Raw), request.Query, request.Qty), Cached: true);
        }

        var detailsRequest = client.BuildDetailsRequest(pivot.VendorPartNumber);
        var wire = await client.ApplySecretsAsync(detailsRequest, _keyConfig, cancellationToken);
        await _rateLimiter.WaitAsync(vendor);
        var stopwatch = Stopwatch.StartNew();
        var sent = await _http.SendAsync(vendor, wire, cancellationToken);
        stopwatch.Stop();

        if (!sent.Succeeded)
            throw new VendorRequestFailedException(sent.Error ?? "vendor request failed", sent.StatusCode);

        TraceLog.Info($"{name} details cache=MISS http={sent.StatusCode} ms={(int)stopwatch.Elapsed.TotalMilliseconds}");
        if (!request.NoCache)
            _cache.Write(vendor, CacheKind.Details, cacheKeyQuery, limit: 1, sent.Body);

        var expanded = AttachQueryData(client.ParseDetailsResponse(sent.Body), request.Query, request.Qty);
        return (expanded, Cached: false);
    }

    // The two-stage alternatives command: stage 1 is the unchanged search path; stage 2
    // probes the deterministic finder query for every vendor whose stage 1 produced a
    // pivot, and the finder classifies, dedups, ranks and limit-caps the combined parts.
    private async Task<VendorOutcome> AlternativesVendorAsync(VendorId vendor, AlternativesRequest request, CancellationToken cancellationToken)
    {
        var (stage1, stage1Parts) = await SearchVendorCoreAsync(vendor, request.Query, request.Qty, AlternativesFetchLimit, request.NoCache, cancellationToken);
        if (stage1.Status != VendorStatus.Ok)
            return stage1;

        var pivot = AlternativesFinder.SelectPivot(stage1Parts);
        if (pivot is null)
            return stage1;

        var stage2Query = AlternativesFinder.BuildStage2Query(pivot);
        TraceLog.Debug($"{Name(vendor)} stage-2 search: {stage2Query}");
        var (stage2, stage2Parts) = await SearchVendorCoreAsync(vendor, stage2Query, request.Qty, AlternativesFetchLimit, request.NoCache, cancellationToken);
        var stage2PartsForClassification = stage2.Status == VendorStatus.Ok ? stage2Parts : [];

        var classified = AlternativesFinder.Classify(stage1Parts, stage2PartsForClassification);

        // The limit caps the alternatives list only; the exact match and variants stay.
        var alternativesKept = 0;
        var parts = new List<PartResult>();
        foreach (var part in classified)
        {
            if (part.Relation == RelationKind.Alternative)
            {
                if (alternativesKept >= request.Limit) continue;
                alternativesKept++;
            }
            parts.Add(part);
        }

        return stage1 with
        {
            Cached = stage1.Cached == true && stage2.Cached == true,
            Parts = parts,
        };
    }

    // The search core: cache-first fetch for the query, and when a vendor that supports
    // keyword narrowing (Digi-Key) legitimately returns zero parts for a multi-word
    // query, retry with each narrower form until something matches. Parts are always
    // match-classified against the requested query, and every intermediate fetch rides
    // the same pacing gate and per-query cache.
    private async Task<(VendorOutcome Outcome, IReadOnlyList<PartResult> Parts)> SearchVendorCoreAsync(
        VendorId vendor, string query, int qty, int limit, bool noCache, CancellationToken cancellationToken)
    {
        var name = Name(vendor);
        if (!_clients.TryGetValue(vendor, out var client) || !client.IsConfigured(_keyConfig))
        {
            TraceLog.Debug($"{name} configured=no");
            return (NoKey(vendor), []);
        }
        TraceLog.Debug($"{name} configured=yes");

        try
        {
            var current = query;
            string? narrowedTo = null;
            var servedFromCache = false;
            IReadOnlyList<PartResult> parts = [];
            var cacheAge = TimeSpan.Zero;
            var statusCode = 0;
            var elapsedMs = 0;

            while (true)
            {
                string raw;
                bool cached;
                if (!noCache)
                {
                    var digest = ResponseCache.CacheKey(vendor, CacheKind.Search, current, limit, ResponseCache.Market);
                    TraceLog.Debug($"{name} search cache-key={digest}");
                    if (_cache.Read(vendor, CacheKind.Search, current, limit) is { } hit)
                    {
                        cached = true;
                        raw = hit.Raw;
                        cacheAge = _clock.UtcNow - hit.FetchedAt;
                    }
                    else
                    {
                        cached = false;
                        (raw, statusCode, elapsedMs) = await FetchAsync(vendor, name, client, current, limit, noCache, cancellationToken);
                    }
                }
                else
                {
                    cached = false;
                    (raw, statusCode, elapsedMs) = await FetchAsync(vendor, name, client, current, limit, noCache, cancellationToken);
                }

                // The flag tracks the response that ends up serving the outcome, so an
                // earlier empty level going to the network cannot mask cache-served parts.
                servedFromCache = cached;

                // Parse and classify against the requested query even when the response
                // was fetched for a narrowed one, so `match` stays truthful. The cap keeps
                // --limit honest on endpoints with no server-side records cap (Mouser's
                // by-number); endpoints that already cap at `limit` are unaffected.
                parts = client.ParseResponse(raw, current)
                    .Select(part => AttachQueryData(part, query, qty))
                    .Take(limit)
                    .ToList();

                if (cached)
                    TraceLog.Info($"{name} search cache=HIT age={HumanAge(cacheAge)} results={parts.Count}");
                else
                    TraceLog.Info($"{name} search cache=MISS http={statusCode} ms={elapsedMs} results={parts.Count}");

                if (parts.Count > 0)
                {
                    narrowedTo = string.Equals(current, query, StringComparison.Ordinal) ? null : current;
                    break;
                }

                var next = client.NarrowSearchQuery(current);
                if (next is null || string.Equals(next, current, StringComparison.Ordinal))
                    break;
                TraceLog.Info($"{name} search empty for \"{current}\", narrowing to \"{next}\"");
                current = next;
            }

            var outcome = new VendorOutcome
            {
                Vendor = vendor,
                Status = parts.Count == 0 ? VendorStatus.NoResults : VendorStatus.Ok,
                Cached = servedFromCache,
                NarrowedTo = parts.Count == 0 ? null : narrowedTo,
                Parts = parts,
            };
            return (outcome, parts);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TraceLog.Info($"{name} search error={ex.Message}");
            return (Error(vendor, ex.Message), []);
        }
    }

    // Miss path: returns the raw body with its status code and elapsed milliseconds; a
    // failed send throws so the caller's isolation turns it into that vendor's error
    // outcome.
    private async Task<(string Body, int StatusCode, int ElapsedMs)> FetchAsync(
        VendorId vendor, string name, IVendorClient client, string query, int limit, bool noCache, CancellationToken cancellationToken)
    {
        var built = client.BuildSearchRequest(query, limit);
        var wire = await client.ApplySecretsAsync(built, _keyConfig, cancellationToken);
        // The gate sits after token acquisition so the token POST and this send stay a
        // full window apart — the details path orders them the same way.
        await _rateLimiter.WaitAsync(vendor);
        TraceLog.Debug($"{name} search url={TraceLog.RedactUrl(wire.Url)}");

        var stopwatch = Stopwatch.StartNew();
        var sent = await _http.SendAsync(vendor, wire, cancellationToken);
        stopwatch.Stop();

        if (!sent.Succeeded)
            throw new VendorRequestFailedException(sent.Error ?? "vendor request failed", sent.StatusCode);

        TraceLog.Debug($"{name} search response-bytes={Encoding.UTF8.GetByteCount(sent.Body)}");
        if (!noCache)
            _cache.Write(vendor, CacheKind.Search, query, limit, sent.Body);
        return (sent.Body, sent.StatusCode, (int)stopwatch.Elapsed.TotalMilliseconds);
    }

    private sealed class VendorRequestFailedException(string message, int statusCode) : Exception(message)
    {
        public int StatusCode { get; } = statusCode;
    }

    internal const string ProbeQuery = "resistor";

    // key test: one limit-1 probe per vendor over the same vendor set a bare search runs.
    // The probe rides the search path's fetch (pacing, secrets at send) but never reads the
    // cache — write-through only — so it always exercises the current key.
    internal async Task<IReadOnlyDictionary<VendorId, string>> ProbeKeysAsync(CancellationToken cancellationToken)
    {
        var statuses = new Dictionary<VendorId, string>();
        foreach (var vendor in ResolveRequestedVendors(null))
            statuses[vendor] = await ProbeVendorAsync(vendor, cancellationToken);
        return statuses;
    }

    private async Task<string> ProbeVendorAsync(VendorId vendor, CancellationToken cancellationToken)
    {
        var name = Name(vendor);
        if (!_clients.TryGetValue(vendor, out var client) || !client.IsConfigured(_keyConfig))
            return "no_key";

        try
        {
            const int probeLimit = 1;
            var (raw, _, _) = await FetchAsync(vendor, name, client, ProbeQuery, probeLimit, noCache: false, cancellationToken);
            var parts = client.ParseResponse(raw, ProbeQuery);
            var status = parts.Count == 0 ? "empty (suspect key type)" : "pass";
            TraceLog.Info($"{name} key test {status}");
            return status;
        }
        catch (VendorRequestFailedException ex) when (ex.StatusCode is 401 or 403)
        {
            TraceLog.Info($"{name} key test rejected");
            return "rejected";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TraceLog.Info($"{name} key test error: {ex.Message}");
            return "error";
        }
    }

    // Canonical vendors first, in canonical order regardless of how they were requested;
    // then any non-canonical requested vendors in requested order, deduplicated.
    private IReadOnlyList<VendorId> ResolveRequestedVendors(IReadOnlyList<VendorId>? requested)
    {
        if (requested is null || requested.Count == 0)
        {
            // All vendors: the canonical three are always enumerated, and a registered
            // vendor beyond them joins the run — registration is its only wiring.
            return [.. CanonicalOrder, .. _registryOrder.Where(v => !CanonicalOrder.Contains(v))];
        }

        var wanted = new HashSet<VendorId>(requested);
        var extras = new HashSet<VendorId>();
        var ordered = new List<VendorId>();
        foreach (var vendor in CanonicalOrder)
        {
            if (wanted.Contains(vendor)) ordered.Add(vendor);
        }
        foreach (var vendor in requested)
        {
            if (!CanonicalOrder.Contains(vendor) && extras.Add(vendor)) ordered.Add(vendor);
        }
        return ordered;
    }

    private static VendorOutcome NoKey(VendorId vendor) => new()
    {
        Vendor = vendor,
        Status = VendorStatus.NoKey,
        Parts = [],
    };

    private static VendorOutcome Error(VendorId vendor, string message) => new()
    {
        Vendor = vendor,
        Status = VendorStatus.Error,
        Error = message,
        Parts = [],
    };

    private static PartResult AttachQueryData(PartResult part, string query, int qty)
    {
        var unitPrice = UnitPriceAt(part.PriceBreaks, qty);
        return part with
        {
            Match = ClassifyMatch(query, part.Mpn, part.VendorPartNumber),
            UnitPrice = unitPrice,
            Currency = unitPrice is null ? null : part.Currency,
            CheaperAt = CheaperAtHint(part.PriceBreaks, qty, unitPrice),
        };
    }

    private static string Name(VendorId vendor) => vendor.ToString().ToLowerInvariant();

    private static string HumanAge(TimeSpan age)
    {
        if (age.TotalHours >= 1) return $"{(int)age.TotalHours}h";
        if (age.TotalMinutes >= 1) return $"{(int)age.TotalMinutes}m";
        return $"{(int)age.TotalSeconds}s";
    }

    // The largest price break whose threshold is at or under the requested quantity;
    // a quantity below the first break still buys at the first break's price.
    internal static decimal? UnitPriceAt(IReadOnlyList<PriceBreak> breaks, int qty)
    {
        if (breaks.Count == 0) return null;
        PriceBreak? best = null;
        foreach (var brk in breaks)
        {
            if (brk.Qty <= qty && (best is null || brk.Qty > best.Qty))
            {
                best = brk;
            }
        }
        return best?.Price ?? breaks.MinBy(b => b.Qty)!.Price;
    }

    // The smallest break above the requested quantity whose total is strictly cheaper
    // than buying the requested quantity at the derived unit price; a tie is no bargain.
    internal static CheaperAt? CheaperAtHint(IReadOnlyList<PriceBreak> breaks, int qty, decimal? unitPrice)
    {
        if (unitPrice is null) return null;
        var currentTotal = unitPrice.Value * qty;
        PriceBreak? best = null;
        foreach (var brk in breaks)
        {
            if (brk.Qty <= qty) continue;
            if (brk.Price * brk.Qty >= currentTotal) continue;
            if (best is null || brk.Qty < best.Qty)
            {
                best = brk;
            }
        }
        if (best is null) return null;
        var bestTotal = best.Price * best.Qty;
        return new CheaperAt(best.Qty, best.Price, bestTotal, currentTotal - bestTotal);
    }

    internal static MatchKind ClassifyMatch(string query, string mpn, string vendorPartNumber) =>
        string.Equals(query, mpn, StringComparison.OrdinalIgnoreCase)
        || string.Equals(query, vendorPartNumber, StringComparison.OrdinalIgnoreCase)
            ? MatchKind.Exact
            : MatchKind.Keyword;
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PartsSearch;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public enum CacheKind
{
    Search,
    Details,
}

public sealed record CacheEntry
{
    public required VendorId Vendor { get; init; }
    public required CacheKind Kind { get; init; }
    public required DateTimeOffset FetchedAt { get; init; }
    public required string Query { get; init; }
    public required string Raw { get; init; }
}

public sealed class ResponseCache
{
    // v1 pins a single market (Canada), but the cache key carries it so a future
    // second market lands on distinct entries.
    public const string Market = "ca";

    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    private readonly string _root;
    private readonly IClock _clock;
    private readonly bool _enabled;

    public ResponseCache(IClock clock, bool enabled = true, string? root = null)
    {
        _clock = clock;
        _enabled = enabled;
        _root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "parts-search", "cache", "resp");
    }

    public static string CacheKey(VendorId vendor, CacheKind kind, string query, int limit, string market)
    {
        // v2: 0.2.0 routes Mouser part-number queries to the by-number endpoint, so
        // pre-upgrade keyword-endpoint entries must miss instead of serving the fuzzy
        // page the routing exists to avoid. Bump per cache-shape change.
        const string version = "v2";
        var material = $"{version}|{vendor.ToString().ToLowerInvariant()}|{kind.ToString().ToLowerInvariant()}|{NormalizedQuery(query)}|{limit}|{market}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    public CacheEntry? Read(VendorId vendor, CacheKind kind, string query, int limit)
    {
        if (!_enabled) return null;
        var path = PathFor(vendor, kind, query, limit);
        if (!File.Exists(path)) return null;
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException)
        {
            return null;
        }
        CacheEntry? entry;
        try
        {
            entry = JsonSerializer.Deserialize<CacheEntry>(text, WireJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
        if (entry is null) return null;
        if (_clock.UtcNow - entry.FetchedAt >= Ttl) return null;
        return entry;
    }

    public void Write(VendorId vendor, CacheKind kind, string query, int limit, string raw)
    {
        if (!_enabled) return;
        Directory.CreateDirectory(_root);
        var entry = new CacheEntry
        {
            Vendor = vendor,
            Kind = kind,
            FetchedAt = _clock.UtcNow,
            Query = NormalizedQuery(query),
            Raw = raw,
        };
        var json = JsonSerializer.Serialize(entry, WireJson.Options);
        var final = PathFor(vendor, kind, query, limit);
        var tmp = $"{final}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tmp, json);
            File.Move(tmp, final, overwrite: true);
        }
        catch (Exception)
        {
            // A failed write or move must not strand this write's temp file.
            try
            {
                File.Delete(tmp);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            throw;
        }
    }

    // Best-effort: sweep and per-file failures are swallowed so pruning never fails a run;
    // an unparsable entry cannot be aged, so it stays.
    public void Prune()
    {
        try
        {
            if (!Directory.Exists(_root)) return;
            foreach (var file in Directory.EnumerateFiles(_root))
            {
                try
                {
                    var entry = JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(file), WireJson.Options);
                    if (entry is not null && _clock.UtcNow - entry.FetchedAt > Retention)
                    {
                        File.Delete(file);
                    }
                }
                catch (Exception ex)
                {
                    TraceLog.Info($"cache prune could not process {Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            TraceLog.Info($"cache prune failed: {ex.Message}");
        }
    }

    private static string NormalizedQuery(string query) => query.Trim().ToLowerInvariant();

    private string PathFor(VendorId vendor, CacheKind kind, string query, int limit) =>
        Path.Combine(_root, CacheKey(vendor, kind, query, limit, Market) + ".json");
}

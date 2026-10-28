using PartsSearch;
using Xunit;

namespace PartsSearch.Tests;

public sealed class FakeClock(DateTimeOffset start) : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = start;

    public void Advance(TimeSpan delta) => UtcNow += delta;
}

public sealed class CacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "parts-search-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeClock _clock = new(DateTimeOffset.UtcNow);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private ResponseCache NewCache(bool enabled = true) => new(_clock, enabled, root: _dir);

    [Fact]
    public void CacheKey_IsDeterministicHex_AndSensitiveToVendorKindLimitMarket()
    {
        var key = ResponseCache.CacheKey(VendorId.Mouser, CacheKind.Search, "tpic6b595dwr", 10, ResponseCache.Market);

        Assert.Equal(64, key.Length);
        Assert.Equal(key, ResponseCache.CacheKey(VendorId.Mouser, CacheKind.Search, "tpic6b595dwr", 10, ResponseCache.Market));
        Assert.NotEqual(key, ResponseCache.CacheKey(VendorId.Newark, CacheKind.Search, "tpic6b595dwr", 10, ResponseCache.Market));
        Assert.NotEqual(key, ResponseCache.CacheKey(VendorId.Mouser, CacheKind.Details, "tpic6b595dwr", 10, ResponseCache.Market));
        Assert.NotEqual(key, ResponseCache.CacheKey(VendorId.Mouser, CacheKind.Search, "tpic6b595dwr", 50, ResponseCache.Market));
        Assert.NotEqual(key, ResponseCache.CacheKey(VendorId.Mouser, CacheKind.Search, "tpic6b595dwr", 10, "us"));
    }

    [Fact]
    public void CacheKey_NormalizesQuery_TrimmedAndLowercased()
    {
        Assert.Equal(
            ResponseCache.CacheKey(VendorId.Digikey, CacheKind.Search, "TPIC6B595DWR", 10, ResponseCache.Market),
            ResponseCache.CacheKey(VendorId.Digikey, CacheKind.Search, "  TPIC6B595DWR  ", 10, ResponseCache.Market));
        Assert.NotEqual(
            ResponseCache.CacheKey(VendorId.Digikey, CacheKind.Search, "tpic6b595dwr", 10, ResponseCache.Market),
            ResponseCache.CacheKey(VendorId.Digikey, CacheKind.Search, "tpic6b595", 10, ResponseCache.Market));
    }

    [Fact]
    public void ReadWrite_RoundTripsRawVerbatim_WithNormalizedQuery()
    {
        var cache = NewCache();
        const string raw = "{\"Products\":[{\"PartNumber\":\"296-1275-1-ND\"}]}";

        cache.Write(VendorId.Digikey, CacheKind.Search, "  TPIC6B595DWR  ", 10, raw);

        var entry = cache.Read(VendorId.Digikey, CacheKind.Search, "tpic6b595dwr", 10);
        Assert.NotNull(entry);
        Assert.Equal(raw, entry.Raw);
        Assert.Equal("tpic6b595dwr", entry.Query);
        Assert.Equal(VendorId.Digikey, entry.Vendor);
        Assert.Equal(CacheKind.Search, entry.Kind);
        Assert.True((entry.FetchedAt - _clock.UtcNow).Duration() < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Ttl_FreshEntryHits_StaleEntryMisses()
    {
        var cache = NewCache();
        cache.Write(VendorId.Mouser, CacheKind.Search, "led", 10, "{}");

        _clock.Advance(TimeSpan.FromHours(23));
        Assert.NotNull(cache.Read(VendorId.Mouser, CacheKind.Search, "led", 10));

        _clock.Advance(TimeSpan.FromHours(2));
        Assert.Null(cache.Read(VendorId.Mouser, CacheKind.Search, "led", 10));
    }

    [Fact]
    public void DetailsKind_DoesNotCollideWithSearchKind()
    {
        var cache = NewCache();
        cache.Write(VendorId.Digikey, CacheKind.Search, "296-1275-1-ND", 10, "{\"search\":true}");

        Assert.Null(cache.Read(VendorId.Digikey, CacheKind.Details, "296-1275-1-ND", 10));

        cache.Write(VendorId.Digikey, CacheKind.Details, "296-1275-1-ND", 10, "{\"details\":true}");
        var details = cache.Read(VendorId.Digikey, CacheKind.Details, "296-1275-1-ND", 10);
        Assert.NotNull(details);
        Assert.Equal("{\"details\":true}", details.Raw);
    }

    [Fact]
    public void NoCacheMode_NeitherReadsNorWrites()
    {
        var cache = NewCache(enabled: false);

        cache.Write(VendorId.Mouser, CacheKind.Search, "led", 10, "{}");
        Assert.False(Directory.Exists(_dir));
        Assert.Null(cache.Read(VendorId.Mouser, CacheKind.Search, "led", 10));

        NewCache().Write(VendorId.Mouser, CacheKind.Search, "led", 10, "{}");
        Assert.Null(NewCache(enabled: false).Read(VendorId.Mouser, CacheKind.Search, "led", 10));
    }

    [Fact]
    public void AtomicWrite_LeavesNoTmpResidue()
    {
        var cache = NewCache();
        cache.Write(VendorId.Mouser, CacheKind.Search, "led", 10, "{}");
        cache.Write(VendorId.Mouser, CacheKind.Search, "led", 10, "{\"v\":2}");

        var files = Directory.EnumerateFiles(_dir).ToList();
        var file = Assert.Single(files);
        Assert.EndsWith(".json", file);
        Assert.DoesNotContain(".tmp", Path.GetFileName(file));
        Assert.Equal(64 + ".json".Length, Path.GetFileName(file).Length);

        var rewritten = cache.Read(VendorId.Mouser, CacheKind.Search, "led", 10);
        Assert.NotNull(rewritten);
        Assert.Equal("{\"v\":2}", rewritten.Raw);
    }

    [Fact]
    public void CorruptEntry_ReadsAsMiss()
    {
        var cache = NewCache();
        cache.Write(VendorId.Mouser, CacheKind.Search, "led", 10, "{}");
        File.WriteAllText(Directory.EnumerateFiles(_dir).Single(), "not json at all");

        Assert.Null(cache.Read(VendorId.Mouser, CacheKind.Search, "led", 10));
    }

    [Fact]
    public void Prune_RemovesOnlyEntriesOlderThanSevenDays()
    {
        var cache = NewCache();
        cache.Write(VendorId.Mouser, CacheKind.Search, "old-entry", 10, "{}");
        var oldFile = Directory.EnumerateFiles(_dir).Single();

        _clock.Advance(TimeSpan.FromDays(8));
        cache.Write(VendorId.Mouser, CacheKind.Search, "new-entry", 10, "{}");

        cache.Prune();

        Assert.False(File.Exists(oldFile));
        Assert.NotNull(cache.Read(VendorId.Mouser, CacheKind.Search, "new-entry", 10));
    }

    [Fact]
    public void Prune_CorruptFiles_SwallowsAndContinuesSweep()
    {
        var cache = NewCache();
        cache.Write(VendorId.Mouser, CacheKind.Search, "old-valid", 10, "{}");
        var oldValid = Directory.EnumerateFiles(_dir).Single();

        _clock.Advance(TimeSpan.FromDays(8));
        cache.Write(VendorId.Mouser, CacheKind.Search, "old-corrupt", 10, "{}");
        var oldCorrupt = Directory.EnumerateFiles(_dir).Single(f => f != oldValid);
        File.WriteAllText(oldCorrupt, "torn json {");
        File.WriteAllText(Path.Combine(_dir, "torn-from-interrupted-run.json.tmp"), "garbage");

        cache.Prune();

        Assert.False(File.Exists(oldValid));
        Assert.True(File.Exists(oldCorrupt));
        Assert.True(File.Exists(Path.Combine(_dir, "torn-from-interrupted-run.json.tmp")));

        cache.Write(VendorId.Mouser, CacheKind.Search, "after-prune", 10, "{}");
        Assert.NotNull(cache.Read(VendorId.Mouser, CacheKind.Search, "after-prune", 10));
    }

    [Fact]
    public void Prune_MissingDirectory_NoThrow()
    {
        NewCache().Prune();
    }
}

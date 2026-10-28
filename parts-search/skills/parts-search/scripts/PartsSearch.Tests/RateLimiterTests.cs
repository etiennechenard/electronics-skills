using PartsSearch;
using Xunit;

namespace PartsSearch.Tests;

public class RateLimiterTests
{
    [Fact]
    public async Task ConsecutiveDigiKeyCalls_Observe600msSpacing()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var waits = new List<TimeSpan>();
        var limiter = new RateLimiter(clock, delay: t =>
        {
            waits.Add(t);
            clock.Advance(t);
            return Task.CompletedTask;
        });

        await limiter.WaitAsync(VendorId.Digikey);
        Assert.Empty(waits);

        clock.Advance(TimeSpan.FromMilliseconds(100));
        await limiter.WaitAsync(VendorId.Digikey);
        Assert.Single(waits, TimeSpan.FromMilliseconds(500));

        await limiter.WaitAsync(VendorId.Digikey);
        Assert.Single(waits, TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public async Task NonDigiKeyVendors_PassThroughUnDelayed()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var waits = new List<TimeSpan>();
        var limiter = new RateLimiter(clock, delay: t =>
        {
            waits.Add(t);
            clock.Advance(t);
            return Task.CompletedTask;
        });

        await limiter.WaitAsync(VendorId.Mouser);
        await limiter.WaitAsync(VendorId.Mouser);
        await limiter.WaitAsync(VendorId.Newark);

        Assert.Empty(waits);
    }
}

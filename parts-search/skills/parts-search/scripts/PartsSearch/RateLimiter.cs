namespace PartsSearch;

public sealed class RateLimiter
{
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(600);

    private readonly IClock _clock;
    private readonly Func<TimeSpan, Task> _delay;
    private DateTimeOffset _lastGatedSend = DateTimeOffset.MinValue;

    public RateLimiter(IClock clock, Func<TimeSpan, Task>? delay = null)
    {
        _clock = clock;
        _delay = delay ?? Task.Delay;
    }

    public async Task WaitAsync(VendorId vendor)
    {
        if (vendor != VendorId.Digikey) return;
        var remaining = Window - (_clock.UtcNow - _lastGatedSend);
        if (remaining > TimeSpan.Zero) await _delay(remaining);
        _lastGatedSend = _clock.UtcNow;
    }
}

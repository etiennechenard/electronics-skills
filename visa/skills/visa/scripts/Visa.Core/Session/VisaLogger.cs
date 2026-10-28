namespace Visa.Core.Session;

public enum LogLevel { Quiet, Info, Debug }

public static class VisaLogger
{
    private static string _prefix = "[visa]";
    private static LogLevel _level = LogLevel.Info;

    // Meaningful warning codes and their symbolic names (values from VISA spec)
    private static readonly Dictionary<int, string> WarningSymbols = new()
    {
        [0x3FFF0005] = "termchar",   // VI_SUCCESS_TERM_CHAR
        [0x3FFF0006] = "maxcnt",     // VI_SUCCESS_MAX_CNT
    };

    public static void Configure(string prefix, LogLevel level)
    {
        _prefix = prefix;
        _level = level;
    }

    public static string Format(string opname, string context, int status)
    {
        var result = status < 0 ? "err" : "ok";
        var hex = $"0x{(uint)status:X8}";
        var suffix = WarningSymbols.TryGetValue(status, out var sym) ? $" ({sym})" : string.Empty;
        return $"{_prefix} {opname} {context} {result} {hex}{suffix}";
    }

    public static void Trace(string opname, string context, int status)
    {
        if (_level == LogLevel.Quiet) return;
        Console.Error.WriteLine(Format(opname, context, status));
    }

    // Debug-level: full hex dump for text I/O payloads
    public static void TracePayload(string opname, string context, byte[] payload)
    {
        if (_level != LogLevel.Debug) return;
        var hex = Convert.ToHexString(payload).ToLowerInvariant();
        Console.Error.WriteLine($"{_prefix} {opname} {context} payload {hex}");
    }

    // Debug-level: byte count + leading bytes only — never dumps the full binary capture
    public static void TraceBlock(string opname, string context, byte[] payload)
    {
        if (_level != LogLevel.Debug) return;
        const int maxLeading = 16;
        var leading = payload.Length <= maxLeading ? payload : payload[..maxLeading];
        var hex = Convert.ToHexString(leading).ToLowerInvariant();
        var suffix = payload.Length > maxLeading ? "..." : string.Empty;
        Console.Error.WriteLine($"{_prefix} {opname} {context} block {payload.Length} bytes [{hex}{suffix}]");
    }
}

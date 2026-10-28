namespace PartsSearch;

public enum TraceLevel
{
    Quiet,
    Info,
    Debug,
}

public static class TraceLog
{
    private const string Prefix = "[parts-search]";
    private static TraceLevel _level = TraceLevel.Info;

    public static void Configure(TraceLevel level) => _level = level;

    public static void ConfigureFromEnvironment()
    {
        var raw = Environment.GetEnvironmentVariable("PARTS_SEARCH_LOG_LEVEL");
        _level = raw?.ToLowerInvariant() switch
        {
            "quiet" => TraceLevel.Quiet,
            "debug" => TraceLevel.Debug,
            _ => TraceLevel.Info,
        };
    }

    public static void Info(string message) => Write(TraceLevel.Info, message);

    public static void Debug(string message) => Write(TraceLevel.Debug, message);

    // Every query parameter whose name contains "key" — apiKey, callInfo.apiKey, … —
    // is masked before a URL is traced; matching is case-insensitive so unusual
    // vendor spellings cannot leak.
    public static string RedactUrl(string url)
    {
        var queryStart = url.IndexOf('?');
        if (queryStart < 0) return url;

        var head = url[..queryStart];
        var pairs = url[(queryStart + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries);
        var rebuilt = new List<string>(pairs.Length);
        foreach (var pair in pairs)
        {
            var separator = pair.IndexOf('=');
            var name = separator < 0 ? pair : pair[..separator];
            rebuilt.Add(name.Contains("key", StringComparison.OrdinalIgnoreCase) ? $"{name}=***" : pair);
        }
        return $"{head}?{string.Join("&", rebuilt)}";
    }

    private static void Write(TraceLevel level, string message)
    {
        if (level > _level) return;
        Console.Error.WriteLine($"{Prefix} {message}");
    }
}

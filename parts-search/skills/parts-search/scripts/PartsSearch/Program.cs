using System.Globalization;

namespace PartsSearch;

public class Program
{
    internal static async Task<int> Main(string[] args)
    {
        TraceLog.ConfigureFromEnvironment();
        var parse = ParseCli(args);
        if (parse is ParsedUsageError usage)
        {
            // Usage errors are always JSON on stdout, whatever the requested format.
            Console.Out.WriteLine(Render.Error(usage.Error));
            return 2;
        }

        try
        {
            return await DispatchAsync(parse);
        }
        catch (KeyUsageException ex)
        {
            Console.Out.WriteLine(Render.Error(new ErrorObject
            {
                Command = CommandKind.Key,
                Error = ErrorKind.Usage,
                Description = ex.Message,
            }));
            return 2;
        }
        catch (Exception ex)
        {
            Console.Out.WriteLine(Render.Error(new ErrorObject
            {
                Command = CommandOf(parse),
                Error = ErrorKind.Internal,
                Description = ex.Message,
            }));
            return 4;
        }
    }

    private static async Task<int> DispatchAsync(CliParse parse)
    {
        var store = new Win32CredentialStore();
        var keyConfig = new KeyConfig(store);
        var clock = new SystemClock();
        var cache = new ResponseCache(clock);
        var rateLimiter = new RateLimiter(clock);
        var http = new HttpSend(new HttpClientTransport());
        cache.Prune();

        switch (parse)
        {
            case ParsedSearch search:
            {
                var envelope = await BuildService().SearchAsync(search.Request);
                Console.Out.WriteLine(Render.Envelope(envelope, search.Request.Format).TrimEnd());
                return 0;
            }
            case ParsedKey key:
            {
                var commands = new KeyCommands(store, keyConfig, new KeyConsole());
                string output;
                switch (key.Args.Sub)
                {
                    case "set":
                        output = commands.Set(key.Args);
                        break;
                    case "list":
                        output = commands.List();
                        break;
                    case "clear":
                        output = commands.Clear(key.Args);
                        break;
                    case "test":
                        output = await commands.TestAsync(BuildService(), CancellationToken.None);
                        break;
                    default:
                        throw new InvalidOperationException($"unhandled key subcommand: {key.Args.Sub}");
                }
                Console.Out.WriteLine(output);
                return 0;
            }
            case ParsedAlternatives alternatives:
            {
                var envelope = await BuildService().AlternativesAsync(alternatives.Request);
                Console.Out.WriteLine(Render.Envelope(envelope, alternatives.Request.Format).TrimEnd());
                return 0;
            }
            case ParsedDetails details:
            {
                var envelope = await BuildService().DetailsAsync(details.Request);
                Console.Out.WriteLine(Render.Envelope(envelope, details.Request.Format).TrimEnd());
                return 0;
            }
            case ParsedCheck check:
            {
                var envelope = await BuildService().CheckAsync(check.Request);
                Console.Out.WriteLine(Render.Check(envelope, check.Request.Format).TrimEnd());
                return 0;
            }
            default:
                throw new InvalidOperationException($"unhandled parse outcome: {parse.GetType().Name}");
        }

        SearchService BuildService() => new(
            [
                new DigiKeyClient(new DigiKeyAuth(keyConfig, http, clock, rateLimiter)),
                new MouserClient(),
                new Element14Client(),
            ],
            keyConfig, cache, rateLimiter, http, clock);
    }

    private static CommandKind CommandOf(CliParse parse) => parse switch
    {
        ParsedSearch => CommandKind.Search,
        ParsedAlternatives => CommandKind.Alternatives,
        ParsedDetails => CommandKind.Details,
        ParsedCheck => CommandKind.Check,
        ParsedKey => CommandKind.Key,
        _ => CommandKind.Unknown,
    };

    internal static CliParse ParseCli(string[] args)
    {
        if (args.Length == 0)
            return Usage(CommandKind.Unknown, "no command given; expected one of: search, alternatives, details, check, key");

        return args[0] switch
        {
            "search" => ParseSearchLike(CommandKind.Search, args[1..]),
            "alternatives" => ParseSearchLike(CommandKind.Alternatives, args[1..]),
            "details" => ParseDetails(args[1..]),
            "check" => ParseCheck(args[1..]),
            "key" => ParseKey(args[1..]),
            _ => Usage(CommandKind.Unknown, $"unknown command '{args[0]}'; expected one of: search, alternatives, details, check, key"),
        };
    }

    private static readonly HashSet<string> SearchFlags = ["q", "vendor", "qty", "limit", "format", "no-cache"];
    private static readonly HashSet<string> DetailsFlags = ["q", "vendor", "qty", "format", "no-cache"];
    private static readonly HashSet<string> CheckFlags = ["mpn", "vendor", "qty", "format", "no-cache"];

    private static CliParse ParseSearchLike(CommandKind kind, string[] rest)
    {
        var raw = new RawArgs();
        if (Walk(kind, rest, SearchFlags, raw) is { } walkError) return walkError;
        if (raw.Query is null) return Usage(kind, "missing required flag '--q'");
        if (raw.Query.Length == 0) return Usage(kind, "--q: value must be non-empty");
        if (ParseVendorSubset(kind, raw.Vendor, out var vendors) is { } vendorError) return vendorError;
        if (ParseQty(kind, raw.Qty, out var qty) is { } qtyError) return qtyError;
        if (ParseLimit(kind, raw.Limit, out var limit) is { } limitError) return limitError;
        if (ParseFormat(kind, raw.Format, out var format) is { } formatError) return formatError;

        return kind == CommandKind.Search
            ? new ParsedSearch(new SearchRequest(raw.Query, vendors, qty, limit, format, raw.NoCache))
            : new ParsedAlternatives(new AlternativesRequest(raw.Query, vendors, qty, limit, format, raw.NoCache));
    }

    private static CliParse ParseDetails(string[] rest)
    {
        var raw = new RawArgs();
        if (Walk(CommandKind.Details, rest, DetailsFlags, raw) is { } walkError) return walkError;
        if (raw.Query is null) return Usage(CommandKind.Details, "missing required flag '--q'");
        if (raw.Query.Length == 0) return Usage(CommandKind.Details, "--q: value must be non-empty");
        if (raw.Vendor is null)
            return Usage(CommandKind.Details, "details requires --vendor digikey | mouser | newark (no all)");
        if (raw.Vendor == "all")
            return Usage(CommandKind.Details, "details requires exactly one vendor; 'all' is not accepted");
        if (raw.Vendor.Contains(','))
            return Usage(CommandKind.Details, "details requires exactly one vendor");
        if (!TryVendorToken(raw.Vendor, out var vendor))
            return Usage(CommandKind.Details, $"--vendor: unknown token '{raw.Vendor}'; expected digikey | mouser | newark");
        if (ParseQty(CommandKind.Details, raw.Qty, out var qty) is { } qtyError) return qtyError;
        if (ParseFormat(CommandKind.Details, raw.Format, out var format) is { } formatError) return formatError;

        return new ParsedDetails(new DetailsRequest(raw.Query, vendor, qty, format, raw.NoCache));
    }

    private static CliParse ParseCheck(string[] rest)
    {
        var raw = new RawArgs();
        if (Walk(CommandKind.Check, rest, CheckFlags, raw) is { } walkError) return walkError;
        if (raw.Mpns.Count == 0) return Usage(CommandKind.Check, "check requires at least one --mpn");
        if (raw.Mpns.Any(m => m.Length == 0)) return Usage(CommandKind.Check, "--mpn: value must be non-empty");
        if (ParseVendorSubset(CommandKind.Check, raw.Vendor, out var vendors) is { } vendorError) return vendorError;
        if (ParseQty(CommandKind.Check, raw.Qty, out var qty) is { } qtyError) return qtyError;
        if (ParseFormat(CommandKind.Check, raw.Format, out var format) is { } formatError) return formatError;

        return new ParsedCheck(new CheckRequest(raw.Mpns, vendors, qty, format, raw.NoCache));
    }

    private static CliParse ParseKey(string[] rest)
    {
        if (rest.Length == 0)
            return Usage(CommandKind.Key, "key requires a subcommand: set | list | test | clear");

        var sub = rest[0];
        switch (sub)
        {
            case "set":
            case "clear":
                if (rest.Length < 2)
                    return Usage(CommandKind.Key, $"key {sub} requires a vendor token: digikey | mouser | newark");
                if (rest.Length > 2)
                    return Usage(CommandKind.Key, $"unexpected argument '{rest[2]}'");
                if (!TryVendorToken(rest[1], out var vendor))
                    return Usage(CommandKind.Key, $"key {sub}: unknown vendor token '{rest[1]}'; expected digikey | mouser | newark");
                return new ParsedKey(new KeyCommandArgs(sub, vendor));
            case "list":
            case "test":
                if (rest.Length > 1)
                    return Usage(CommandKind.Key, $"key {sub} takes no vendor");
                return new ParsedKey(new KeyCommandArgs(sub, null));
            default:
                return Usage(CommandKind.Key, $"unknown key subcommand '{sub}'; expected set | list | test | clear");
        }
    }

    // Returns the usage error, or null when the argv tail parses cleanly.
    private static CliParse? Walk(CommandKind kind, string[] rest, HashSet<string> allowed, RawArgs raw)
    {
        for (var i = 0; i < rest.Length; i++)
        {
            var arg = rest[i];
            if (!arg.StartsWith("--"))
                return Usage(kind, $"unexpected argument '{arg}'");
            if (arg.Contains('='))
                return Usage(kind, $"'--flag=value' syntax is not supported; use '--flag value' instead (got '{arg}')");
            var name = arg[2..];
            if (!allowed.Contains(name))
                return Usage(kind, $"unknown flag '{arg}'");
            if (name == "no-cache")
            {
                raw.NoCache = true;
                continue;
            }
            if (i + 1 >= rest.Length || rest[i + 1].StartsWith("--"))
                return Usage(kind, $"flag '{arg}' requires a value");
            var value = rest[++i];
            switch (name)
            {
                case "q":
                    if (raw.Query is not null) return DuplicateFlag(kind, arg);
                    raw.Query = value;
                    break;
                case "vendor":
                    if (raw.Vendor is not null) return DuplicateFlag(kind, arg);
                    raw.Vendor = value;
                    break;
                case "qty":
                    if (raw.Qty is not null) return DuplicateFlag(kind, arg);
                    raw.Qty = value;
                    break;
                case "limit":
                    if (raw.Limit is not null) return DuplicateFlag(kind, arg);
                    raw.Limit = value;
                    break;
                case "format":
                    if (raw.Format is not null) return DuplicateFlag(kind, arg);
                    raw.Format = value;
                    break;
                case "mpn":
                    raw.Mpns.Add(value);
                    break;
            }
        }

        return null;
    }

    private static CliParse? ParseVendorSubset(CommandKind kind, string? value, out IReadOnlyList<VendorId>? vendors)
    {
        vendors = null;
        if (value is null || value == "all") return null;
        var list = new List<VendorId>();
        foreach (var token in value.Split(','))
        {
            if (!TryVendorToken(token, out var vendor))
                return Usage(kind, $"--vendor: unknown token '{token}'; expected all | digikey | mouser | newark");
            list.Add(vendor);
        }
        vendors = list;
        return null;
    }

    private static bool TryVendorToken(string token, out VendorId vendor)
    {
        switch (token)
        {
            case "digikey": vendor = VendorId.Digikey; return true;
            case "mouser": vendor = VendorId.Mouser; return true;
            case "newark": vendor = VendorId.Newark; return true;
            default: vendor = default; return false;
        }
    }

    private static CliParse? ParseQty(CommandKind kind, string? value, out int qty)
    {
        if (value is null) { qty = 1; return null; }
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out qty) && qty >= 1 && qty <= 100000)
            return null;
        return Usage(kind, $"--qty: value '{value}' out of domain 1-100000");
    }

    private static CliParse? ParseLimit(CommandKind kind, string? value, out int limit)
    {
        if (value is null) { limit = 10; return null; }
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out limit) && limit >= 1 && limit <= 50)
            return null;
        return Usage(kind, $"--limit: value '{value}' out of domain 1-50");
    }

    private static CliParse? ParseFormat(CommandKind kind, string? value, out OutputFormat format)
    {
        switch (value)
        {
            case null: format = OutputFormat.Md; return null;
            case "json": format = OutputFormat.Json; return null;
            case "table": format = OutputFormat.Table; return null;
            case "md": format = OutputFormat.Md; return null;
            default:
                format = OutputFormat.Md;
                return Usage(kind, $"--format: unknown token '{value}'; expected json | table | md");
        }
    }

    private static CliParse DuplicateFlag(CommandKind kind, string arg) =>
        Usage(kind, $"flag '{arg}' was specified more than once");

    private static ParsedUsageError Usage(CommandKind command, string description) =>
        new(new ErrorObject { Command = command, Error = ErrorKind.Usage, Description = description });

    private sealed class RawArgs
    {
        public string? Query;
        public string? Vendor;
        public string? Qty;
        public string? Limit;
        public string? Format;
        public bool NoCache;
        public List<string> Mpns { get; } = [];
    }
}

internal sealed record KeyCommandArgs(string Sub, VendorId? Vendor);

internal abstract record CliParse;

internal sealed record ParsedSearch(SearchRequest Request) : CliParse;

internal sealed record ParsedAlternatives(AlternativesRequest Request) : CliParse;

internal sealed record ParsedDetails(DetailsRequest Request) : CliParse;

internal sealed record ParsedCheck(CheckRequest Request) : CliParse;

internal sealed record ParsedKey(KeyCommandArgs Args) : CliParse;

internal sealed record ParsedUsageError(ErrorObject Error) : CliParse;

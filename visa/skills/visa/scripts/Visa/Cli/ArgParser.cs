namespace Visa.Cli;

public enum FlagKind { Value, Switch, Repeatable }

public class FlagSpec
{
    public required string Name { get; init; }
    public FlagKind Kind { get; init; } = FlagKind.Value;
    public bool Required { get; init; }
}

public class ParsedArgs
{
    public IReadOnlyList<string> Positionals { get; }
    public bool HelpRequested { get; }

    private readonly IReadOnlyDictionary<string, string> _flags;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _repeatable;
    private readonly IReadOnlySet<string> _switches;

    internal ParsedArgs(bool helpRequested)
    {
        HelpRequested = helpRequested;
        Positionals = [];
        _flags = new Dictionary<string, string>();
        _repeatable = new Dictionary<string, IReadOnlyList<string>>();
        _switches = new HashSet<string>();
    }

    internal ParsedArgs(
        List<string> positionals,
        Dictionary<string, string> flags,
        Dictionary<string, IReadOnlyList<string>> repeatable,
        HashSet<string> switches)
    {
        Positionals = positionals.AsReadOnly();
        _flags = flags;
        _repeatable = repeatable;
        _switches = switches;
    }

    public bool HasFlag(string name) => _flags.ContainsKey(name);
    public string GetFlag(string name) => _flags[name];
    public string GetFlagOrDefault(string name, string defaultValue) =>
        _flags.TryGetValue(name, out var v) ? v : defaultValue;
    public bool GetSwitch(string name) => _switches.Contains(name);
    public IReadOnlyList<string> GetRepeatable(string name) =>
        _repeatable.TryGetValue(name, out var v) ? v : [];
}

public class ArgParser
{
    private readonly int _minPositionals;
    private readonly int _maxPositionals;
    private readonly List<FlagSpec> _specs;
    private readonly List<string[]> _mutuallyExclusive;

    public ArgParser(int positionals, IEnumerable<FlagSpec> flagSpecs,
        IEnumerable<string[]>? mutuallyExclusive = null)
        : this(positionals, positionals, flagSpecs, mutuallyExclusive) { }

    public ArgParser(int minPositionals, int maxPositionals, IEnumerable<FlagSpec> flagSpecs,
        IEnumerable<string[]>? mutuallyExclusive = null)
    {
        _minPositionals = minPositionals;
        _maxPositionals = maxPositionals;
        _specs = flagSpecs.ToList();
        _mutuallyExclusive = mutuallyExclusive?.ToList() ?? [];
    }

    public ParsedArgs Parse(string[] args)
    {
        if (args.Any(a => a == "--help"))
            return new ParsedArgs(helpRequested: true);

        var flagLookup = _specs.ToDictionary(f => f.Name, f => f, StringComparer.Ordinal);

        var positionals = new List<string>();
        var flags = new Dictionary<string, string>(StringComparer.Ordinal);
        var repeatable = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var repeatableMutable = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var switches = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.StartsWith("--"))
            {
                if (arg.Contains('='))
                    throw new UsageException("'--flag=value' syntax is not supported; use '--flag value' instead.", "argparse");

                var name = arg[2..];

                if (!flagLookup.TryGetValue(name, out var spec))
                    throw new UsageException($"Unknown flag '--{name}'.", "argparse");

                if (spec.Kind == FlagKind.Switch)
                {
                    switches.Add(name);
                    continue;
                }

                if (i + 1 >= args.Length || args[i + 1].StartsWith("--"))
                    throw new UsageException($"Flag '--{name}' requires a value.", "argparse");

                var value = args[++i];

                if (spec.Kind == FlagKind.Repeatable)
                {
                    if (!repeatableMutable.TryGetValue(name, out var list))
                    {
                        list = [];
                        repeatableMutable[name] = list;
                    }
                    list.Add(value);
                }
                else
                {
                    if (flags.ContainsKey(name))
                        throw new UsageException($"Flag '--{name}' was specified more than once.", "argparse");
                    flags[name] = value;
                }
            }
            else
            {
                if (positionals.Count >= _maxPositionals)
                    throw new UsageException($"Unexpected argument '{arg}'.", "argparse");
                positionals.Add(arg);
            }
        }

        if (positionals.Count < _minPositionals)
            throw new UsageException($"Missing required positional argument (got {positionals.Count}, need at least {_minPositionals}).", "argparse");

        foreach (var spec in _specs.Where(s => s.Required))
        {
            bool present = spec.Kind switch
            {
                FlagKind.Repeatable => repeatableMutable.ContainsKey(spec.Name),
                FlagKind.Switch => switches.Contains(spec.Name),
                _ => flags.ContainsKey(spec.Name)
            };
            if (!present)
                throw new UsageException($"Missing required flag '--{spec.Name}'.", "argparse");
        }

        foreach (var group in _mutuallyExclusive)
        {
            var present = group.Where(n =>
                flags.ContainsKey(n) || switches.Contains(n) || repeatableMutable.ContainsKey(n)
            ).ToList();
            if (present.Count > 1)
                throw new UsageException(
                    $"Flags {string.Join(" and ", present.Select(n => $"--{n}"))} are mutually exclusive.",
                    "argparse");
        }

        foreach (var kv in repeatableMutable)
            repeatable[kv.Key] = kv.Value.AsReadOnly();

        return new ParsedArgs(positionals, flags, repeatable, switches);
    }
}

namespace Visa.Cli;

public static class UsbControlFlags
{
    public static IReadOnlyList<FlagSpec> Specs { get; } = new FlagSpec[]
    {
        new() { Name = "request-type", Required = true },
        new() { Name = "request",      Required = true },
        new() { Name = "value",        Required = true },
        new() { Name = "index",        Required = true },
        new() { Name = "length",       Required = true },
    };

    public static IReadOnlyList<FlagSpec> SpecsWithoutLength { get; } = new FlagSpec[]
    {
        new() { Name = "request-type", Required = true },
        new() { Name = "request",      Required = true },
        new() { Name = "value",        Required = true },
        new() { Name = "index",        Required = true },
    };

    public static short ParseSigned(string flagName, string input)
    {
        long val;
        if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!long.TryParse(input[2..], System.Globalization.NumberStyles.HexNumber, null, out val))
                throw new UsageException($"--{flagName}: invalid hex value '{input}'.");
        }
        else if (!long.TryParse(input, out val))
        {
            throw new UsageException($"--{flagName}: invalid value '{input}'.");
        }

        if (val < short.MinValue || val > short.MaxValue)
            throw new UsageException($"--{flagName}: value {val} is out of signed 16-bit range ({short.MinValue}–{short.MaxValue}).");

        return (short)val;
    }

    public static ushort ParseUnsigned(string flagName, string input)
    {
        long val;
        if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!long.TryParse(input[2..], System.Globalization.NumberStyles.HexNumber, null, out val))
                throw new UsageException($"--{flagName}: invalid hex value '{input}'.");
        }
        else if (!long.TryParse(input, out val))
        {
            throw new UsageException($"--{flagName}: invalid value '{input}'.");
        }

        if (val < 0 || val > ushort.MaxValue)
            throw new UsageException($"--{flagName}: value {val} is out of unsigned 16-bit range (0–{ushort.MaxValue}).");

        return (ushort)val;
    }

    public static short GetRequestType(ParsedArgs args) =>
        ParseSigned("request-type", args.GetFlag("request-type"));

    public static short GetRequest(ParsedArgs args) =>
        ParseSigned("request", args.GetFlag("request"));

    public static ushort GetValue(ParsedArgs args) =>
        ParseUnsigned("value", args.GetFlag("value"));

    public static ushort GetIndex(ParsedArgs args) =>
        ParseUnsigned("index", args.GetFlag("index"));

    public static ushort GetLength(ParsedArgs args) =>
        ParseUnsigned("length", args.GetFlag("length"));
}

using Visa.Core.Protocol;

namespace Visa.Cli;

public static class SerialFlags
{
    public static IReadOnlyList<FlagSpec> Specs { get; } = new FlagSpec[]
    {
        new() { Name = "baud" },
        new() { Name = "data-bits" },
        new() { Name = "parity" },
        new() { Name = "stop-bits" },
        new() { Name = "flow-control" },
    };

    public static SerialConfig Extract(ParsedArgs args)
    {
        var config = new SerialConfig();

        if (args.HasFlag("baud"))
        {
            var s = args.GetFlag("baud");
            if (!uint.TryParse(s, out var baud))
                throw new UsageException($"--baud: invalid value '{s}'.");
            try { config.SetBaud(baud); }
            catch (ArgumentException ex) { throw new UsageException(ex.Message); }
        }

        if (args.HasFlag("data-bits"))
        {
            var s = args.GetFlag("data-bits");
            if (!byte.TryParse(s, out var bits))
                throw new UsageException($"--data-bits: invalid value '{s}'.");
            try { config.SetDataBits(bits); }
            catch (ArgumentException ex) { throw new UsageException(ex.Message); }
        }

        if (args.HasFlag("parity"))
        {
            try { config.SetParity(args.GetFlag("parity")); }
            catch (ArgumentException ex) { throw new UsageException(ex.Message); }
        }

        if (args.HasFlag("stop-bits"))
        {
            try { config.SetStopBits(args.GetFlag("stop-bits")); }
            catch (ArgumentException ex) { throw new UsageException(ex.Message); }
        }

        if (args.HasFlag("flow-control"))
        {
            try { config.SetFlowControl(args.GetFlag("flow-control")); }
            catch (ArgumentException ex) { throw new UsageException(ex.Message); }
        }

        return config;
    }
}

using Visa.Cli;
using Visa.Core.Session;
using Visa.Json;

namespace Visa.Commands;

public class InfoCommand : ICommandHandler
{
    public string HelpText =>
        "info <address> [--timeout <ms>] [serial flags]\n" +
        "  Open a resource, send *IDN?, return the identifier.\n" +
        "  --timeout <ms>  VISA timeout in milliseconds (default: 5000)\n" +
        "  Serial flags: --baud, --data-bits, --parity, --stop-bits, --flow-control";

    public int Run(string[] args)
    {
        var specs = new List<FlagSpec> { new() { Name = "timeout" } };
        specs.AddRange(SerialFlags.Specs);

        var parser = new ArgParser(1, specs);
        var parsed = parser.Parse(args);

        var address = parsed.Positionals[0];

        var timeoutStr = parsed.GetFlagOrDefault("timeout", "5000");
        if (!int.TryParse(timeoutStr, out var timeoutMs) || timeoutMs < 0)
            throw new UsageException($"--timeout: invalid value '{timeoutStr}'.", "argparse");

        var serialConfig = SerialFlags.Extract(parsed);

        try
        {
            using var session = new VisaSession();
            session.Open(address, TimeSpan.FromMilliseconds(timeoutMs), serialConfig);
            session.Write("*IDN?");
            var identifier = session.ReadLine(out _);
            ResultWriter.WriteSuccess(new Dictionary<string, object?>
            {
                ["address"]    = address,
                ["identifier"] = identifier,
            });
            return 0;
        }
        catch (VisaException ex)
        {
            ResultWriter.WriteError("visa", ex.Operation, ex.Description,
                context: new Dictionary<string, object?> { ["address"] = address },
                statusInt: ex.Status);
            return 1;
        }
    }
}

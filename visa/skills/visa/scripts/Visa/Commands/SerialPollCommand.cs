using Visa.Cli;
using Visa.Core.Session;
using Visa.Json;

namespace Visa.Commands;

public class SerialPollCommand : ICommandHandler
{
    public string HelpText =>
        "serial-poll <address> [--timeout <ms>] [serial flags]\n" +
        "  Read the status byte (viReadSTB) from a VISA resource.\n" +
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
            var statusByte = session.ReadStb();
            ResultWriter.WriteSuccess(new Dictionary<string, object?>
            {
                ["address"]         = address,
                ["status_byte"]     = (int)statusByte,
                ["status_byte_hex"] = $"0x{statusByte:x2}",
                ["rqs"]             = (statusByte & 0x40) != 0,
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

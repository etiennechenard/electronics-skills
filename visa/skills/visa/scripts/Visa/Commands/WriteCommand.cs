using Visa.Cli;
using Visa.Core.Session;
using Visa.Json;

namespace Visa.Commands;

public class WriteCommand : ICommandHandler
{
    public string HelpText =>
        "write <address> <command> [--timeout <ms>] [--no-eoi] [serial flags]\n" +
        "  Write a command; do not read.\n" +
        "  --timeout <ms> VISA timeout in milliseconds (default: 5000)\n" +
        "  --no-eoi       Suppress EOI on the write\n" +
        "  Serial flags: --baud, --data-bits, --parity, --stop-bits, --flow-control";

    public int Run(string[] args)
    {
        var specs = new List<FlagSpec>
        {
            new() { Name = "timeout" },
            new() { Name = "no-eoi", Kind = FlagKind.Switch },
        };
        specs.AddRange(SerialFlags.Specs);

        var parser = new ArgParser(2, specs);
        var parsed = parser.Parse(args);

        var address = parsed.Positionals[0];
        var command = parsed.Positionals[1];

        var timeoutStr = parsed.GetFlagOrDefault("timeout", "5000");
        if (!int.TryParse(timeoutStr, out var timeoutMs) || timeoutMs < 0)
            throw new UsageException($"--timeout: invalid value '{timeoutStr}'.", "argparse");

        var noEoi = parsed.GetSwitch("no-eoi");
        var serialConfig = SerialFlags.Extract(parsed);

        try
        {
            using var session = new VisaSession();
            session.Open(address, TimeSpan.FromMilliseconds(timeoutMs), serialConfig);
            if (noEoi)
                session.SetSendEndEnabled(false);
            var bytesWritten = session.Write(command);
            ResultWriter.WriteSuccess(new Dictionary<string, object?>
            {
                ["address"]       = address,
                ["command"]       = command,
                ["bytes_written"] = bytesWritten,
            });
            return 0;
        }
        catch (VisaException ex)
        {
            ResultWriter.WriteError("visa", ex.Operation, ex.Description,
                context: new Dictionary<string, object?> { ["address"] = address, ["command"] = command },
                statusInt: ex.Status);
            return 1;
        }
    }
}

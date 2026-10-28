using Visa.Cli;
using Visa.Core.Session;
using Visa.Json;

namespace Visa.Commands;

public class GpibCommandCommand : ICommandHandler
{
    public string HelpText =>
        "gpib-command <board> --hex <bytes> [--timeout <ms>]\n" +
        "  Send raw GPIB command bytes (viGpibCommand).\n" +
        "  <board>         GPIB board: 'GPIB<N>' or 'GPIB<N>::INTFC'\n" +
        "  --hex <bytes>   Hex string (upper/lower, with/without spaces, even digit count)\n" +
        "  --timeout <ms>  VISA timeout in milliseconds (default: 5000)";

    public int Run(string[] args)
    {
        var parser = new ArgParser(1,
        [
            new FlagSpec { Name = "hex", Required = true },
            new FlagSpec { Name = "timeout" },
        ]);
        var parsed = parser.Parse(args);

        var (board, resource) = BoardId.Normalize(parsed.Positionals[0]);
        var hexInput = parsed.GetFlag("hex");
        var bytes = HexBytes.Parse(hexInput);
        var hex = HexBytes.Format(bytes);

        var timeoutStr = parsed.GetFlagOrDefault("timeout", "5000");
        if (!int.TryParse(timeoutStr, out var timeoutMs) || timeoutMs < 0)
            throw new UsageException($"--timeout: invalid value '{timeoutStr}'.", "argparse");

        try
        {
            var bytesSent = VisaBoard.Command(resource, bytes, TimeSpan.FromMilliseconds(timeoutMs));
            ResultWriter.WriteSuccess(new Dictionary<string, object?>
            {
                ["board"]      = board,
                ["resource"]   = resource,
                ["operation"]  = "gpib-command",
                ["bytes_sent"] = (int)bytesSent,
                ["hex"]        = hex,
            });
            return 0;
        }
        catch (VisaException ex)
        {
            ResultWriter.WriteError("visa", ex.Operation, ex.Description,
                context: new Dictionary<string, object?>
                {
                    ["board"]    = board,
                    ["resource"] = resource,
                    ["hex"]      = hex,
                },
                statusInt: ex.Status);
            return 1;
        }
    }
}

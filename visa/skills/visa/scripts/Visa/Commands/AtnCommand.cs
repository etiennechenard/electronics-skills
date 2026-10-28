using Visa.Cli;
using Visa.Core.Session;
using Visa.Json;

namespace Visa.Commands;

public class AtnCommand : ICommandHandler
{
    public string HelpText =>
        "atn <board> <on|off> [--timeout <ms>]\n" +
        "  Assert or deassert Attention (viGpibControlATN) on a GPIB board.\n" +
        "  <board>         GPIB board: 'GPIB<N>' or 'GPIB<N>::INTFC'\n" +
        "  <on|off>        Assert ('on') or deassert ('off') ATN\n" +
        "  --timeout <ms>  VISA timeout in milliseconds (default: 5000)";

    public int Run(string[] args)
    {
        var parser = new ArgParser(2, [new FlagSpec { Name = "timeout" }]);
        var parsed = parser.Parse(args);

        var (board, resource) = BoardId.Normalize(parsed.Positionals[0]);
        var stateStr = parsed.Positionals[1];

        bool assert = stateStr switch
        {
            "on"  => true,
            "off" => false,
            _     => throw new UsageException($"atn state must be 'on' or 'off'; got '{stateStr}'.", "validate"),
        };

        var timeoutStr = parsed.GetFlagOrDefault("timeout", "5000");
        if (!int.TryParse(timeoutStr, out var timeoutMs) || timeoutMs < 0)
            throw new UsageException($"--timeout: invalid value '{timeoutStr}'.", "argparse");

        try
        {
            VisaBoard.ControlAtn(resource, assert, TimeSpan.FromMilliseconds(timeoutMs));
            ResultWriter.WriteSuccess(new Dictionary<string, object?>
            {
                ["board"]     = board,
                ["resource"]  = resource,
                ["operation"] = "atn",
                ["state"]     = stateStr,
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
                    ["state"]    = stateStr,
                },
                statusInt: ex.Status);
            return 1;
        }
    }
}

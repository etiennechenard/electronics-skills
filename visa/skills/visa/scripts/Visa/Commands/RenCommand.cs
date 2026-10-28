using Visa.Cli;
using Visa.Core.Session;
using Visa.Json;

namespace Visa.Commands;

public class RenCommand : ICommandHandler
{
    public string HelpText =>
        "ren <board> <on|off> [--timeout <ms>]\n" +
        "  Assert or deassert Remote Enable (viGpibControlREN) on a GPIB board.\n" +
        "  <board>         GPIB board: 'GPIB<N>' or 'GPIB<N>::INTFC'\n" +
        "  <on|off>        Assert ('on') or deassert ('off') REN\n" +
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
            _     => throw new UsageException($"ren state must be 'on' or 'off'; got '{stateStr}'.", "validate"),
        };

        var timeoutStr = parsed.GetFlagOrDefault("timeout", "5000");
        if (!int.TryParse(timeoutStr, out var timeoutMs) || timeoutMs < 0)
            throw new UsageException($"--timeout: invalid value '{timeoutStr}'.", "argparse");

        try
        {
            VisaBoard.ControlRen(resource, assert, TimeSpan.FromMilliseconds(timeoutMs));
            ResultWriter.WriteSuccess(new Dictionary<string, object?>
            {
                ["board"]     = board,
                ["resource"]  = resource,
                ["operation"] = "ren",
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

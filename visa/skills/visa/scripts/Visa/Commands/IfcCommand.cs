using Visa.Cli;
using Visa.Core.Session;
using Visa.Json;

namespace Visa.Commands;

public class IfcCommand : ICommandHandler
{
    public string HelpText =>
        "ifc <board> [--timeout <ms>]\n" +
        "  Send interface clear (viGpibSendIFC) to a GPIB board.\n" +
        "  <board>         GPIB board: 'GPIB<N>' or 'GPIB<N>::INTFC'\n" +
        "  --timeout <ms>  VISA timeout in milliseconds (default: 5000)";

    public int Run(string[] args)
    {
        var parser = new ArgParser(1, [new FlagSpec { Name = "timeout" }]);
        var parsed = parser.Parse(args);

        var (board, resource) = BoardId.Normalize(parsed.Positionals[0]);

        var timeoutStr = parsed.GetFlagOrDefault("timeout", "5000");
        if (!int.TryParse(timeoutStr, out var timeoutMs) || timeoutMs < 0)
            throw new UsageException($"--timeout: invalid value '{timeoutStr}'.", "argparse");

        try
        {
            VisaBoard.SendIfc(resource, TimeSpan.FromMilliseconds(timeoutMs));
            ResultWriter.WriteSuccess(new Dictionary<string, object?>
            {
                ["board"]     = board,
                ["resource"]  = resource,
                ["operation"] = "ifc",
            });
            return 0;
        }
        catch (VisaException ex)
        {
            ResultWriter.WriteError("visa", ex.Operation, ex.Description,
                context: new Dictionary<string, object?> { ["board"] = board, ["resource"] = resource },
                statusInt: ex.Status);
            return 1;
        }
    }
}

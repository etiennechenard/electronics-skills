using Visa.Cli;
using Visa.Core.Session;
using Visa.Json;

namespace Visa.Commands;

public class ListCommand : ICommandHandler
{
    public string HelpText =>
        "list [--pattern <expr>]\n" +
        "  Enumerate VISA resources matching a search expression.\n" +
        "  --pattern <expr>  VISA viFindRsrc search expression (default: ?*INSTR)";

    public int Run(string[] args)
    {
        var parser = new ArgParser(0, [new FlagSpec { Name = "pattern" }]);
        var parsed = parser.Parse(args);

        var pattern = parsed.GetFlagOrDefault("pattern", "?*INSTR");

        try
        {
            var resources = VisaSession.FindResources(pattern);
            ResultWriter.WriteSuccess(new Dictionary<string, object?>
            {
                ["pattern"]   = pattern,
                ["resources"] = (IEnumerable<string>)resources,
            });
            return 0;
        }
        catch (VisaException ex)
        {
            ResultWriter.WriteError("visa", ex.Operation, ex.Description,
                context: new Dictionary<string, object?> { ["pattern"] = pattern },
                statusInt: ex.Status);
            return 1;
        }
    }
}

using Visa.Json;

namespace Visa.Commands;

public class NotImplementedHandler : ICommandHandler
{
    private readonly string _subcommand;

    public NotImplementedHandler(string subcommand) => _subcommand = subcommand;

    public string HelpText => $"Subcommand '{_subcommand}' is not yet implemented.";

    public int Run(string[] args)
    {
        ResultWriter.WriteError("usage", "dispatch", $"Subcommand '{_subcommand}' is not yet implemented.");
        return 2;
    }
}

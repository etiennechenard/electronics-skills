namespace Visa.Commands;

public interface ICommandHandler
{
    string HelpText { get; }
    int Run(string[] args);
}

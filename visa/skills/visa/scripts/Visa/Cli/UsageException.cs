namespace Visa.Cli;

public class UsageException : Exception
{
    public string Operation { get; }

    public UsageException(string message, string operation = "validate")
        : base(message)
    {
        Operation = operation;
    }
}

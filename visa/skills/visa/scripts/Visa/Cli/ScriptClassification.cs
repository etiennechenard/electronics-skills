namespace Visa.Cli;

public static class ScriptClassification
{
    public enum Kind { Write, Query }

    public static Kind Classify(string command)
    {
        var trimmed = command.Trim();
        return trimmed.EndsWith('?') ? Kind.Query : Kind.Write;
    }
}

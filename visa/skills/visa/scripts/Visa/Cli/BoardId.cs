using System.Text.RegularExpressions;

namespace Visa.Cli;

public static class BoardId
{
    private static readonly Regex ShortForm = new(@"^GPIB[0-9]+$", RegexOptions.Compiled);
    private static readonly Regex IntfcForm = new(@"^GPIB[0-9]+::INTFC$", RegexOptions.Compiled);

    public static (string board, string resource) Normalize(string input)
    {
        if (string.IsNullOrEmpty(input))
            throw new UsageException($"board id must be 'GPIB<N>' or 'GPIB<N>::INTFC'; got ''.");

        if (ShortForm.IsMatch(input))
            return (input, $"{input}::INTFC");

        if (IntfcForm.IsMatch(input))
            return (input, input);

        throw new UsageException($"board id must be 'GPIB<N>' or 'GPIB<N>::INTFC'; got '{input}'.");
    }
}

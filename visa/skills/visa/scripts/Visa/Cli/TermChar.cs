namespace Visa.Cli;

public static class TermChar
{
    public static byte Parse(string input)
    {
        if (input == "NL") return 10;
        if (input == "CR") return 13;

        if (int.TryParse(input, out var value))
        {
            if (value < 0 || value > 255)
                throw new UsageException($"--term-char value must be 0–255; got {value}.");
            return (byte)value;
        }

        throw new UsageException($"--term-char value must be 'NL', 'CR', or a decimal integer 0–255; got '{input}'.");
    }

    /// <summary>
    /// Parse the <c>--term-char-en</c> value (<c>0</c> or <c>1</c>) into a read-termination-char
    /// enable flag. Overrides the interface-aware default chosen at session open.
    /// </summary>
    public static bool ParseEnable(string input) => input switch
    {
        "1" => true,
        "0" => false,
        _ => throw new UsageException($"--term-char-en value must be 0 or 1; got '{input}'."),
    };
}

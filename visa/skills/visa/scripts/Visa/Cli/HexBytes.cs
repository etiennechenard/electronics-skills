namespace Visa.Cli;

public static class HexBytes
{
    public static byte[] Parse(string input)
    {
        if (string.IsNullOrEmpty(input))
            throw new UsageException("hex string cannot be empty.");

        var digits = input.Replace(" ", "");

        if (digits.Length % 2 != 0)
            throw new UsageException($"hex string must contain an even number of digits; got {digits.Length}.");

        foreach (var c in digits)
        {
            if (!Uri.IsHexDigit(c))
                throw new UsageException($"invalid hex character '{c}'.");
        }

        var result = new byte[digits.Length / 2];
        for (int i = 0; i < result.Length; i++)
            result[i] = Convert.ToByte(digits.Substring(i * 2, 2), 16);

        return result;
    }

    public static string Format(byte[] bytes) =>
        Convert.ToHexString(bytes).ToLowerInvariant();
}

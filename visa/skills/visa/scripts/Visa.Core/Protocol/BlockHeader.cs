namespace Visa.Core.Protocol;

public static class BlockHeader
{
    public readonly record struct ParseResult(int PayloadOffset, int? DeclaredLength, bool IsIndefinite);

    /// <summary>
    /// Parse an IEEE 488.2 arbitrary-block header from the start of <paramref name="data"/>.
    /// Throws <see cref="FormatException"/> on malformed input with a specific message.
    /// </summary>
    public static ParseResult Parse(byte[] data)
    {
        // Skip leading ASCII whitespace
        int pos = 0;
        while (pos < data.Length && data[pos] <= 0x20 && data[pos] != 0)
            pos++;

        if (pos >= data.Length || data[pos] != (byte)'#')
            throw new FormatException("No '#' found in response. Try --raw to skip 488.2 parsing.");

        pos++; // advance past '#'

        if (pos >= data.Length || data[pos] < (byte)'0' || data[pos] > (byte)'9')
            throw new FormatException("Expected a digit after '#'. Try --raw to skip 488.2 parsing.");

        byte nByte = data[pos];
        pos++;

        if (nByte == (byte)'0')
        {
            // Indefinite-length block: payload starts at pos, strip one trailing \n or \r
            int payloadStart = pos;
            int payloadEnd = data.Length;
            if (payloadEnd > payloadStart && (data[payloadEnd - 1] == '\n' || data[payloadEnd - 1] == '\r'))
                payloadEnd--;
            // Return declaredLength = payloadEnd - payloadStart (the actual content length after stripping)
            // but per spec returns null for indefinite
            return new ParseResult(payloadStart, null, IsIndefinite: true);
        }

        // Definite-length: N = nByte - '0', read N ASCII digits
        int n = nByte - '0';
        if (pos + n > data.Length)
            throw new FormatException(
                $"Not enough length digits: expected {n}, got {data.Length - pos}. Try --raw to skip 488.2 parsing.");

        int declaredLength = 0;
        for (int i = 0; i < n; i++)
        {
            byte d = data[pos + i];
            if (d < (byte)'0' || d > (byte)'9')
                throw new FormatException(
                    $"Non-digit in length field at position {pos + i}. Try --raw to skip 488.2 parsing.");
            declaredLength = declaredLength * 10 + (d - '0');
        }
        pos += n;

        int payloadOffset = pos;
        if (payloadOffset + declaredLength > data.Length)
            throw new FormatException(
                $"Declared length {declaredLength} exceeds available bytes {data.Length - payloadOffset}. Try --raw to skip 488.2 parsing.");

        return new ParseResult(payloadOffset, declaredLength, IsIndefinite: false);
    }

    /// <summary>
    /// Format a definite-length 488.2 block header for a payload of <paramref name="payloadLength"/> bytes.
    /// Returns the header bytes (e.g. 2048 → "#42048").
    /// </summary>
    public static byte[] Format(int payloadLength)
    {
        var lengthStr = payloadLength.ToString();
        var header = $"#{lengthStr.Length}{lengthStr}";
        return System.Text.Encoding.ASCII.GetBytes(header);
    }
}

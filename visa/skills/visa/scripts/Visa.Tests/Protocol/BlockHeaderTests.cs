using Visa.Core.Protocol;
using Xunit;

namespace Visa.Tests.Protocol;

public class BlockHeaderTests
{
    // Definite: #42048 -> offset 6, length 2048
    [Fact]
    public void Parse_definite_4digit_length()
    {
        var data = BuildBlock("#42048", 2048);
        var result = BlockHeader.Parse(data);
        Assert.Equal(6, result.PayloadOffset);
        Assert.Equal(2048, result.DeclaredLength);
        Assert.False(result.IsIndefinite);
    }

    // Definite: #15 -> offset 3, length 5
    [Fact]
    public void Parse_definite_1digit_length()
    {
        var data = BuildBlock("#15", 5);
        var result = BlockHeader.Parse(data);
        Assert.Equal(3, result.PayloadOffset);
        Assert.Equal(5, result.DeclaredLength);
        Assert.False(result.IsIndefinite);
    }

    // Indefinite: #0<payload>\n -> isIndefinite, trailing \n excluded
    [Fact]
    public void Parse_indefinite_strips_trailing_newline()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03 };
        var data = new byte[] { (byte)'#', (byte)'0' }
            .Concat(payload)
            .Append((byte)'\n')
            .ToArray();
        var result = BlockHeader.Parse(data);
        Assert.Equal(2, result.PayloadOffset);
        Assert.Null(result.DeclaredLength);
        Assert.True(result.IsIndefinite);
        // The payload bytes after offset, excluding the trailing \n, are 3 bytes
        var payloadSlice = data[result.PayloadOffset..^1];
        Assert.Equal(3, payloadSlice.Length);
        Assert.Equal(payload, payloadSlice);
    }

    // Indefinite: #0<payload> with no trailing newline -> all remaining bytes
    [Fact]
    public void Parse_indefinite_no_trailing_newline()
    {
        var data = new byte[] { (byte)'#', (byte)'0', 0x01, 0x02 };
        var result = BlockHeader.Parse(data);
        Assert.Equal(2, result.PayloadOffset);
        Assert.Null(result.DeclaredLength);
        Assert.True(result.IsIndefinite);
    }

    // Leading whitespace tolerance
    [Fact]
    public void Parse_skips_leading_whitespace()
    {
        var inner = BuildBlock("#15", 5);
        var data = new byte[] { (byte)' ', (byte)' ' }.Concat(inner).ToArray();
        var result = BlockHeader.Parse(data);
        Assert.Equal(2 + 3, result.PayloadOffset); // 2 spaces + 3 header bytes
        Assert.Equal(5, result.DeclaredLength);
    }

    // Malformed: no '#'
    [Fact]
    public void Parse_fails_no_hash()
    {
        var data = new byte[] { 0x42, 0x15 };
        var ex = Assert.Throws<FormatException>(() => BlockHeader.Parse(data));
        Assert.Contains("#", ex.Message);
    }

    // Malformed: '#' not followed by a digit
    [Fact]
    public void Parse_fails_hash_not_followed_by_digit()
    {
        var data = new byte[] { (byte)'#', (byte)'X', 0x01, 0x02 };
        var ex = Assert.Throws<FormatException>(() => BlockHeader.Parse(data));
        Assert.Contains("digit", ex.Message);
    }

    // Malformed: fewer than N length digits available
    [Fact]
    public void Parse_fails_too_few_length_digits()
    {
        // #4 followed by only 2 digits, not 4
        var data = new byte[] { (byte)'#', (byte)'4', (byte)'2', (byte)'0' };
        var ex = Assert.Throws<FormatException>(() => BlockHeader.Parse(data));
        Assert.Contains("length", ex.Message.ToLowerInvariant());
    }

    // Malformed: declared length exceeds bytes actually read
    [Fact]
    public void Parse_fails_declared_length_exceeds_data()
    {
        // Declares 100 bytes but only provides 3
        var data = new byte[] { (byte)'#', (byte)'3', (byte)'1', (byte)'0', (byte)'0', 0x01, 0x02, 0x03 };
        var ex = Assert.Throws<FormatException>(() => BlockHeader.Parse(data));
        Assert.Contains("100", ex.Message);
    }

    // Definite-length: declared count is authoritative; trailing bytes after payload are ignored
    [Fact]
    public void Parse_definite_trailing_bytes_ignored()
    {
        var payload = new byte[5];
        var data = new byte[] { (byte)'#', (byte)'1', (byte)'5' }
            .Concat(payload)
            .Concat(new byte[] { 0x0A, 0xFF }) // trailing bytes
            .ToArray();
        var result = BlockHeader.Parse(data);
        Assert.Equal(3, result.PayloadOffset);
        Assert.Equal(5, result.DeclaredLength);
    }

    // Format: length 0
    [Fact]
    public void Format_length_0()
    {
        var header = System.Text.Encoding.ASCII.GetString(BlockHeader.Format(0));
        Assert.Equal("#10", header);
    }

    // Format: length 5
    [Fact]
    public void Format_length_5()
    {
        var header = System.Text.Encoding.ASCII.GetString(BlockHeader.Format(5));
        Assert.Equal("#15", header);
    }

    // Format: length 2048
    [Fact]
    public void Format_length_2048()
    {
        var header = System.Text.Encoding.ASCII.GetString(BlockHeader.Format(2048));
        Assert.Equal("#42048", header);
    }

    // Format: rollover 9 -> #19
    [Fact]
    public void Format_rollover_9()
    {
        var header = System.Text.Encoding.ASCII.GetString(BlockHeader.Format(9));
        Assert.Equal("#19", header);
    }

    // Format: rollover 10 -> #210
    [Fact]
    public void Format_rollover_10()
    {
        var header = System.Text.Encoding.ASCII.GetString(BlockHeader.Format(10));
        Assert.Equal("#210", header);
    }

    // Helper: build a byte array with a definite-length block header + payload
    private static byte[] BuildBlock(string header, int payloadLength)
    {
        var headerBytes = System.Text.Encoding.ASCII.GetBytes(header);
        var payload = new byte[payloadLength];
        return headerBytes.Concat(payload).ToArray();
    }
}

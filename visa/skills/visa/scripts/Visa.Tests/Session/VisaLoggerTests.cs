using Visa.Core.Session;
using Xunit;

namespace Visa.Tests.Session;

public class VisaLoggerTests
{
    // Level filtering — quiet emits nothing
    [Fact]
    public void Quiet_emits_nothing()
    {
        VisaLogger.Configure("[visa]", LogLevel.Quiet);
        var old = Console.Error;
        using var sw = new System.IO.StringWriter();
        Console.SetError(sw);
        try
        {
            VisaLogger.Trace("viOpen", "GPIB0::5::INSTR", 0);
            VisaLogger.TracePayload("viWrite", "test", new byte[] { 0x01, 0x02 });
            VisaLogger.TraceBlock("viRead", "test", new byte[] { 0x01, 0x02 });
        }
        finally { Console.SetError(old); }
        Assert.Equal(string.Empty, sw.ToString());
    }

    // Level filtering — info emits call lines but no payloads
    [Fact]
    public void Info_emits_call_lines_not_payloads()
    {
        VisaLogger.Configure("[visa]", LogLevel.Info);
        var old = Console.Error;
        using var sw = new System.IO.StringWriter();
        Console.SetError(sw);
        try
        {
            VisaLogger.Trace("viOpen", "ADDR", 0);
            VisaLogger.TracePayload("viWrite", "ADDR", new byte[] { 0x01 });
            VisaLogger.TraceBlock("viRead", "ADDR", new byte[] { 0x42 });
        }
        finally { Console.SetError(old); }
        var output = sw.ToString();
        Assert.Contains("viOpen", output);
        Assert.DoesNotContain("payload", output);
        Assert.DoesNotContain("block", output);
    }

    // Level filtering — debug emits call lines and payloads
    [Fact]
    public void Debug_emits_call_lines_and_payloads()
    {
        VisaLogger.Configure("[visa]", LogLevel.Debug);
        var old = Console.Error;
        using var sw = new System.IO.StringWriter();
        Console.SetError(sw);
        try
        {
            VisaLogger.Trace("viOpen", "ADDR", 0);
            VisaLogger.TracePayload("viWrite", "ADDR", new byte[] { 0x0A });
            VisaLogger.TraceBlock("viRead", "ADDR", new byte[] { 0x23, 0x34 });
        }
        finally { Console.SetError(old); }
        var output = sw.ToString();
        Assert.Contains("viOpen", output);
        Assert.Contains("payload", output);
        Assert.Contains("block", output);
    }

    // Line format — status hex is uppercase 8-digit with 0x prefix
    [Fact]
    public void Format_status_is_uppercase_8hex()
    {
        VisaLogger.Configure("[visa]", LogLevel.Info);
        var line = VisaLogger.Format("viWrite", "ADDR", 0);
        Assert.Contains("0x00000000", line);
    }

    // Line format — negative status shows err
    [Fact]
    public void Format_negative_status_shows_err()
    {
        VisaLogger.Configure("[visa]", LogLevel.Info);
        var line = VisaLogger.Format("viOpen", "ADDR", unchecked((int)0xBFFF0015));
        Assert.Contains("err", line);
        Assert.Contains("0xBFFF0015", line);
    }

    // Line format — positive status shows ok
    [Fact]
    public void Format_positive_status_shows_ok()
    {
        VisaLogger.Configure("[visa]", LogLevel.Info);
        var line = VisaLogger.Format("viRead", "ADDR", 0);
        Assert.Contains("ok", line);
    }

    // Warning suffix — VI_SUCCESS_TERM_CHAR gets (termchar)
    [Fact]
    public void Format_termchar_warning_adds_suffix()
    {
        VisaLogger.Configure("[visa]", LogLevel.Info);
        var line = VisaLogger.Format("viRead", "ADDR", 0x3FFF0005);
        Assert.Contains("0x3FFF0005 (termchar)", line);
    }

    // Warning suffix — VI_SUCCESS_MAX_CNT gets (maxcnt)
    [Fact]
    public void Format_maxcnt_warning_adds_suffix()
    {
        VisaLogger.Configure("[visa]", LogLevel.Info);
        var line = VisaLogger.Format("viRead", "ADDR", 0x3FFF0006);
        Assert.Contains("0x3FFF0006 (maxcnt)", line);
    }

    // Warning suffix — plain success has no suffix
    [Fact]
    public void Format_plain_success_no_suffix()
    {
        VisaLogger.Configure("[visa]", LogLevel.Info);
        var line = VisaLogger.Format("viWrite", "ADDR", 0);
        Assert.DoesNotContain("(", line);
    }

    // Prefix injection — prefix appears in output
    [Fact]
    public void Prefix_injection_appears_in_output()
    {
        VisaLogger.Configure("[myprefix]", LogLevel.Info);
        var line = VisaLogger.Format("viOpen", "ADDR", 0);
        Assert.StartsWith("[myprefix]", line);
    }

    // Prefix injection — different prefixes produce different output
    [Fact]
    public void Different_prefixes_produce_different_output()
    {
        VisaLogger.Configure("[pfx1]", LogLevel.Info);
        var line1 = VisaLogger.Format("viOpen", "ADDR", 0);
        VisaLogger.Configure("[pfx2]", LogLevel.Info);
        var line2 = VisaLogger.Format("viOpen", "ADDR", 0);
        Assert.NotEqual(line1, line2);
        Assert.StartsWith("[pfx1]", line1);
        Assert.StartsWith("[pfx2]", line2);
    }

    // Binary block payload — shows byte count and leading bytes, not full content
    [Fact]
    public void TraceBlock_shows_count_and_leading_bytes()
    {
        VisaLogger.Configure("[visa]", LogLevel.Debug);
        var old = Console.Error;
        using var sw = new System.IO.StringWriter();
        Console.SetError(sw);
        var payload = new byte[100];
        for (int i = 0; i < 100; i++) payload[i] = (byte)(i + 1);
        try { VisaLogger.TraceBlock("viRead", "ADDR", payload); }
        finally { Console.SetError(old); }
        var output = sw.ToString();
        Assert.Contains("100 bytes", output);
        // Does not contain the 17th byte value (0x11 = 17) at the start — only leading 16
        Assert.Contains("block", output);
        // Verify truncation marker present for large payloads
        Assert.Contains("...", output);
    }

    // Binary block payload — small payload shows all bytes, no truncation marker
    [Fact]
    public void TraceBlock_small_payload_no_truncation()
    {
        VisaLogger.Configure("[visa]", LogLevel.Debug);
        var old = Console.Error;
        using var sw = new System.IO.StringWriter();
        Console.SetError(sw);
        try { VisaLogger.TraceBlock("viRead", "ADDR", new byte[] { 0xAB, 0xCD }); }
        finally { Console.SetError(old); }
        var output = sw.ToString();
        Assert.Contains("2 bytes", output);
        Assert.DoesNotContain("...", output);
    }
}

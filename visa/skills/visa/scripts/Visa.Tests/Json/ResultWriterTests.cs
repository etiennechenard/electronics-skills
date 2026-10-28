using System.Text.Json;
using Visa.Json;
using Xunit;

namespace Visa.Tests.Json;

public class ResultWriterTests
{
    private static string CaptureStdout(Action action)
    {
        var original = Console.Out;
        using var sw = new System.IO.StringWriter();
        Console.SetOut(sw);
        try { action(); }
        finally { Console.SetOut(original); }
        return sw.ToString().TrimEnd('\r', '\n');
    }

    [Fact]
    public void SuccessEnvelope_OkFieldFirst()
    {
        var payload = new Dictionary<string, object?> { ["address"] = "GPIB0::5::INSTR" };
        var json = CaptureStdout(() => ResultWriter.WriteSuccess(payload));
        var doc = JsonDocument.Parse(json);
        var props = doc.RootElement.EnumerateObject().ToList();
        Assert.Equal("ok", props[0].Name);
        Assert.True(props[0].Value.GetBoolean());
    }

    [Fact]
    public void ErrorEnvelope_WithStatusInt_IncludesStatusFields()
    {
        var json = CaptureStdout(() =>
            ResultWriter.WriteError("visa", "viRead", "Timeout expired.", null, statusInt: -1073807339));
        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("visa", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("0xBFFF0015", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal(-1073807339, doc.RootElement.GetProperty("status_int").GetInt32());
    }

    [Fact]
    public void ErrorEnvelope_WithoutStatusInt_NoStatusFields()
    {
        var json = CaptureStdout(() =>
            ResultWriter.WriteError("usage", "argparse", "Missing required argument."));
        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("status", out _));
        Assert.False(doc.RootElement.TryGetProperty("status_int", out _));
    }

    [Fact]
    public void Output_IsSingleLine()
    {
        var payload = new Dictionary<string, object?> { ["value"] = "test" };
        var json = CaptureStdout(() => ResultWriter.WriteSuccess(payload));
        Assert.DoesNotContain('\n', json);
    }

    [Fact]
    public void Output_IsNewlineTerminated()
    {
        var payload = new Dictionary<string, object?> { ["value"] = "test" };
        var original = Console.Out;
        using var sw = new System.IO.StringWriter();
        Console.SetOut(sw);
        try { ResultWriter.WriteSuccess(payload); }
        finally { Console.SetOut(original); }
        Assert.EndsWith("\n", sw.ToString().Replace("\r\n", "\n"));
    }

    [Fact]
    public void SpecialCharsInDescription_EscapedProperly()
    {
        var desc = "Error: \"quotes\" and backslash \\ here";
        var json = CaptureStdout(() => ResultWriter.WriteError("internal", "unhandled", desc));
        var doc = JsonDocument.Parse(json);
        Assert.Equal(desc, doc.RootElement.GetProperty("description").GetString());
    }

    [Fact]
    public void SuccessEnvelope_StringArrayField()
    {
        var payload = new Dictionary<string, object?> { ["resources"] = new[] { "GPIB0::5::INSTR" } };
        var json = CaptureStdout(() => ResultWriter.WriteSuccess(payload));
        var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement.GetProperty("resources").EnumerateArray().ToList();
        Assert.Single(arr);
        Assert.Equal("GPIB0::5::INSTR", arr[0].GetString());
    }

    [Fact]
    public void ErrorEnvelope_ContextFieldsAppended()
    {
        var ctx = new Dictionary<string, object?> { ["address"] = "GPIB0::5::INSTR", ["command"] = "*IDN?" };
        var json = CaptureStdout(() => ResultWriter.WriteError("visa", "viWrite", "desc", ctx, -1073807339));
        var doc = JsonDocument.Parse(json);
        Assert.Equal("GPIB0::5::INSTR", doc.RootElement.GetProperty("address").GetString());
        Assert.Equal("*IDN?", doc.RootElement.GetProperty("command").GetString());
    }

    [Fact]
    public void Numbers_SerializedAsJsonNumbers_NotStrings()
    {
        var payload = new Dictionary<string, object?> { ["count"] = 42, ["bytes"] = 1024u };
        var json = CaptureStdout(() => ResultWriter.WriteSuccess(payload));
        var doc = JsonDocument.Parse(json);
        Assert.Equal(42, doc.RootElement.GetProperty("count").GetInt32());
        Assert.Equal(1024u, doc.RootElement.GetProperty("bytes").GetUInt32());
    }

    [Fact]
    public void Booleans_SerializedAsJsonBooleans_NotStrings()
    {
        var payload = new Dictionary<string, object?> { ["flag"] = true };
        var json = CaptureStdout(() => ResultWriter.WriteSuccess(payload));
        var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.True, doc.RootElement.GetProperty("flag").ValueKind);
    }
}

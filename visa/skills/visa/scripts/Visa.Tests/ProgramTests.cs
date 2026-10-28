using System.Text.Json;
using Visa.Cli;
using Visa.Commands;
using Xunit;

namespace Visa.Tests;

public class ProgramTests
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

    private static Dictionary<string, Func<ICommandHandler>> HandlersWithOverride(
        string subcommand, Func<ICommandHandler> factory)
    {
        var handlers = Program.CreateHandlers();
        handlers[subcommand] = factory;
        return handlers;
    }

    // --- Top-level help ---

    [Fact]
    public void TopLevelHelp_NoArgs_ReturnsOkWithAllSubcommands()
    {
        var exit = 0;
        var json = CaptureStdout(() => { exit = Program.RunMain([], Program.CreateHandlers()); });
        Assert.Equal(0, exit);
        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(doc.RootElement.TryGetProperty("help", out _));
        var subs = doc.RootElement.GetProperty("subcommands")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(19, subs.Count);
        foreach (var name in new[]
        {
            "list", "info", "query", "write", "read", "script",
            "clear", "trigger", "serial-poll",
            "ifc", "ren", "atn", "gpib-command",
            "get-attr", "set-attr",
            "usb-control-in", "usb-control-out",
            "read-block", "write-block",
        })
        {
            Assert.Contains(name, subs);
        }
    }

    [Fact]
    public void TopLevelHelp_HelpFlag_ReturnsOkWithAllSubcommands()
    {
        var exit = 0;
        var json = CaptureStdout(() => { exit = Program.RunMain(["--help"], Program.CreateHandlers()); });
        Assert.Equal(0, exit);
        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        var subs = doc.RootElement.GetProperty("subcommands")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(19, subs.Count);
    }

    // --- Unknown subcommand ---

    [Fact]
    public void UnknownSubcommand_Returns2WithDispatchError()
    {
        var exit = 0;
        var json = CaptureStdout(() => { exit = Program.RunMain(["bogus"], Program.CreateHandlers()); });
        Assert.Equal(2, exit);
        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("dispatch", doc.RootElement.GetProperty("operation").GetString());
        Assert.Contains("bogus", doc.RootElement.GetProperty("description").GetString()!);
    }

    // --- Per-subcommand --help ---

    [Fact]
    public void SubcommandHelp_HelpFlagOnKnownSubcommand_ReturnsSubcommandHelpEnvelope()
    {
        var exit = 0;
        var json = CaptureStdout(() => { exit = Program.RunMain(["list", "--help"], Program.CreateHandlers()); });
        Assert.Equal(0, exit);
        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("list", doc.RootElement.GetProperty("subcommand").GetString());
        Assert.True(doc.RootElement.TryGetProperty("help", out _));
    }

    // --- Exception mapping ---

    [Fact]
    public void UsageException_Returns2WithUsageErrorEnvelope()
    {
        var handlers = HandlersWithOverride(
            "list", () => new ThrowingHandler(new UsageException("bad arg", "argparse")));
        var exit = 0;
        var json = CaptureStdout(() => { exit = Program.RunMain(["list"], handlers); });
        Assert.Equal(2, exit);
        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("argparse", doc.RootElement.GetProperty("operation").GetString());
    }

    [Fact]
    public void UnhandledException_Returns4WithInternalErrorEnvelope()
    {
        var handlers = HandlersWithOverride(
            "list", () => new ThrowingHandler(new InvalidOperationException("boom")));
        var exit = 0;
        var json = CaptureStdout(() => { exit = Program.RunMain(["list"], handlers); });
        Assert.Equal(4, exit);
        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("internal", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("unhandled", doc.RootElement.GetProperty("operation").GetString());
    }

    [Fact]
    public void NotImplementedHandler_ReturnsUsageError_Exit2()
    {
        var handlers = HandlersWithOverride("query", () => new NotImplementedHandler("query"));
        var exit = 0;
        var json = CaptureStdout(() => { exit = Program.RunMain(["query"], handlers); });
        Assert.Equal(2, exit);
        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("dispatch", doc.RootElement.GetProperty("operation").GetString());
    }

    // --- T20: list and info ---

    [Fact]
    public void Info_MissingAddress_Returns2WithUsageEnvelope()
    {
        var exit = 0;
        var json = CaptureStdout(() => { exit = Program.RunMain(["info"], Program.CreateHandlers()); });
        Assert.Equal(2, exit);
        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void List_SerialFlagRejected_Returns2()
    {
        var exit = 0;
        var json = CaptureStdout(() => { exit = Program.RunMain(["list", "--baud", "9600"], Program.CreateHandlers()); });
        Assert.Equal(2, exit);
        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage", doc.RootElement.GetProperty("error").GetString());
    }

    // --- Phase 5 review fix: script viOpen failure must include address in error envelope ---

    [Fact]
    public void Script_OpenFailure_IncludesAddressInErrorEnvelope()
    {
        var exit = 0;
        var json = CaptureStdout(() => { exit = Program.RunMain(["script", "INVALID_ADDR", "--cmd", "*IDN?"], Program.CreateHandlers()); });
        if (exit == 3) return; // VISA driver not available on this machine — skip
        Assert.Equal(1, exit);
        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("visa", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("INVALID_ADDR", doc.RootElement.GetProperty("address").GetString());
    }

    [Fact]
    public void DllNotFoundException_Returns3WithVisaLibraryMissing()
    {
        var handlers = HandlersWithOverride(
            "list", () => new ThrowingHandler(new DllNotFoundException("visa32.dll not found")));
        var exit = 0;
        var json = CaptureStdout(() => { exit = Program.RunMain(["list"], handlers); });
        Assert.Equal(3, exit);
        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("visa_library_missing", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("loadlibrary", doc.RootElement.GetProperty("operation").GetString());
    }

    // --- T25: get-attr and set-attr ---

    [Fact]
    public void GetAttr_MissingAttrFlag_Returns2()
    {
        var exit = 0;
        var json = CaptureStdout(() => { exit = Program.RunMain(["get-attr", "ADDR"], Program.CreateHandlers()); });
        Assert.Equal(2, exit);
        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void GetAttr_UnknownAttrName_Returns2()
    {
        var exit = 0;
        var json = CaptureStdout(() => { exit = Program.RunMain(["get-attr", "ADDR", "--attr", "NOT_AN_ATTR"], Program.CreateHandlers()); });
        Assert.Equal(2, exit);
        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void GetAttr_TypeMismatch_Returns2()
    {
        var exit = 0;
        var json = CaptureStdout(() => { exit = Program.RunMain(["get-attr", "ADDR", "--attr", "VI_ATTR_TMO_VALUE", "--type", "u8"], Program.CreateHandlers()); });
        Assert.Equal(2, exit);
        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void SetAttr_MissingAttrFlag_Returns2()
    {
        var exit = 0;
        var json = CaptureStdout(() => { exit = Program.RunMain(["set-attr", "ADDR", "--value", "9600"], Program.CreateHandlers()); });
        Assert.Equal(2, exit);
        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void SetAttr_MissingValueFlag_Returns2()
    {
        var exit = 0;
        var json = CaptureStdout(() => { exit = Program.RunMain(["set-attr", "ADDR", "--attr", "VI_ATTR_TMO_VALUE"], Program.CreateHandlers()); });
        Assert.Equal(2, exit);
        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void SetAttr_StringTypeRejected_Returns2()
    {
        var exit = 0;
        var json = CaptureStdout(() => { exit = Program.RunMain(["set-attr", "ADDR", "--attr", "0x3FFF0021", "--value", "5", "--type", "string"], Program.CreateHandlers()); });
        Assert.Equal(2, exit);
        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void SetAttr_BadValueForType_Returns2()
    {
        var exit = 0;
        var json = CaptureStdout(() => { exit = Program.RunMain(["set-attr", "ADDR", "--attr", "VI_ATTR_TMO_VALUE", "--value", "notanumber"], Program.CreateHandlers()); });
        Assert.Equal(2, exit);
        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage", doc.RootElement.GetProperty("error").GetString());
    }

    // --- T26: usb-control-in and usb-control-out ---

    [Fact]
    public void UsbControlIn_MissingRequiredFlag_Returns2()
    {
        var exit = 0;
        var json = CaptureStdout(() => { exit = Program.RunMain(["usb-control-in", "ADDR", "--request-type", "0", "--request", "0", "--value", "0", "--index", "0"], Program.CreateHandlers()); });
        Assert.Equal(2, exit);
        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void UsbControlIn_OutOfRangeLength_Returns2()
    {
        var exit = 0;
        var json = CaptureStdout(() => { exit = Program.RunMain(["usb-control-in", "ADDR", "--request-type", "0", "--request", "0", "--value", "0", "--index", "0", "--length", "65536"], Program.CreateHandlers()); });
        Assert.Equal(2, exit);
        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void UsbControlOut_InvalidHex_Returns2()
    {
        var exit = 0;
        var json = CaptureStdout(() => { exit = Program.RunMain(["usb-control-out", "ADDR", "--request-type", "0", "--request", "0", "--value", "0", "--index", "0", "--hex", "ZZZ"], Program.CreateHandlers()); });
        Assert.Equal(2, exit);
        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void UsbControlOut_SerialFlagRejected_Returns2()
    {
        var exit = 0;
        var json = CaptureStdout(() => { exit = Program.RunMain(["usb-control-out", "ADDR", "--request-type", "0", "--request", "0", "--value", "0", "--index", "0", "--baud", "9600"], Program.CreateHandlers()); });
        Assert.Equal(2, exit);
        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void UsbControlIn_OutOfRangeValue_Returns2()
    {
        var exit = 0;
        var json = CaptureStdout(() => { exit = Program.RunMain(["usb-control-in", "ADDR", "--request-type", "0", "--request", "0", "--value", "70000", "--index", "0", "--length", "2"], Program.CreateHandlers()); });
        Assert.Equal(2, exit);
        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage", doc.RootElement.GetProperty("error").GetString());
    }

    private class ThrowingHandler : ICommandHandler
    {
        private readonly Exception _ex;
        public ThrowingHandler(Exception ex) => _ex = ex;
        public string HelpText => "test";
        public int Run(string[] args) => throw _ex;
    }
}

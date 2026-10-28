using PartsSearch;
using Xunit;

namespace PartsSearch.Tests;

public class CliArgsTests
{
    private static SearchRequest Search(params string[] args) =>
        Assert.IsType<ParsedSearch>(Program.ParseCli(args)).Request;

    private static AlternativesRequest Alternatives(params string[] args) =>
        Assert.IsType<ParsedAlternatives>(Program.ParseCli(args)).Request;

    private static DetailsRequest Details(params string[] args) =>
        Assert.IsType<ParsedDetails>(Program.ParseCli(args)).Request;

    private static CheckRequest Check(params string[] args) =>
        Assert.IsType<ParsedCheck>(Program.ParseCli(args)).Request;

    private static KeyCommandArgs Key(params string[] args) =>
        Assert.IsType<ParsedKey>(Program.ParseCli(args)).Args;

    private static ErrorObject UsageError(params string[] args) =>
        Assert.IsType<ParsedUsageError>(Program.ParseCli(args)).Error;

    private static void AssertUsage(ErrorObject error, CommandKind command)
    {
        Assert.False(error.Ok);
        Assert.Equal(command, error.Command);
        Assert.Equal(ErrorKind.Usage, error.Error);
        Assert.False(string.IsNullOrWhiteSpace(error.Description));
    }

    [Fact]
    public void Search_Defaults_AllVendorsQty1Limit10Md()
    {
        var request = Search("search", "--q", "TPIC6B595DWR");
        Assert.Equal("TPIC6B595DWR", request.Query);
        Assert.Null(request.Vendors);
        Assert.Equal(1, request.Qty);
        Assert.Equal(10, request.Limit);
        Assert.Equal(OutputFormat.Md, request.Format);
        Assert.False(request.NoCache);
    }

    [Fact]
    public void Search_ExplicitFlags_OverrideDefaults()
    {
        var request = Search("search", "--q", "led", "--vendor", "digikey,mouser", "--qty", "25",
            "--limit", "3", "--format", "table", "--no-cache");
        Assert.Equal("led", request.Query);
        Assert.Equal(new[] { VendorId.Digikey, VendorId.Mouser }, request.Vendors);
        Assert.Equal(25, request.Qty);
        Assert.Equal(3, request.Limit);
        Assert.Equal(OutputFormat.Table, request.Format);
        Assert.True(request.NoCache);
    }

    [Fact]
    public void Search_VendorAllExplicit_ParsesToNullSubset() =>
        Assert.Null(Search("search", "--q", "x", "--vendor", "all").Vendors);

    [Fact]
    public void Search_SingleVendor_ParsesToOneElementSubset()
    {
        var request = Search("search", "--q", "x", "--vendor", "newark");
        Assert.Equal(new[] { VendorId.Newark }, request.Vendors);
    }

    [Fact]
    public void Alternatives_AcceptsSearchFlags_WithDefaults()
    {
        var request = Alternatives("alternatives", "--q", "TPIC6B595DWR");
        Assert.Equal("TPIC6B595DWR", request.Query);
        Assert.Null(request.Vendors);
        Assert.Equal(1, request.Qty);
        Assert.Equal(10, request.Limit);
        Assert.Equal(OutputFormat.Md, request.Format);
        Assert.False(request.NoCache);
    }

    [Fact]
    public void Details_SingleVendor_ParsesWithoutLimit()
    {
        var request = Details("details", "--vendor", "digikey", "--q", "296-1275-1-ND", "--qty", "10");
        Assert.Equal("296-1275-1-ND", request.Query);
        Assert.Equal(VendorId.Digikey, request.Vendor);
        Assert.Equal(10, request.Qty);
        Assert.Equal(OutputFormat.Md, request.Format);
        Assert.False(request.NoCache);
    }

    [Fact]
    public void Check_PreservesMpnFlagOrder()
    {
        var request = Check("check", "--mpn", "TPIC6B595DWR", "--mpn", "HM1705-ND", "--mpn", "83F8917", "--qty", "10");
        Assert.Equal(new[] { "TPIC6B595DWR", "HM1705-ND", "83F8917" }, request.Mpns);
        Assert.Equal(10, request.Qty);
        Assert.Null(request.Vendors);
        Assert.Equal(OutputFormat.Md, request.Format);
    }

    [Fact]
    public void Check_AcceptsCommaVendorList()
    {
        var request = Check("check", "--mpn", "A", "--vendor", "digikey,mouser");
        Assert.Equal(new[] { VendorId.Digikey, VendorId.Mouser }, request.Vendors);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100000)]
    public void Search_QtyDomain_BoundariesAccepted(int qty) =>
        Assert.Equal(qty, Search("search", "--q", "x", "--qty", qty.ToString()).Qty);

    [Theory]
    [InlineData(0)]
    [InlineData(100001)]
    [InlineData(-3)]
    public void Search_QtyOutOfRange_IsUsageError(int qty) =>
        AssertUsage(UsageError("search", "--q", "x", "--qty", qty.ToString()), CommandKind.Search);

    [Fact]
    public void Search_QtyNonInteger_IsUsageError() =>
        AssertUsage(UsageError("search", "--q", "x", "--qty", "abc"), CommandKind.Search);

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    public void Search_LimitDomain_BoundariesAccepted(int limit) =>
        Assert.Equal(limit, Search("search", "--q", "x", "--limit", limit.ToString()).Limit);

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void Search_LimitOutOfRange_IsUsageError(int limit) =>
        AssertUsage(UsageError("search", "--q", "x", "--limit", limit.ToString()), CommandKind.Search);

    [Theory]
    [InlineData("json", OutputFormat.Json)]
    [InlineData("table", OutputFormat.Table)]
    [InlineData("md", OutputFormat.Md)]
    public void Search_FormatDomain_TokensAccepted(string token, OutputFormat format) =>
        Assert.Equal(format, Search("search", "--q", "x", "--format", token).Format);

    [Fact]
    public void Search_FormatUnknownToken_IsUsageError() =>
        AssertUsage(UsageError("search", "--q", "x", "--format", "xml"), CommandKind.Search);

    [Fact]
    public void Search_VendorUnknownToken_IsUsageError()
    {
        var error = UsageError("search", "--q", "x", "--vendor", "digi");
        AssertUsage(error, CommandKind.Search);
    }

    [Fact]
    public void Search_VendorAllInsideCommaList_IsUsageError() =>
        AssertUsage(UsageError("search", "--q", "x", "--vendor", "all,digikey"), CommandKind.Search);

    [Fact]
    public void Search_UnknownFlag_IsUsageError() =>
        AssertUsage(UsageError("search", "--q", "x", "--bogus", "y"), CommandKind.Search);

    [Fact]
    public void Search_MisspelledFlag_IsUsageError() =>
        AssertUsage(UsageError("search", "--q", "x", "--quanity", "5"), CommandKind.Search);

    [Fact]
    public void Search_MpnIsCheckOnly_IsUsageError() =>
        AssertUsage(UsageError("search", "--q", "x", "--mpn", "A"), CommandKind.Search);

    [Fact]
    public void Search_FlagEqualsForm_IsUsageError() =>
        AssertUsage(UsageError("search", "--q=TPIC"), CommandKind.Search);

    [Fact]
    public void Search_ShortFlag_IsUsageError() =>
        AssertUsage(UsageError("search", "-q", "TPIC"), CommandKind.Search);

    [Fact]
    public void Search_MissingQuery_IsUsageError() =>
        AssertUsage(UsageError("search"), CommandKind.Search);

    [Fact]
    public void Alternatives_MissingQuery_IsUsageError() =>
        AssertUsage(UsageError("alternatives"), CommandKind.Alternatives);

    [Fact]
    public void Search_EmptyQuery_IsUsageError() =>
        AssertUsage(UsageError("search", "--q", ""), CommandKind.Search);

    [Fact]
    public void Search_MissingFlagValueAtEnd_IsUsageError() =>
        AssertUsage(UsageError("search", "--q"), CommandKind.Search);

    [Fact]
    public void Search_FlagValueCannotBeAnotherFlag_IsUsageError() =>
        AssertUsage(UsageError("search", "--vendor", "--q", "x"), CommandKind.Search);

    [Fact]
    public void Search_DuplicateValueFlag_IsUsageError() =>
        AssertUsage(UsageError("search", "--q", "a", "--q", "b"), CommandKind.Search);

    [Fact]
    public void Search_UnexpectedPositional_IsUsageError() =>
        AssertUsage(UsageError("search", "--q", "x", "extra"), CommandKind.Search);

    [Fact]
    public void Search_NoCacheSwitchTakesNoValue_IsUsageError() =>
        AssertUsage(UsageError("search", "--q", "x", "--no-cache", "true"), CommandKind.Search);

    [Fact]
    public void Details_RejectsLimit_IsUsageError() =>
        AssertUsage(UsageError("details", "--vendor", "digikey", "--q", "x", "--limit", "5"), CommandKind.Details);

    [Fact]
    public void Check_RejectsLimit_IsUsageError() =>
        AssertUsage(UsageError("check", "--mpn", "A", "--limit", "5"), CommandKind.Check);

    [Fact]
    public void Details_MissingVendor_IsUsageError() =>
        AssertUsage(UsageError("details", "--q", "x"), CommandKind.Details);

    [Fact]
    public void Details_AllVendor_IsUsageError() =>
        AssertUsage(UsageError("details", "--q", "x", "--vendor", "all"), CommandKind.Details);

    [Fact]
    public void Details_CommaListVendor_IsUsageError() =>
        AssertUsage(UsageError("details", "--q", "x", "--vendor", "digikey,mouser"), CommandKind.Details);

    [Fact]
    public void Check_MissingMpn_IsUsageError() =>
        AssertUsage(UsageError("check"), CommandKind.Check);

    [Fact]
    public void Check_EmptyMpnValue_IsUsageError() =>
        AssertUsage(UsageError("check", "--mpn", ""), CommandKind.Check);

    [Fact]
    public void Key_Set_ParsesVendorToken()
    {
        var args = Key("key", "set", "mouser");
        Assert.Equal("set", args.Sub);
        Assert.Equal(VendorId.Mouser, args.Vendor);
    }

    [Fact]
    public void Key_Clear_ParsesVendorToken()
    {
        var args = Key("key", "clear", "newark");
        Assert.Equal("clear", args.Sub);
        Assert.Equal(VendorId.Newark, args.Vendor);
    }

    [Fact]
    public void Key_List_TakesNoVendor()
    {
        var args = Key("key", "list");
        Assert.Equal("list", args.Sub);
        Assert.Null(args.Vendor);
    }

    [Fact]
    public void Key_Test_TakesNoVendor()
    {
        var args = Key("key", "test");
        Assert.Equal("test", args.Sub);
        Assert.Null(args.Vendor);
    }

    [Fact]
    public void Key_UnknownSubcommand_IsUsageError() =>
        AssertUsage(UsageError("key", "frobnicate"), CommandKind.Key);

    [Fact]
    public void Key_UnknownVendorToken_IsUsageError() =>
        AssertUsage(UsageError("key", "set", "lcsc"), CommandKind.Key);

    [Fact]
    public void Key_MissingVendor_IsUsageError()
    {
        AssertUsage(UsageError("key", "set"), CommandKind.Key);
        AssertUsage(UsageError("key", "clear"), CommandKind.Key);
    }

    [Fact]
    public void Key_VendorOnListOrTest_IsUsageError()
    {
        AssertUsage(UsageError("key", "list", "digikey"), CommandKind.Key);
        AssertUsage(UsageError("key", "test", "digikey"), CommandKind.Key);
    }

    [Fact]
    public void Key_ExtraArgument_IsUsageError() =>
        AssertUsage(UsageError("key", "set", "mouser", "extra"), CommandKind.Key);

    [Fact]
    public void Key_MissingSubcommand_IsUsageError() =>
        AssertUsage(UsageError("key"), CommandKind.Key);

    [Fact]
    public void UsageError_IsClassifiedForExit2()
    {
        var error = UsageError("search", "--bogus", "x");
        Assert.False(error.Ok);
        Assert.Equal(CommandKind.Search, error.Command);
        Assert.Equal(ErrorKind.Usage, error.Error);
        Assert.False(string.IsNullOrWhiteSpace(error.Description));
    }

    [Fact]
    public void UsageError_KeyFamily_CarriesCommandKey() =>
        AssertUsage(UsageError("key", "frobnicate"), CommandKind.Key);

    [Fact]
    public void UnknownCommand_IsUsageError() =>
        AssertUsage(UsageError("frobnicate", "--q", "x"), CommandKind.Unknown);

    [Fact]
    public void EmptyArgv_IsUsageError() =>
        AssertUsage(UsageError(), CommandKind.Unknown);
}

using PartsSearch;
using Xunit;

namespace PartsSearch.Tests;

public class MatchClassificationTests
{
    [Fact]
    public void QueryEqualingMpn_CaseInsensitive_IsExact()
    {
        Assert.Equal(MatchKind.Exact, SearchService.ClassifyMatch("tpic6b595dwr", "TPIC6B595DWR", "296-1275-1-ND"));
        Assert.Equal(MatchKind.Exact, SearchService.ClassifyMatch("TPIC6B595DWR", "tpic6b595dwr", "296-1275-1-ND"));
    }

    [Fact]
    public void QueryEqualingDistributorCode_CaseInsensitive_IsExact()
    {
        Assert.Equal(MatchKind.Exact, SearchService.ClassifyMatch("296-1275-1-nd", "TPIC6B595DWR", "296-1275-1-ND"));
        Assert.Equal(MatchKind.Exact, SearchService.ClassifyMatch("hm1705-nd", "HM1705-ND", "HM1705-ND"));
    }

    [Fact]
    public void QueryMatchingNeither_IsKeyword()
    {
        Assert.Equal(MatchKind.Keyword, SearchService.ClassifyMatch("tpic6b595", "TPIC6B595DWR", "296-1275-1-ND"));
        Assert.Equal(MatchKind.Keyword, SearchService.ClassifyMatch("reverse mount green 1206 LED", "150060GS75000", "150060GS75000"));
    }

    [Fact]
    public void QueryEqualingOnlyVendorPartNumber_StaysExact_EvenWhenMpnDiffers()
    {
        Assert.Equal(MatchKind.Exact, SearchService.ClassifyMatch("83F8917", "1402H", "83F8917"));
    }
}

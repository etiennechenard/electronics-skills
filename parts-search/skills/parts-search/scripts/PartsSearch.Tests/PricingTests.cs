using PartsSearch;
using Xunit;

namespace PartsSearch.Tests;

public class PricingTests
{
    private static readonly IReadOnlyList<PriceBreak> ExampleBreaks =
    [
        new PriceBreak(1, 3.85m),
        new PriceBreak(10, 2.60m),
    ];

    [Fact]
    public void BelowFirstBreak_BuysAtFirstBreakPrice()
    {
        Assert.Equal(3.85m, SearchService.UnitPriceAt(ExampleBreaks, 5));
    }

    [Fact]
    public void BetweenBreaks_TakesLargestBreakAtOrUnderQty()
    {
        Assert.Equal(3.85m, SearchService.UnitPriceAt(ExampleBreaks, 8));
        Assert.Equal(3.85m, SearchService.UnitPriceAt(ExampleBreaks, 9));
    }

    [Fact]
    public void BreakThreshold_Inclusive()
    {
        Assert.Equal(2.60m, SearchService.UnitPriceAt(ExampleBreaks, 10));
    }

    [Fact]
    public void AboveLargestBreak_TakesLargestBreakPrice()
    {
        Assert.Equal(2.60m, SearchService.UnitPriceAt(ExampleBreaks, 100));
        Assert.Equal(2.60m, SearchService.UnitPriceAt(ExampleBreaks, 100000));
    }

    [Fact]
    public void UnpricedPart_HasNoUnitPrice()
    {
        Assert.Null(SearchService.UnitPriceAt([], 8));
    }

    [Fact]
    public void WorkedExample_Qty8_FindsCheaperTen()
    {
        var unitPrice = SearchService.UnitPriceAt(ExampleBreaks, 8);
        Assert.Equal(3.85m, unitPrice);

        var hint = SearchService.CheaperAtHint(ExampleBreaks, 8, unitPrice);
        Assert.NotNull(hint);
        Assert.Equal(10, hint.Qty);
        Assert.Equal(2.60m, hint.UnitPrice);
        Assert.Equal(26.00m, hint.Total);
        Assert.Equal(4.80m, hint.Savings);
    }

    [Fact]
    public void SmallestQualifyingBreak_Wins_NotJustAnyCheaperOne()
    {
        IReadOnlyList<PriceBreak> breaks =
        [
            new PriceBreak(1, 3.85m),
            new PriceBreak(10, 2.60m),
            new PriceBreak(20, 1.00m),
        ];

        var hint = SearchService.CheaperAtHint(breaks, 6, 3.85m);

        Assert.NotNull(hint);
        Assert.Equal(20, hint.Qty);
        Assert.Equal(1.00m, hint.UnitPrice);
        Assert.Equal(20.00m, hint.Total);
        Assert.Equal(3.10m, hint.Savings);
    }

    [Fact]
    public void LargerBreakNotCheaper_InTotal_IsNotAHint()
    {
        IReadOnlyList<PriceBreak> breaks =
        [
            new PriceBreak(1, 3.85m),
            new PriceBreak(10, 2.60m),
            new PriceBreak(25, 2.50m),
        ];

        var hint = SearchService.CheaperAtHint(breaks, 8, 3.85m);

        Assert.NotNull(hint);
        Assert.Equal(10, hint.Qty);
    }

    [Fact]
    public void TieOnTotal_GivesNull()
    {
        IReadOnlyList<PriceBreak> breaks =
        [
            new PriceBreak(1, 3.85m),
            new PriceBreak(10, 3.08m),
        ];

        Assert.Null(SearchService.CheaperAtHint(breaks, 8, 3.85m));
    }

    [Fact]
    public void NoCheaperBreak_GivesNull()
    {
        IReadOnlyList<PriceBreak> breaks =
        [
            new PriceBreak(1, 3.85m),
            new PriceBreak(10, 4.00m),
        ];

        Assert.Null(SearchService.CheaperAtHint(breaks, 8, 3.85m));
    }

    [Fact]
    public void UnpricedPart_GivesNullHint()
    {
        Assert.Null(SearchService.CheaperAtHint([], 8, null));
    }

    [Fact]
    public void BelowFirstBreak_TotalGrowsWithQty_NoHintWhenLargerBracketCostsMore()
    {
        Assert.Null(SearchService.CheaperAtHint(ExampleBreaks, 5, 3.85m));
    }
}

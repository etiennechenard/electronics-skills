using PartsSearch;
using Xunit;

namespace PartsSearch.Tests;

public class AlternativesTests
{
    private static PartResult Part(
        string mpn,
        string vendorCode,
        string manufacturer = "Texas Instruments",
        int? stock = 1000,
        string? category = null,
        string description = "",
        MatchKind match = MatchKind.Keyword) => new()
    {
        Mpn = mpn,
        VendorPartNumber = vendorCode,
        Description = description,
        Manufacturer = manufacturer,
        Stock = stock,
        Category = category,
        Url = $"https://example.com/{vendorCode}",
        Match = match,
    };

    private static PartResult Pivot() => Part(
        "TPIC6B595DWR",
        "296-1351-1-ND",
        manufacturer: "Texas Instruments",
        stock: 36075,
        category: "Shift Registers",
        description: "IC SR OPEN DRAIN 8BIT 20-SOIC",
        match: MatchKind.Exact);

    // ---- pivot selection ----

    [Fact]
    public void Pivot_FirstExactMatchWins()
    {
        var keyword = Part("OTHER1", "O1");
        var exact = Part("PIVOTMPN", "P1", match: MatchKind.Exact);

        var pivot = AlternativesFinder.SelectPivot([keyword, exact]);

        Assert.Equal("PIVOTMPN", pivot!.Mpn);
    }

    [Fact]
    public void Pivot_NoExact_TopKeywordResult()
    {
        var first = Part("FIRST1", "F1");
        var second = Part("SECOND1", "S1");

        var pivot = AlternativesFinder.SelectPivot([first, second]);

        Assert.Equal("FIRST1", pivot!.Mpn);
    }

    [Fact]
    public void Pivot_EmptyStage1_Null()
    {
        Assert.Null(AlternativesFinder.SelectPivot([]));
    }

    // ---- stage-2 query build ----

    [Fact]
    public void Stage2Query_Manufacturer_PlusFirstSixDescriptionTokens_Lowercased()
    {
        var pivot = Part(
            "TPIC6B595DWR",
            "296-1351-1-ND",
            manufacturer: "Texas Instruments",
            description: "IC SR OPEN DRAIN 8BIT 20-SOIC MORE TOKENS FOLLOW HERE");

        var query = AlternativesFinder.BuildStage2Query(pivot);

        Assert.Equal("texas instruments ic sr open drain 8bit 20-soic", query);
    }

    [Fact]
    public void Stage2Query_ExcludesTheMpnToken_AndUsesAllTokensWhenFewerThanSix()
    {
        var pivot = Part(
            "TPIC6B595DWR",
            "296-1351-1-ND",
            manufacturer: "Texas Instruments",
            description: "TPIC6B595DWR SHIFT REGISTER");

        var query = AlternativesFinder.BuildStage2Query(pivot);

        Assert.Equal("texas instruments shift register", query);
    }

    // ---- relation rules ----

    [Fact]
    public void ExactRelation_CarriesPinnedReason()
    {
        var classified = AlternativesFinder.Classify([Pivot()], []);

        var part = Assert.Single(classified);
        Assert.Equal(RelationKind.Exact, part.Relation);
        Assert.Equal("mpn equals query", part.Reason);
    }

    [Fact]
    public void Variant_SameManufacturerSameBaseMpn_WithPinnedReason()
    {
        var variant = Part("TPIC6B595DWRG4", "G4-ND", description: "IC SR OPEN DRAIN 8BIT");

        var classified = AlternativesFinder.Classify([Pivot()], [variant]);

        var match = Assert.Single(classified, part => part.Mpn == "TPIC6B595DWRG4");
        Assert.Equal(RelationKind.Variant, match.Relation);
        Assert.Equal("same base MPN as TPIC6B595DWR", match.Reason);
    }

    [Fact]
    public void Variant_CaseInsensitiveBaseMpnMatch()
    {
        var variant = Part("tpic6b595dwrg4", "G4-ND");

        var classified = AlternativesFinder.Classify([Pivot()], [variant]);

        Assert.Contains(classified, part => part.Relation == RelationKind.Variant);
    }

    [Fact]
    public void Variant_StripUpToThreeTrailingAlphanumericsFromEitherSide()
    {
        // Three stripped from the candidate lands on the pivot exactly.
        var stripped = Part("TPIC6B595DWRG4Q", "G4Q-ND");

        var classified = AlternativesFinder.Classify([Pivot()], [stripped]);

        Assert.Contains(classified, part => part.Mpn == "TPIC6B595DWRG4Q" && part.Relation == RelationKind.Variant);
    }

    [Fact]
    public void FamilyMemberWithDifferentNumber_IsNotVariant()
    {
        var other = Part("TPIC6C595DWR", "C595-ND", category: "Shift Registers", description: "IC SR OPEN DRAIN 8BIT");

        var classified = AlternativesFinder.Classify([Pivot()], [other]);

        Assert.DoesNotContain(classified, part => part.Mpn == "TPIC6C595DWR" && part.Relation == RelationKind.Variant);
    }

    [Fact]
    public void Alternative_SameCategoryDifferentBaseMpn_PinnedReason()
    {
        var sameCategory = Part("CD4021BE", "CD4021-ND", category: "Shift Registers", description: "CMOS SHIFT REGISTER");

        var classified = AlternativesFinder.Classify([Pivot()], [sameCategory]);

        var match = Assert.Single(classified, part => part.Mpn == "CD4021BE");
        Assert.Equal(RelationKind.Alternative, match.Relation);
        Assert.Equal("same category (Shift Registers), same manufacturer", match.Reason);
    }

    [Fact]
    public void Alternative_CrossManufacturer_OmitsTheManufacturerClause()
    {
        var cross = Part("CD4021BE", "CD4021-ND", manufacturer: "RCA", category: "Shift Registers", description: "CMOS SHIFT REGISTER");

        var classified = AlternativesFinder.Classify([Pivot()], [cross]);

        var match = Assert.Single(classified, part => part.Mpn == "CD4021BE");
        Assert.Equal("same category (Shift Registers)", match.Reason);
    }

    [Fact]
    public void OverlapFallback_AtOrAboveThreshold_IsAlternative_WhenEitherCategoryIsNull()
    {
        // Jaccard = |{aaa,bbb}| / |{aaa,bbb,ccc,ddd,eee}| = 2/5 = 0.4 exactly; the candidate's
        // category is null so the overlap stands in for the category rule.
        var candidate = Part(
            "NOCAT1",
            "NC1-ND",
            category: null,
            description: "aaa bbb");

        var classified = AlternativesFinder.Classify(
            [Pivot() with { Description = "aaa bbb ccc ddd eee" }],
            [candidate]);

        var match = Assert.Single(classified, part => part.Mpn == "NOCAT1");
        Assert.Equal(RelationKind.Alternative, match.Relation);
        Assert.Equal("description overlap with TPIC6B595DWR", match.Reason);
    }

    [Fact]
    public void OverlapFallback_BelowThreshold_IsDropped()
    {
        // Jaccard = 2/6 < 0.4.
        var candidate = Part("NOCAT1", "NC1-ND", category: null, description: "aaa bbb");

        var classified = AlternativesFinder.Classify(
            [Pivot() with { Description = "aaa bbb ccc ddd eee fff" }],
            [candidate]);

        Assert.Single(classified); // the pivot only
    }

    [Fact]
    public void OverlapFallback_RequiresDifferentBaseMpn()
    {
        // Same base MPN and null categories: the variant rule needs the same manufacturer,
        // and the alternative rule is barred by the shared base MPN — no rule matches.
        var differentManufacturerVariant = Part("TPIC6B595DWRG4", "G4-ND", manufacturer: "RCA", category: null);

        var classified = AlternativesFinder.Classify([Pivot()], [differentManufacturerVariant]);

        Assert.Single(classified); // the pivot only
    }

    [Fact]
    public void UnclassifiedParts_AreDropped_FromBothStages()
    {
        var stage1Unrelated = Part("UNRELATED1", "U1", category: "Capacitors", description: "capacitor thing");
        var stage2Unrelated = Part("UNRELATED2", "U2", category: "Connectors", description: "connector thing");

        var classified = AlternativesFinder.Classify([Pivot(), stage1Unrelated], [stage2Unrelated]);

        Assert.Single(classified); // the pivot only
    }

    // ---- dedup + ranking ----

    [Fact]
    public void CrossStageDedup_ByVendorPartNumber_Stage1Wins()
    {
        var stage1Occurrence = Part("TPIC6B595DWRG4", "G4-ND", stock: 500, description: "IC SR OPEN DRAIN 8BIT");
        var stage2Occurrence = Part("TPIC6B595DWRG4", "G4-ND", stock: 9999, description: "IC SR OPEN DRAIN 8BIT");

        var classified = AlternativesFinder.Classify([Pivot(), stage1Occurrence], [stage2Occurrence]);

        Assert.Equal(2, classified.Count); // pivot + the variant once
        var variant = Assert.Single(classified, part => part.Mpn == "TPIC6B595DWRG4");
        Assert.Equal(500, variant.Stock); // the stage-1 occurrence's data survived
    }

    [Fact]
    public void Ranking_SameManufacturerFirst_ThenStockDescending_NullsLast()
    {
        var crossManufacturerHighStock = Part("XMANU1", "X1", manufacturer: "RCA", stock: 99999, category: "Shift Registers");
        var sameManufacturerLowStock = Part("LOW1", "L1", stock: 1, category: "Shift Registers");
        var sameManufacturerNullStock = Part("NULL1", "N1", stock: null, category: "Shift Registers");
        var sameManufacturerMidStock = Part("MID1", "M1", stock: 500, category: "Shift Registers");

        var classified = AlternativesFinder.Classify(
            [Pivot(), crossManufacturerHighStock, sameManufacturerLowStock, sameManufacturerNullStock, sameManufacturerMidStock],
            []);

        // Stock descending inside the same-manufacturer group puts the pivot (36075) first.
        Assert.Equal(
            new[] { "TPIC6B595DWR", "MID1", "LOW1", "NULL1", "XMANU1" },
            classified.Select(part => part.Mpn).ToArray());
    }

    [Fact]
    public void Ranking_PreservesRelevanceOrder_WithinEqualKeys()
    {
        var sameManufacturerA = Part("AAA1", "A1", stock: 100, category: "Shift Registers");
        var sameManufacturerB = Part("BBB1", "B1", stock: 100, category: "Shift Registers");

        var classified = AlternativesFinder.Classify([Pivot(), sameManufacturerA, sameManufacturerB], []);

        Assert.Equal(
            new[] { "TPIC6B595DWR", "AAA1", "BBB1" },
            classified.Select(part => part.Mpn).ToArray());
    }

    [Fact]
    public void Classify_EmptyStage1_ReturnsEmpty_RegardlessOfStage2()
    {
        var stage2 = Part("TPIC6B595DWRG4", "G4-ND");

        Assert.Empty(AlternativesFinder.Classify([], [stage2]));
    }

    [Fact]
    public void SameBaseMpn_EqualityAndPlainPrefix_CountAsBaseMatch()
    {
        Assert.True(AlternativesFinder.SameBaseMpn("TPIC6B595DWR", "tpic6b595dwr"));
        Assert.True(AlternativesFinder.SameBaseMpn("TPIC6B595", "TPIC6B595DWR"));
        Assert.False(AlternativesFinder.SameBaseMpn("", "TPIC6B595DWR"));
        Assert.False(AlternativesFinder.SameBaseMpn("TPIC6B595", "TPIC6C595"));
    }

    [Fact]
    public void SameBaseMpn_StripsTrailingAlphanumericsFromBothSides()
    {
        // Neither string is a plain prefix of the other; both strip to TPIC6B595DWR.
        Assert.True(AlternativesFinder.SameBaseMpn("TPIC6B595DWRG4", "TPIC6B595DWRXYZ"));
        Assert.True(AlternativesFinder.SameBaseMpn("TPIC6B595DWRAB", "TPIC6B595DWRXYZ"));
    }

    [Fact]
    public void SameBaseMpn_StripLimitIsThreeTrailingAlphanumerics()
    {
        // Three strips leave ...DWRA and ...DWRW (diverging at position 12, so no prefix
        // relation); four strips would make both exactly TPIC6B595DWR — the limit is 3.
        Assert.False(AlternativesFinder.SameBaseMpn("TPIC6B595DWRABCD", "TPIC6B595DWRWXYZ"));
    }
}

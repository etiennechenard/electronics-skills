using System.Text.Json;
using PartsSearch;
using Xunit;

namespace PartsSearch.Tests;

public class RenderTests
{
    internal static DateTimeOffset FixtureAsOf => new(2026, 10, 23, 19, 40, 12, TimeSpan.Zero);

    private static Envelope BuildCanonicalEnvelope() => new()
    {
        Command = CommandKind.Search,
        Query = "TPIC6B595DWR",
        Qty = 8,
        AsOf = new DateTimeOffset(2026, 10, 23, 19, 40, 12, TimeSpan.Zero),
        Vendors =
        [
            new VendorOutcome
            {
                Vendor = VendorId.Digikey,
                Status = VendorStatus.Ok,
                Cached = true,
                Parts =
                [
                    new PartResult
                    {
                        Mpn = "TPIC6B595DWR",
                        VendorPartNumber = "296-1275-1-ND",
                        Description = "Power Driver Open Drain 8:1 20-Pin SOIC",
                        Manufacturer = "Texas Instruments",
                        Stock = 12500,
                        UnitPrice = 3.85m,
                        Currency = "CAD",
                        PriceBreaks = [new PriceBreak(1, 3.85m), new PriceBreak(10, 2.60m)],
                        CheaperAt = new CheaperAt(10, 2.60m, 26.00m, 4.80m),
                        Lifecycle = LifecycleStage.Active,
                        Packaging = "Cut Tape (CT)",
                        Category = "Shift Registers",
                        Url = "https://www.digikey.ca/en/products/detail/texas-instruments/TPIC6B595DWR/274257",
                        Match = MatchKind.Exact,
                    },
                ],
            },
            new VendorOutcome { Vendor = VendorId.Mouser, Status = VendorStatus.NoKey },
            new VendorOutcome { Vendor = VendorId.Newark, Status = VendorStatus.Error, Error = "http 503 (1 retry)" },
        ],
    };

    [Fact]
    public void Envelope_SerializesExactSpellings_NumberPrices_AndRoundTrips()
    {
        var json = JsonSerializer.Serialize(BuildCanonicalEnvelope(), WireJson.Options);

        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"command\":\"search\"", json);
        Assert.Contains("\"query\":\"TPIC6B595DWR\"", json);
        Assert.Contains("\"as_of\":\"2026-10-23T19:40:12Z\"", json);
        Assert.Contains("\"vendor_part_number\":\"296-1275-1-ND\"", json);
        Assert.Contains("\"unit_price\":3.85", json);
        Assert.Contains("\"price_breaks\":[{\"qty\":1,\"price\":3.85},{\"qty\":10,\"price\":2.60}]", json);
        Assert.Contains("\"cheaper_at\":{\"qty\":10,\"unit_price\":2.60,\"total\":26.00,\"savings\":4.80}", json);
        Assert.Contains("\"vendor\":\"mouser\",\"status\":\"no_key\",\"parts\":[]", json);
        Assert.Contains("\"vendor\":\"newark\",\"status\":\"error\",\"error\":\"http 503 (1 retry)\",\"parts\":[]", json);

        using var doc = JsonDocument.Parse(json);
        var part = doc.RootElement.GetProperty("vendors")[0].GetProperty("parts")[0];
        Assert.Equal(JsonValueKind.Number, part.GetProperty("stock").ValueKind);
        Assert.Equal(JsonValueKind.Number, part.GetProperty("unit_price").ValueKind);
        Assert.Equal(JsonValueKind.Number, part.GetProperty("price_breaks")[1].GetProperty("price").ValueKind);
        Assert.Equal(JsonValueKind.Number, part.GetProperty("cheaper_at").GetProperty("total").ValueKind);

        var roundTrip = JsonSerializer.Deserialize<Envelope>(json, WireJson.Options);
        Assert.NotNull(roundTrip);
        Assert.Equal(json, JsonSerializer.Serialize(roundTrip, WireJson.Options));
    }

    [Fact]
    public void CheckEnvelope_SerializesLinesShape_AndRoundTrips()
    {
        var envelope = new CheckEnvelope
        {
            Qty = 10,
            AsOf = new DateTimeOffset(2026, 10, 23, 19, 40, 12, TimeSpan.Zero),
            Lines =
            [
                new CheckLine
                {
                    Mpn = "TPIC6B595DWR",
                    Vendors =
                    [
                        new VendorOutcome { Vendor = VendorId.Digikey, Status = VendorStatus.Ok, Cached = true },
                        new VendorOutcome { Vendor = VendorId.Mouser, Status = VendorStatus.Ok, Cached = false },
                    ],
                },
            ],
        };

        var json = JsonSerializer.Serialize(envelope, WireJson.Options);

        Assert.StartsWith("{\"ok\":true,\"command\":\"check\",\"qty\":10,", json);
        Assert.DoesNotContain("\"query\"", json);
        Assert.Contains("\"lines\":[{\"mpn\":\"TPIC6B595DWR\",\"vendors\":[{\"vendor\":\"digikey\",\"status\":\"ok\",\"cached\":true,\"parts\":[]},{\"vendor\":\"mouser\",\"status\":\"ok\",\"cached\":false,\"parts\":[]}]}]", json);

        var roundTrip = JsonSerializer.Deserialize<CheckEnvelope>(json, WireJson.Options);
        Assert.NotNull(roundTrip);
        Assert.Equal(json, JsonSerializer.Serialize(roundTrip, WireJson.Options));
    }

    [Fact]
    public void UnpricedPart_SerializesPinnedNullsExplicitly_AndNonErrorOutcomeOmitsError()
    {
        var envelope = new Envelope
        {
            Command = CommandKind.Search,
            Query = "reverse mount green 1206 LED",
            Qty = 1,
            AsOf = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero),
            Vendors =
            [
                new VendorOutcome
                {
                    Vendor = VendorId.Mouser,
                    Status = VendorStatus.Ok,
                    Cached = false,
                    Parts =
                    [
                        new PartResult
                        {
                            Mpn = "150060GS75000",
                            VendorPartNumber = "150060GS75000",
                            Description = "LED Green 570nm 2V 20mA",
                            Manufacturer = "Wurth Elektronik",
                            Url = "https://www.mouser.com/ProductDetail/Wurth-Elektronik/150060GS75000",
                            Match = MatchKind.Keyword,
                        },
                    ],
                },
            ],
        };

        var json = JsonSerializer.Serialize(envelope, WireJson.Options);

        Assert.Contains("\"stock\":null", json);
        Assert.Contains("\"unit_price\":null", json);
        Assert.Contains("\"currency\":null", json);
        Assert.Contains("\"price_breaks\":[]", json);
        Assert.Contains("\"cheaper_at\":null", json);
        Assert.Contains("\"packaging\":null", json);
        Assert.Contains("\"category\":null", json);
        Assert.Contains("\"cached\":false", json);
        Assert.DoesNotContain("\"error\"", json);
        Assert.DoesNotContain("\"relation\"", json);
        Assert.DoesNotContain("\"reason\"", json);
        Assert.DoesNotContain("\"parameters\"", json);
        Assert.DoesNotContain("\"datasheets\"", json);

        var roundTrip = JsonSerializer.Deserialize<Envelope>(json, WireJson.Options);
        Assert.NotNull(roundTrip);
        Assert.Equal(json, JsonSerializer.Serialize(roundTrip, WireJson.Options));
    }

    [Fact]
    public void ErrorObject_SerializesExactShape()
    {
        var json = JsonSerializer.Serialize(
            new ErrorObject { Command = CommandKind.Search, Error = ErrorKind.Usage, Description = "unknown flag --bogus" },
            WireJson.Options);

        Assert.Equal("{\"ok\":false,\"command\":\"search\",\"error\":\"usage\",\"description\":\"unknown flag --bogus\"}", json);
    }

    [Fact]
    public void ErrorObject_CarriesKeyCommand()
    {
        var json = JsonSerializer.Serialize(
            new ErrorObject { Command = CommandKind.Key, Error = ErrorKind.Usage, Description = "unknown key subcommand" },
            WireJson.Options);

        Assert.Equal("{\"ok\":false,\"command\":\"key\",\"error\":\"usage\",\"description\":\"unknown key subcommand\"}", json);
    }

    // ---- Renderers (md / table / json) ----

    internal static PartResult SearchPart() => new()
    {
        Mpn = "TPIC6B595DWR",
        VendorPartNumber = "296-1275-1-ND",
        Description = "Power Driver Open Drain 8:1 20-Pin SOIC",
        Manufacturer = "Texas Instruments",
        Stock = 12500,
        UnitPrice = 3.85m,
        Currency = "CAD",
        PriceBreaks = [new PriceBreak(1, 3.85m), new PriceBreak(10, 2.60m)],
        CheaperAt = new CheaperAt(10, 2.60m, 26.00m, 4.80m),
        Lifecycle = LifecycleStage.Active,
        Packaging = "Cut Tape (CT)",
        Category = "Shift Registers",
        Url = "https://www.digikey.ca/en/products/detail/texas-instruments/TPIC6B595DWR/274257",
        Match = MatchKind.Exact,
    };

    private static string DigiKeyUrl(string mpn) =>
        $"https://www.digikey.ca/en/products/detail/texas-instruments/{mpn}/274257";

    internal static Envelope SearchFixture() => new()
    {
        Command = CommandKind.Search,
        Query = "TPIC6B595DWR",
        Qty = 8,
        AsOf = FixtureAsOf,
        Vendors =
        [
            new VendorOutcome
            {
                Vendor = VendorId.Digikey,
                Status = VendorStatus.Ok,
                Cached = true,
                Parts = [SearchPart()],
            },
            new VendorOutcome { Vendor = VendorId.Mouser, Status = VendorStatus.NoKey },
            new VendorOutcome { Vendor = VendorId.Newark, Status = VendorStatus.Error, Error = "http 503 (1 retry)" },
        ],
    };

    internal static Envelope AlternativesFixture() => new()
    {
        Command = CommandKind.Alternatives,
        Query = "TPIC6B595DWR",
        Qty = 8,
        AsOf = FixtureAsOf,
        Vendors =
        [
            new VendorOutcome
            {
                Vendor = VendorId.Digikey,
                Status = VendorStatus.Ok,
                Cached = false,
                Parts =
                [
                    SearchPart() with { Relation = RelationKind.Exact, Reason = "mpn equals query" },
                    SearchPart() with
                    {
                        Mpn = "TPIC6B595DWRG4",
                        Url = DigiKeyUrl("TPIC6B595DWRG4"),
                        Match = MatchKind.Keyword,
                        Relation = RelationKind.Variant,
                        Reason = "same base MPN as TPIC6B595DWR",
                        Stock = 4000,
                    },
                    SearchPart() with
                    {
                        Mpn = "TPIC6C595DWR",
                        Url = DigiKeyUrl("TPIC6C595DWR"),
                        Match = MatchKind.Keyword,
                        Relation = RelationKind.Alternative,
                        Reason = "same category (Shift Registers), same manufacturer",
                        Stock = 2300,
                        UnitPrice = 3.95m,
                        // Single break at 1: pins unit 3.95 at qty 8 with no cheaper larger bracket.
                        PriceBreaks = [new PriceBreak(1, 3.95m)],
                        CheaperAt = null,
                    },
                ],
            },
        ],
    };

    internal static Envelope DetailsFixture() => new()
    {
        Command = CommandKind.Details,
        Query = "296-1275-1-ND",
        Qty = 1,
        AsOf = FixtureAsOf,
        Vendors =
        [
            new VendorOutcome
            {
                Vendor = VendorId.Digikey,
                Status = VendorStatus.Ok,
                Cached = true,
                Parts =
                [
                    SearchPart() with
                    {
                        // qty 1: no larger break totals less, so the hint must be null.
                        CheaperAt = null,
                        Parameters =
                        [
                            new PartParameter("Voltage - Supply", "4.5V ~ 5.5V"),
                            new PartParameter("Output Current", "750mA"),
                        ],
                        Datasheets = ["https://www.ti.com/lit/ds/symlink/tpic6b595.pdf"],
                    },
                ],
            },
        ],
    };

    internal static CheckEnvelope CheckFixture() => new()
    {
        Qty = 10,
        AsOf = FixtureAsOf,
        Lines =
        [
            new CheckLine
            {
                Mpn = "TPIC6B595DWR",
                Vendors =
                [
                    new VendorOutcome
                    {
                        Vendor = VendorId.Digikey,
                        Status = VendorStatus.Ok,
                        Cached = true,
                        Parts =
                        [
                            // qty 10: the largest break at or under qty is 10 @ 2.60.
                            SearchPart() with { UnitPrice = 2.60m, CheaperAt = null },
                        ],
                    },
                    new VendorOutcome { Vendor = VendorId.Mouser, Status = VendorStatus.Ok, Cached = false },
                ],
            },
            new CheckLine
            {
                Mpn = "HM1705-ND",
                Vendors =
                [
                    new VendorOutcome { Vendor = VendorId.Newark, Status = VendorStatus.Error, Error = "http 503 (1 retry)" },
                ],
            },
        ],
    };

    private static string LoadGolden(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", fileName);
        return File.ReadAllText(path).Replace("\r\n", "\n");
    }

    [Fact]
    public void Golden_Search_MdAndTable()
    {
        var envelope = SearchFixture();
        Assert.Equal(LoadGolden("render-search.golden.md"), Render.Envelope(envelope, OutputFormat.Md));
        Assert.Equal(LoadGolden("render-search-table.golden.md"), Render.Envelope(envelope, OutputFormat.Table));
    }

    [Fact]
    public void Golden_Alternatives_MdAndTable()
    {
        var envelope = AlternativesFixture();
        Assert.Equal(LoadGolden("render-alternatives.golden.md"), Render.Envelope(envelope, OutputFormat.Md));
        Assert.Equal(LoadGolden("render-alternatives-table.golden.md"), Render.Envelope(envelope, OutputFormat.Table));
    }

    [Fact]
    public void Golden_Details_MdAndTable()
    {
        var envelope = DetailsFixture();
        Assert.Equal(LoadGolden("render-details.golden.md"), Render.Envelope(envelope, OutputFormat.Md));
        Assert.Equal(LoadGolden("render-details-table.golden.md"), Render.Envelope(envelope, OutputFormat.Table));
    }

    [Fact]
    public void Golden_Check_MdAndTable()
    {
        var envelope = CheckFixture();
        Assert.Equal(LoadGolden("render-check.golden.md"), Render.Check(envelope, OutputFormat.Md));
        Assert.Equal(LoadGolden("render-check-table.golden.md"), Render.Check(envelope, OutputFormat.Table));
    }

    [Fact]
    public void SearchMd_NonOkLinesExact_AndTipLineExact()
    {
        var md = Render.Envelope(SearchFixture(), OutputFormat.Md);

        Assert.Contains("- mouser: no key configured (run: parts-search key set mouser)", md);
        Assert.Contains("- newark: error — http 503 (1 retry)", md);
        Assert.Contains("- tip: buy 10 × TPIC6B595DWR instead of 8 — total 26.00 vs 30.80 CAD (save 4.80)", md);
    }

    [Fact]
    public void SearchMd_NoResultsLineExact()
    {
        var envelope = new Envelope
        {
            Command = CommandKind.Search,
            Query = "obscure part",
            Qty = 1,
            AsOf = FixtureAsOf,
            Vendors = [new VendorOutcome { Vendor = VendorId.Digikey, Status = VendorStatus.NoResults, Cached = true }],
        };

        var md = Render.Envelope(envelope, OutputFormat.Md);

        Assert.Contains("- digikey: no results", md);
    }

    [Fact]
    public void Description_TruncatesAt60Chars()
    {
        var longDescription = new string('A', 65);
        var boundaryDescription = new string('B', 60);
        var envelope = new Envelope
        {
            Command = CommandKind.Search,
            Query = "q",
            Qty = 1,
            AsOf = FixtureAsOf,
            Vendors =
            [
                new VendorOutcome
                {
                    Vendor = VendorId.Digikey,
                    Status = VendorStatus.Ok,
                    Parts =
                    [
                        SearchPart() with { Mpn = "LONG1", Description = longDescription },
                        SearchPart() with { Mpn = "EDGE1", Description = boundaryDescription },
                    ],
                },
            ],
        };

        var md = Render.Envelope(envelope, OutputFormat.Md);

        Assert.Contains($"{new string('A', 60)} …", md);
        Assert.DoesNotContain(new string('A', 61), md);
        Assert.Contains(boundaryDescription, md);
        var lines = md.Split('\n');
        Assert.DoesNotContain(lines, line => line.Contains("EDGE1") && line.Contains(" …"));
    }

    [Fact]
    public void Json_RendersCanonicalEnvelope_PartsNeverNull()
    {
        var json = Render.Envelope(SearchFixture(), OutputFormat.Json);

        Assert.Equal(JsonSerializer.Serialize(SearchFixture(), WireJson.Options), json);
        Assert.Contains("\"parts\":[]", json);
        Assert.DoesNotContain("\"parts\":null", json);
    }

    [Fact]
    public void Json_RendersCheckEnvelope()
    {
        var json = Render.Check(CheckFixture(), OutputFormat.Json);

        Assert.Equal(JsonSerializer.Serialize(CheckFixture(), WireJson.Options), json);
        Assert.StartsWith("{\"ok\":true,\"command\":\"check\"", json);
    }

    [Fact]
    public void Error_RendersJson()
    {
        var error = new ErrorObject { Command = CommandKind.Search, Error = ErrorKind.Usage, Description = "unknown flag --bogus" };

        Assert.Equal("{\"ok\":false,\"command\":\"search\",\"error\":\"usage\",\"description\":\"unknown flag --bogus\"}", Render.Error(error));
    }

    // Golden fixtures pin priced parts by hand; this guard keeps every pinned number
    // consistent with the pricing rules at its envelope's quantity.
    [Fact]
    public void FixtureParts_PinnedPricing_MatchesThePricingRules()
    {
        AssertEnvelopePricingConsistent(SearchFixture(), 8);
        AssertEnvelopePricingConsistent(AlternativesFixture(), 8);
        AssertEnvelopePricingConsistent(DetailsFixture(), 1);
        foreach (var line in CheckFixture().Lines)
        foreach (var outcome in line.Vendors)
        foreach (var part in outcome.Parts)
            AssertDerived(part, 10);
    }

    private static void AssertEnvelopePricingConsistent(Envelope envelope, int qty)
    {
        foreach (var outcome in envelope.Vendors)
        foreach (var part in outcome.Parts)
            AssertDerived(part, qty);
    }

    private static void AssertDerived(PartResult part, int qty)
    {
        if (part.UnitPrice is null) return;
        Assert.Equal(SearchService.UnitPriceAt(part.PriceBreaks, qty), part.UnitPrice);
        Assert.Equal(SearchService.CheaperAtHint(part.PriceBreaks, qty, part.UnitPrice), part.CheaperAt);
    }
}

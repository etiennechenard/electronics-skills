using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PartsSearch;

public enum VendorId
{
    Digikey,
    Mouser,
    Newark,
}

public enum OutputFormat
{
    Json,
    Table,
    Md,
}

public enum CommandKind
{
    Search,
    Alternatives,
    Details,
    Check,
    Key,
    // Names the command family in usage errors whose argv names no known command.
    Unknown,
}

public enum MatchKind
{
    Exact,
    Keyword,
}

public enum LifecycleStage
{
    Active,
    Nrnd,
    Eol,
    Obsolete,
    Unknown,
}

public enum RelationKind
{
    Exact,
    Variant,
    Alternative,
}

public enum VendorStatus
{
    Ok,
    NoResults,
    NoKey,
    Error,
}

public enum ErrorKind
{
    Usage,
    Internal,
}

public sealed record SearchRequest(
    string Query,
    // null selects all vendors (the --vendor default); a comma-list narrows the subset.
    IReadOnlyList<VendorId>? Vendors,
    int Qty,
    int Limit,
    OutputFormat Format,
    bool NoCache);

public sealed record AlternativesRequest(
    string Query,
    IReadOnlyList<VendorId>? Vendors,
    int Qty,
    int Limit,
    OutputFormat Format,
    bool NoCache);

public sealed record DetailsRequest(
    string Query,
    VendorId Vendor,
    int Qty,
    OutputFormat Format,
    bool NoCache);

public sealed record CheckRequest(
    IReadOnlyList<string> Mpns,
    IReadOnlyList<VendorId>? Vendors,
    int Qty,
    OutputFormat Format,
    bool NoCache);

public sealed record PriceBreak(int Qty, decimal Price);

public sealed record CheaperAt(int Qty, decimal UnitPrice, decimal Total, decimal Savings);

public sealed record PartParameter(string Name, string Value);

public sealed record PartResult
{
    public required string Mpn { get; init; }
    public required string VendorPartNumber { get; init; }
    public required string Description { get; init; }
    public required string Manufacturer { get; init; }
    public int? Stock { get; init; }
    public decimal? UnitPrice { get; init; }
    public string? Currency { get; init; }
    public IReadOnlyList<PriceBreak> PriceBreaks { get; init; } = [];
    public CheaperAt? CheaperAt { get; init; }
    public LifecycleStage Lifecycle { get; init; } = LifecycleStage.Unknown;
    public string? Packaging { get; init; }
    public string? Category { get; init; }
    public string? Url { get; init; }
    public MatchKind Match { get; init; }
    // Additive per command (alternatives / details): absent on the other commands' parts.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RelationKind? Relation { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<PartParameter>? Parameters { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Datasheets { get; init; }
}

public sealed record VendorOutcome
{
    public required VendorId Vendor { get; init; }
    public required VendorStatus Status { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Cached { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }
    // Set when the parts were found under a narrowed form of the query (keyword
    // narrowing); null when the requested query itself produced the parts.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NarrowedTo { get; init; }
    public IReadOnlyList<PartResult> Parts { get; init; } = [];
}

public sealed record Envelope
{
    public bool Ok { get; init; } = true;
    public required CommandKind Command { get; init; }
    public required string Query { get; init; }
    public required int Qty { get; init; }
    public required DateTimeOffset AsOf { get; init; }
    public IReadOnlyList<VendorOutcome> Vendors { get; init; } = [];
}

public sealed record CheckLine
{
    public required string Mpn { get; init; }
    public IReadOnlyList<VendorOutcome> Vendors { get; init; } = [];
}

public sealed record CheckEnvelope
{
    public bool Ok { get; init; } = true;
    public CommandKind Command { get; init; } = CommandKind.Check;
    public required int Qty { get; init; }
    public required DateTimeOffset AsOf { get; init; }
    public IReadOnlyList<CheckLine> Lines { get; init; } = [];
}

public sealed record ErrorObject
{
    public bool Ok { get; init; } = false;
    public required CommandKind Command { get; init; }
    public required ErrorKind Error { get; init; }
    public required string Description { get; init; }
}

public sealed class UtcIso8601Converter : JsonConverter<DateTimeOffset>
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture));
}

public static class WireJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters =
        {
            new UtcIso8601Converter(),
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower),
        },
    };
}

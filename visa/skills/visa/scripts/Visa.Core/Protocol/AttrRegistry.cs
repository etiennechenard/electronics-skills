namespace Visa.Core.Protocol;

public enum AttrType { U32, U16, U8, Boolean, String }

public static class AttrRegistry
{
    public readonly record struct AttrEntry(uint Id, AttrType Type);

    private static readonly Dictionary<string, AttrEntry> _map = new()
    {
        // Carried-over general attributes (TechDesignCliContract §6 table 1)
        ["VI_ATTR_TMO_VALUE"]    = new(0x3FFF001A, AttrType.U32),
        ["VI_ATTR_TERMCHAR"]     = new(0x3FFF0018, AttrType.U8),
        ["VI_ATTR_TERMCHAR_EN"]  = new(0x3FFF0038, AttrType.Boolean),
        ["VI_ATTR_SEND_END_EN"]  = new(0x3FFF0016, AttrType.Boolean),

        // Serial (ASRL) attributes (TechDesignCliContract §6 table 2)
        ["VI_ATTR_ASRL_BAUD"]       = new(0x3FFF0021, AttrType.U32),
        ["VI_ATTR_ASRL_DATA_BITS"]  = new(0x3FFF0022, AttrType.U16),
        ["VI_ATTR_ASRL_PARITY"]     = new(0x3FFF0023, AttrType.U16),
        ["VI_ATTR_ASRL_STOP_BITS"]  = new(0x3FFF0024, AttrType.U16),
        ["VI_ATTR_ASRL_FLOW_CNTRL"] = new(0x3FFF0025, AttrType.U16),
    };

    /// <summary>
    /// Resolve a token (known symbolic name or raw 0xHHHHHHHH hex id) to (id, type).
    /// typeOverride conflicts with a known name's registry type → ArgumentException.
    /// Raw hex with no typeOverride defaults to U32.
    /// </summary>
    public static AttrEntry Resolve(string token, AttrType? typeOverride = null)
    {
        if (_map.TryGetValue(token, out var entry))
        {
            if (typeOverride.HasValue && typeOverride.Value != entry.Type)
                throw new ArgumentException(
                    $"Type override '{typeOverride.Value}' conflicts with registry type '{entry.Type}' for '{token}'.");
            return entry;
        }

        if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            uint.TryParse(token[2..], System.Globalization.NumberStyles.HexNumber, null, out uint rawId))
        {
            return new AttrEntry(rawId, typeOverride ?? AttrType.U32);
        }

        throw new ArgumentException($"Unknown attribute token '{token}'. Use a known symbolic name or a raw 0xHHHHHHHH hex id.");
    }
}

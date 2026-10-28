using Visa.Cli;
using Visa.Core.Protocol;
using Visa.Core.Session;
using Visa.Json;

namespace Visa.Commands;

public class GetAttrCommand : ICommandHandler
{
    public string HelpText =>
        "get-attr <address> --attr <name|0xHEX> [--type <u32|u16|u8|bool|string>] [--timeout <ms>] [serial flags]\n" +
        "  Read a VISA attribute from a freshly-opened session.\n" +
        "  --attr <a>      Symbolic name (e.g. VI_ATTR_TMO_VALUE) or raw 0xHHHHHHHH id.\n" +
        "  --type <t>      Read type: u32, u16, u8, bool, string. For known names the\n" +
        "                  registry type is used; --type must match or it is a usage error.\n" +
        "  --timeout <ms>  VISA timeout (default: 5000)\n" +
        "  Serial flags: --baud, --data-bits, --parity, --stop-bits, --flow-control";

    public int Run(string[] args)
    {
        var specs = new List<FlagSpec>
        {
            new() { Name = "attr" },
            new() { Name = "type" },
            new() { Name = "timeout" },
        };
        specs.AddRange(SerialFlags.Specs);

        var parser = new ArgParser(1, specs);
        var parsed = parser.Parse(args);

        var address = parsed.Positionals[0];

        if (!parsed.HasFlag("attr"))
            throw new UsageException("get-attr requires --attr.", "argparse");

        var attrToken = parsed.GetFlag("attr");

        AttrType? typeOverride = null;
        if (parsed.HasFlag("type"))
        {
            if (!TryParseAttrType(parsed.GetFlag("type"), out var t))
                throw new UsageException(
                    $"--type: unknown type '{parsed.GetFlag("type")}'. Use u32, u16, u8, bool, or string.", "argparse");
            typeOverride = t;
        }

        AttrRegistry.AttrEntry entry;
        try
        {
            entry = AttrRegistry.Resolve(attrToken, typeOverride);
        }
        catch (ArgumentException ex)
        {
            throw new UsageException(ex.Message, "argparse");
        }

        var timeoutStr = parsed.GetFlagOrDefault("timeout", "5000");
        if (!int.TryParse(timeoutStr, out var timeoutMs) || timeoutMs < 0)
            throw new UsageException($"--timeout: invalid value '{timeoutStr}'.", "argparse");

        var serialConfig = SerialFlags.Extract(parsed);

        try
        {
            using var session = new VisaSession();
            session.Open(address, TimeSpan.FromMilliseconds(timeoutMs), serialConfig);
            var raw = session.GetAttribute(entry.Id, entry.Type);
            var value = NormalizeValue(raw, entry.Type);

            ResultWriter.WriteSuccess(new Dictionary<string, object?>
            {
                ["address"] = address,
                ["attr"]    = attrToken,
                ["attr_id"] = $"0x{entry.Id:X8}",
                ["type"]    = AttrTypeToString(entry.Type),
                ["value"]   = value,
            });
            return 0;
        }
        catch (VisaException ex)
        {
            ResultWriter.WriteError("visa", ex.Operation, ex.Description,
                context: new Dictionary<string, object?> { ["address"] = address, ["attr"] = attrToken },
                statusInt: ex.Status);
            return 1;
        }
    }

    private static bool TryParseAttrType(string s, out AttrType type)
    {
        switch (s)
        {
            case "u32":    type = AttrType.U32;     return true;
            case "u16":    type = AttrType.U16;     return true;
            case "u8":     type = AttrType.U8;      return true;
            case "bool":   type = AttrType.Boolean; return true;
            case "string": type = AttrType.String;  return true;
            default:       type = AttrType.U32;     return false;
        }
    }

    internal static string AttrTypeToString(AttrType type) => type switch
    {
        AttrType.U32     => "u32",
        AttrType.U16     => "u16",
        AttrType.U8      => "u8",
        AttrType.Boolean => "bool",
        AttrType.String  => "string",
        _                => throw new ArgumentException($"Unknown AttrType {type}"),
    };

    private static object NormalizeValue(object raw, AttrType type) => type switch
    {
        AttrType.U32     => (uint)raw,
        AttrType.U16     => (uint)(ushort)raw,
        AttrType.U8      => (uint)(byte)raw,
        AttrType.Boolean => (bool)raw,
        AttrType.String  => (string)raw,
        _                => raw,
    };
}

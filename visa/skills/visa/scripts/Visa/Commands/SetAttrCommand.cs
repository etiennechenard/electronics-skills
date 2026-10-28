using Visa.Cli;
using Visa.Core.Protocol;
using Visa.Core.Session;
using Visa.Json;

namespace Visa.Commands;

public class SetAttrCommand : ICommandHandler
{
    public string HelpText =>
        "set-attr <address> --attr <name|0xHEX> --value <v> [--type <u32|u16|u8|bool>] [--timeout <ms>] [serial flags]\n" +
        "  Write a VISA attribute on a freshly-opened session.\n" +
        "  --attr <a>      Symbolic name or raw 0xHHHHHHHH id.\n" +
        "  --value <v>     Value: decimal or 0x-hex for numeric; true/false or 0/1 for bool.\n" +
        "  --type <t>      Type: u32, u16, u8, bool. String attributes are read-only.\n" +
        "  --timeout <ms>  VISA timeout (default: 5000)\n" +
        "  Serial flags: --baud, --data-bits, --parity, --stop-bits, --flow-control";

    public int Run(string[] args)
    {
        var specs = new List<FlagSpec>
        {
            new() { Name = "attr" },
            new() { Name = "value" },
            new() { Name = "type" },
            new() { Name = "timeout" },
        };
        specs.AddRange(SerialFlags.Specs);

        var parser = new ArgParser(1, specs);
        var parsed = parser.Parse(args);

        var address = parsed.Positionals[0];

        if (!parsed.HasFlag("attr"))
            throw new UsageException("set-attr requires --attr.", "argparse");
        if (!parsed.HasFlag("value"))
            throw new UsageException("set-attr requires --value.", "argparse");

        var attrToken = parsed.GetFlag("attr");
        var valueStr  = parsed.GetFlag("value");

        AttrType? typeOverride = null;
        if (parsed.HasFlag("type"))
        {
            var typeStr = parsed.GetFlag("type");
            if (typeStr == "string")
                throw new UsageException(
                    "set-attr: string attributes are read-only; --type string is not accepted.", "argparse");
            if (!TryParseAttrType(typeStr, out var t))
                throw new UsageException(
                    $"--type: unknown type '{typeStr}'. Use u32, u16, u8, or bool.", "argparse");
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

        if (entry.Type == AttrType.String)
            throw new UsageException(
                $"set-attr: '{attrToken}' is a string attribute (read-only in v1).", "argparse");

        var timeoutStr = parsed.GetFlagOrDefault("timeout", "5000");
        if (!int.TryParse(timeoutStr, out var timeoutMs) || timeoutMs < 0)
            throw new UsageException($"--timeout: invalid value '{timeoutStr}'.", "argparse");

        var serialConfig = SerialFlags.Extract(parsed);

        uint rawValue;
        object echoValue;
        switch (entry.Type)
        {
            case AttrType.U32:
                if (!TryParseUInt32(valueStr, out var u32))
                    throw new UsageException($"--value: cannot parse '{valueStr}' as u32.", "argparse");
                rawValue  = u32;
                echoValue = u32;
                break;
            case AttrType.U16:
                if (!TryParseUInt16(valueStr, out var u16))
                    throw new UsageException($"--value: cannot parse '{valueStr}' as u16.", "argparse");
                rawValue  = u16;
                echoValue = (uint)u16;
                break;
            case AttrType.U8:
                if (!TryParseUInt8(valueStr, out var u8))
                    throw new UsageException($"--value: cannot parse '{valueStr}' as u8.", "argparse");
                rawValue  = u8;
                echoValue = (uint)u8;
                break;
            case AttrType.Boolean:
                if (!TryParseBool(valueStr, out var boolVal))
                    throw new UsageException(
                        $"--value: cannot parse '{valueStr}' as bool. Use true/false or 0/1.", "argparse");
                rawValue  = boolVal ? 1u : 0u;
                echoValue = boolVal;
                break;
            default:
                throw new UsageException($"set-attr: unexpected type {entry.Type}.", "argparse");
        }

        try
        {
            using var session = new VisaSession();
            session.Open(address, TimeSpan.FromMilliseconds(timeoutMs), serialConfig);
            session.SetAttribute(entry.Id, entry.Type, rawValue);

            ResultWriter.WriteSuccess(new Dictionary<string, object?>
            {
                ["address"] = address,
                ["attr"]    = attrToken,
                ["attr_id"] = $"0x{entry.Id:X8}",
                ["type"]    = GetAttrCommand.AttrTypeToString(entry.Type),
                ["value"]   = echoValue,
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
            case "u32":  type = AttrType.U32;     return true;
            case "u16":  type = AttrType.U16;     return true;
            case "u8":   type = AttrType.U8;      return true;
            case "bool": type = AttrType.Boolean; return true;
            default:     type = AttrType.U32;     return false;
        }
    }

    private static bool TryParseUInt32(string s, out uint value)
    {
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(s[2..], System.Globalization.NumberStyles.HexNumber, null, out value);
        return uint.TryParse(s, out value);
    }

    private static bool TryParseUInt16(string s, out ushort value)
    {
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return ushort.TryParse(s[2..], System.Globalization.NumberStyles.HexNumber, null, out value);
        return ushort.TryParse(s, out value);
    }

    private static bool TryParseUInt8(string s, out byte value)
    {
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return byte.TryParse(s[2..], System.Globalization.NumberStyles.HexNumber, null, out value);
        return byte.TryParse(s, out value);
    }

    private static bool TryParseBool(string s, out bool value)
    {
        switch (s)
        {
            case "true":  value = true;  return true;
            case "false": value = false; return true;
            case "1":     value = true;  return true;
            case "0":     value = false; return true;
            default:      value = false; return false;
        }
    }
}

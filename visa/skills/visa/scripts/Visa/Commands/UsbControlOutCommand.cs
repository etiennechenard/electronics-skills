using Visa.Cli;
using Visa.Core.Session;
using Visa.Json;

namespace Visa.Commands;

public class UsbControlOutCommand : ICommandHandler
{
    public string HelpText =>
        "usb-control-out <address> --request-type <n> --request <n> --value <n> --index <n> [--hex <bytes>] [--timeout <ms>]\n" +
        "  Low-level USB-TMC control-pipe write (viUsbControlOut). For USB resources only.\n" +
        "  --request-type <n>  bmRequestType (signed 16-bit; decimal or 0x-hex).\n" +
        "  --request <n>       bRequest (signed 16-bit).\n" +
        "  --value <n>         wValue (unsigned 16-bit, 0-65535).\n" +
        "  --index <n>         wIndex (unsigned 16-bit).\n" +
        "  --hex <bytes>       Optional data payload (even-digit hex, upper/lower, spaces allowed).\n" +
        "                      If omitted, a zero-length control-out is sent.\n" +
        "  --timeout <ms>      VISA timeout (default: 5000)";

    public int Run(string[] args)
    {
        var specs = new List<FlagSpec>(UsbControlFlags.SpecsWithoutLength)
        {
            new() { Name = "hex" },
            new() { Name = "timeout" },
        };

        var parser = new ArgParser(1, specs);
        var parsed = parser.Parse(args);

        var address = parsed.Positionals[0];
        var reqType = UsbControlFlags.GetRequestType(parsed);
        var request = UsbControlFlags.GetRequest(parsed);
        var wValue  = UsbControlFlags.GetValue(parsed);
        var wIndex  = UsbControlFlags.GetIndex(parsed);

        byte[] payload;
        string hexStr;
        if (parsed.HasFlag("hex"))
        {
            hexStr  = parsed.GetFlag("hex");
            payload = HexBytes.Parse(hexStr);
        }
        else
        {
            payload = [];
            hexStr  = "";
        }

        var normalizedHex = HexBytes.Format(payload);

        var timeoutStr = parsed.GetFlagOrDefault("timeout", "5000");
        if (!int.TryParse(timeoutStr, out var timeoutMs) || timeoutMs < 0)
            throw new UsageException($"--timeout: invalid value '{timeoutStr}'.", "argparse");

        try
        {
            using var session = new VisaSession();
            session.Open(address, TimeSpan.FromMilliseconds(timeoutMs));
            session.UsbControlOut(reqType, request, wValue, wIndex, payload);

            ResultWriter.WriteSuccess(new Dictionary<string, object?>
            {
                ["address"]    = address,
                ["bytes_sent"] = payload.Length,
                ["hex"]        = normalizedHex,
            });
            return 0;
        }
        catch (VisaException ex)
        {
            ResultWriter.WriteError("visa", ex.Operation, ex.Description,
                context: new Dictionary<string, object?> { ["address"] = address, ["hex"] = hexStr },
                statusInt: ex.Status);
            return 1;
        }
    }
}

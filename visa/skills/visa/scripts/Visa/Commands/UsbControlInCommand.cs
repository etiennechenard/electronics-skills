using Visa.Cli;
using Visa.Core.Session;
using Visa.Json;

namespace Visa.Commands;

public class UsbControlInCommand : ICommandHandler
{
    public string HelpText =>
        "usb-control-in <address> --request-type <n> --request <n> --value <n> --index <n> --length <n> [--timeout <ms>]\n" +
        "  Low-level USB-TMC control-pipe read (viUsbControlIn). For USB resources only.\n" +
        "  --request-type <n>  bmRequestType (signed 16-bit; decimal or 0x-hex).\n" +
        "  --request <n>       bRequest (signed 16-bit).\n" +
        "  --value <n>         wValue (unsigned 16-bit, 0-65535).\n" +
        "  --index <n>         wIndex (unsigned 16-bit).\n" +
        "  --length <n>        wLength — number of bytes to read back (0-65535).\n" +
        "  --timeout <ms>      VISA timeout (default: 5000)";

    public int Run(string[] args)
    {
        var specs = new List<FlagSpec>(UsbControlFlags.Specs)
        {
            new() { Name = "timeout" },
        };

        var parser = new ArgParser(1, specs);
        var parsed = parser.Parse(args);

        var address    = parsed.Positionals[0];
        var reqType    = UsbControlFlags.GetRequestType(parsed);
        var request    = UsbControlFlags.GetRequest(parsed);
        var wValue     = UsbControlFlags.GetValue(parsed);
        var wIndex     = UsbControlFlags.GetIndex(parsed);
        var length     = UsbControlFlags.GetLength(parsed);

        var timeoutStr = parsed.GetFlagOrDefault("timeout", "5000");
        if (!int.TryParse(timeoutStr, out var timeoutMs) || timeoutMs < 0)
            throw new UsageException($"--timeout: invalid value '{timeoutStr}'.", "argparse");

        try
        {
            using var session = new VisaSession();
            session.Open(address, TimeSpan.FromMilliseconds(timeoutMs));
            var (data, retCnt) = session.UsbControlIn(reqType, request, wValue, wIndex, length);
            var hex = HexBytes.Format(data[..retCnt]);

            ResultWriter.WriteSuccess(new Dictionary<string, object?>
            {
                ["address"]    = address,
                ["bytes_read"] = (int)retCnt,
                ["hex"]        = hex,
            });
            return 0;
        }
        catch (VisaException ex)
        {
            ResultWriter.WriteError("visa", ex.Operation, ex.Description,
                context: new Dictionary<string, object?> { ["address"] = address },
                statusInt: ex.Status);
            return 1;
        }
    }
}

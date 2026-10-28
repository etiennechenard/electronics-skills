using Visa.Cli;
using Visa.Core.Protocol;
using Visa.Core.Session;
using Visa.Json;

namespace Visa.Commands;

public class ReadBlockCommand : ICommandHandler
{
    public string HelpText =>
        "read-block <address> <command> --out-file <path> [--raw] [--timeout <ms>] [serial flags]\n" +
        "  Send a query and read a binary IEEE 488.2 block to a file.\n" +
        "  <address>         VISA resource string.\n" +
        "  <command>         Query command sent before the read (e.g. CURV?); \\n appended if absent.\n" +
        "  --out-file <path> Destination file for payload bytes. Created/truncated. Relative to cwd.\n" +
        "  --raw             Skip 488.2 parsing; write raw response bytes to --out-file.\n" +
        "  --timeout <ms>    VISA timeout (default: 5000)\n" +
        "  Serial flags: --baud, --data-bits, --parity, --stop-bits, --flow-control\n" +
        "  Note: --term-char is not accepted on block subcommands.";

    public int Run(string[] args)
    {
        var specs = new List<FlagSpec>
        {
            new() { Name = "out-file", Required = true },
            new() { Name = "raw", Kind = FlagKind.Switch },
            new() { Name = "timeout" },
        };
        specs.AddRange(SerialFlags.Specs);

        var parser = new ArgParser(2, specs);
        var parsed = parser.Parse(args);

        var address  = parsed.Positionals[0];
        var command  = parsed.Positionals[1];
        var outFile  = parsed.GetFlag("out-file");
        var raw      = parsed.GetSwitch("raw");
        var serialConfig = SerialFlags.Extract(parsed);

        var timeoutStr = parsed.GetFlagOrDefault("timeout", "5000");
        if (!int.TryParse(timeoutStr, out var timeoutMs) || timeoutMs < 0)
            throw new UsageException($"--timeout: invalid value '{timeoutStr}'.", "argparse");

        byte[] rawBytes;
        try
        {
            using var session = new VisaSession();
            session.Open(address, TimeSpan.FromMilliseconds(timeoutMs), serialConfig);
            session.Write(command);
            rawBytes = session.ReadRaw(out _);
        }
        catch (VisaException ex)
        {
            ResultWriter.WriteError("visa", ex.Operation, ex.Description,
                context: new Dictionary<string, object?>
                {
                    ["address"]  = address,
                    ["command"]  = command,
                    ["out_file"] = outFile,
                },
                statusInt: ex.Status);
            return 1;
        }

        if (raw)
        {
            File.WriteAllBytes(outFile, rawBytes);
            ResultWriter.WriteSuccess(new Dictionary<string, object?>
            {
                ["address"]         = address,
                ["command"]         = command,
                ["out_file"]        = outFile,
                ["format"]          = "raw",
                ["declared_length"] = (object?)null,
                ["bytes_written"]   = rawBytes.Length,
            });
            return 0;
        }

        BlockHeader.ParseResult result;
        try
        {
            result = BlockHeader.Parse(rawBytes);
        }
        catch (FormatException ex)
        {
            ResultWriter.WriteError("usage", "parse_block", ex.Message,
                context: new Dictionary<string, object?>
                {
                    ["address"]  = address,
                    ["command"]  = command,
                    ["out_file"] = outFile,
                });
            return 2;
        }

        byte[] payload;
        string format;
        int? declaredLength;

        if (result.IsIndefinite)
        {
            int payloadEnd = rawBytes.Length;
            if (payloadEnd > result.PayloadOffset &&
                (rawBytes[payloadEnd - 1] == '\n' || rawBytes[payloadEnd - 1] == '\r'))
                payloadEnd--;
            payload = rawBytes[result.PayloadOffset..payloadEnd];
            format = "indefinite";
            declaredLength = null;
        }
        else
        {
            payload = rawBytes[result.PayloadOffset..(result.PayloadOffset + result.DeclaredLength!.Value)];
            format = "definite";
            declaredLength = result.DeclaredLength;
        }

        File.WriteAllBytes(outFile, payload);
        ResultWriter.WriteSuccess(new Dictionary<string, object?>
        {
            ["address"]         = address,
            ["command"]         = command,
            ["out_file"]        = outFile,
            ["format"]          = format,
            ["declared_length"] = (object?)declaredLength,
            ["bytes_written"]   = payload.Length,
        });
        return 0;
    }
}

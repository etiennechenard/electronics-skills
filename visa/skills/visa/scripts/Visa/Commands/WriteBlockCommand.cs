using System.Text;
using Visa.Cli;
using Visa.Core.Protocol;
using Visa.Core.Session;
using Visa.Json;

namespace Visa.Commands;

public class WriteBlockCommand : ICommandHandler
{
    public string HelpText =>
        "write-block <address> --in-file <path> [<command>] [--raw] [--timeout <ms>] [serial flags]\n" +
        "  Write a binary IEEE 488.2 block from a file.\n" +
        "  <address>          VISA resource string.\n" +
        "  --in-file <path>   Source file of payload bytes. Must exist. Relative to cwd.\n" +
        "  <command>          Optional ASCII prefix sent before the block (e.g. \"TRACE:DATA \"); no auto-\\n.\n" +
        "  --raw              Write file bytes verbatim (no 488.2 header, no trailing \\n).\n" +
        "  --timeout <ms>     VISA timeout (default: 5000)\n" +
        "  Serial flags: --baud, --data-bits, --parity, --stop-bits, --flow-control\n" +
        "  Note: --term-char is not accepted on block subcommands.";

    public int Run(string[] args)
    {
        var specs = new List<FlagSpec>
        {
            new() { Name = "in-file", Required = true },
            new() { Name = "raw", Kind = FlagKind.Switch },
            new() { Name = "timeout" },
        };
        specs.AddRange(SerialFlags.Specs);

        var parser = new ArgParser(1, 2, specs);
        var parsed = parser.Parse(args);

        var address = parsed.Positionals[0];
        var command = parsed.Positionals.Count > 1 ? parsed.Positionals[1] : null;
        var inFile  = parsed.GetFlag("in-file");
        var raw     = parsed.GetSwitch("raw");
        var serialConfig = SerialFlags.Extract(parsed);

        var timeoutStr = parsed.GetFlagOrDefault("timeout", "5000");
        if (!int.TryParse(timeoutStr, out var timeoutMs) || timeoutMs < 0)
            throw new UsageException($"--timeout: invalid value '{timeoutStr}'.", "argparse");

        byte[] payload;
        try
        {
            payload = File.ReadAllBytes(inFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UsageException($"--in-file: cannot read '{inFile}': {ex.Message}", "argparse");
        }

        var parts = new List<byte[]>();
        if (command != null)
            parts.Add(Encoding.ASCII.GetBytes(command));

        string format;
        if (raw)
        {
            parts.Add(payload);
            format = "raw";
        }
        else
        {
            parts.Add(BlockHeader.Format(payload.Length));
            parts.Add(payload);
            parts.Add([(byte)'\n']);
            format = "definite";
        }

        int totalLen = parts.Sum(p => p.Length);
        var buffer = new byte[totalLen];
        int bufOffset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(buffer, bufOffset);
            bufOffset += part.Length;
        }

        uint bytesWritten;
        try
        {
            using var session = new VisaSession();
            session.Open(address, TimeSpan.FromMilliseconds(timeoutMs), serialConfig);
            bytesWritten = session.WriteRaw(buffer);
        }
        catch (VisaException ex)
        {
            ResultWriter.WriteError("visa", ex.Operation, ex.Description,
                context: new Dictionary<string, object?>
                {
                    ["address"] = address,
                    ["in_file"] = inFile,
                },
                statusInt: ex.Status);
            return 1;
        }

        ResultWriter.WriteSuccess(new Dictionary<string, object?>
        {
            ["address"]       = address,
            ["in_file"]       = inFile,
            ["format"]        = format,
            ["payload_bytes"] = payload.Length,
            ["bytes_written"] = (int)bytesWritten,
        });
        return 0;
    }
}

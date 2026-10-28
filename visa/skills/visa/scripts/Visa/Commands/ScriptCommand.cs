using Visa.Cli;
using Visa.Core.Session;
using Visa.Json;

namespace Visa.Commands;

public class ScriptCommand : ICommandHandler
{
    public string HelpText =>
        "script <address> (--cmd <c>... | --file <path>) [--timeout <ms>] [--term-char <val>] [--term-char-en <0|1>] [--no-eoi] [--stop-on-error] [serial flags]\n" +
        "  Open a session and run a sequence of write/query commands.\n" +
        "  --cmd <c>           Repeatable. One SCPI command per flag.\n" +
        "  --file <path>       UTF-8 file, one command per line. Blank / # lines skipped.\n" +
        "  --timeout <ms>      VISA timeout per operation (default: 5000)\n" +
        "  --term-char <val>   Read termination character (default: NL); supplying it enables matching\n" +
        "  --term-char-en <b>  Match the read termination char: 1 on, 0 off. Default is interface-aware\n" +
        "                      (off for GPIB/USB/VXI/TCPIP-INSTR where EOI/END terminates; on for\n" +
        "                      ASRL and TCPIP SOCKET which have no end indicator).\n" +
        "  --no-eoi            Suppress EOI on every write (write-side; does not affect read termination)\n" +
        "  --stop-on-error     Abort on first VISA failure instead of capturing it inline\n" +
        "  Serial flags: --baud, --data-bits, --parity, --stop-bits, --flow-control";

    public int Run(string[] args)
    {
        var specs = new List<FlagSpec>
        {
            new() { Name = "cmd", Kind = FlagKind.Repeatable },
            new() { Name = "file" },
            new() { Name = "timeout" },
            new() { Name = "term-char" },
            new() { Name = "term-char-en" },
            new() { Name = "no-eoi", Kind = FlagKind.Switch },
            new() { Name = "stop-on-error", Kind = FlagKind.Switch },
        };
        specs.AddRange(SerialFlags.Specs);

        var parser = new ArgParser(1, specs);
        var parsed = parser.Parse(args);

        var address = parsed.Positionals[0];
        var cmds = parsed.GetRepeatable("cmd");
        var hasFile = parsed.HasFlag("file");
        var hasCmds = cmds.Count > 0;

        if (!hasCmds && !hasFile)
            throw new UsageException("script requires at least one of --cmd or --file.", "validate");
        if (hasCmds && hasFile)
            throw new UsageException("script accepts --cmd or --file, not both.", "validate");

        List<string> commands;
        if (hasFile)
        {
            var filePath = parsed.GetFlag("file");
            try
            {
                commands = File.ReadAllLines(filePath)
                    .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#'))
                    .ToList();
            }
            catch (Exception ex)
            {
                throw new UsageException($"script --file {filePath}: {ex.Message}", "validate");
            }
        }
        else
        {
            foreach (var c in cmds)
            {
                if (string.IsNullOrEmpty(c))
                    throw new UsageException("script --cmd value cannot be empty.", "validate");
            }
            commands = cmds.ToList();
        }

        var timeoutStr = parsed.GetFlagOrDefault("timeout", "5000");
        if (!int.TryParse(timeoutStr, out var timeoutMs) || timeoutMs < 0)
            throw new UsageException($"--timeout: invalid value '{timeoutStr}'.", "argparse");

        var stopOnError = parsed.GetSwitch("stop-on-error");
        var serialConfig = SerialFlags.Extract(parsed);

        try
        {
            using var session = new VisaSession();
            session.Open(address, TimeSpan.FromMilliseconds(timeoutMs), serialConfig);

            if (parsed.HasFlag("term-char"))
                session.SetTermChar(TermChar.Parse(parsed.GetFlag("term-char")));
            // An explicit --term-char-en wins; otherwise supplying --term-char signals intent to
            // terminate on that char, so enable matching (which the interface default may leave off).
            if (parsed.HasFlag("term-char-en"))
                session.SetTermCharEnabled(TermChar.ParseEnable(parsed.GetFlag("term-char-en")));
            else if (parsed.HasFlag("term-char"))
                session.SetTermCharEnabled(true);
            if (parsed.GetSwitch("no-eoi"))
                session.SetSendEndEnabled(false);

            var results = new List<IDictionary<string, object?>>();

            foreach (var cmd in commands)
            {
                var kind = ScriptClassification.Classify(cmd);
                var kindStr = kind == ScriptClassification.Kind.Query ? "query" : "write";

                try
                {
                    var bytesWritten = session.Write(cmd);
                    var row = new Dictionary<string, object?>
                    {
                        ["command"]       = cmd,
                        ["kind"]          = kindStr,
                        ["bytes_written"] = (int)bytesWritten,
                    };

                    if (kind == ScriptClassification.Kind.Query)
                    {
                        var response = session.ReadLine(out var bytesRead);
                        row["response"]   = response;
                        row["bytes_read"] = (int)bytesRead;
                    }

                    results.Add(row);
                }
                catch (VisaException ex)
                {
                    var errRow = new Dictionary<string, object?>
                    {
                        ["command"] = cmd,
                        ["kind"]    = kindStr,
                        ["error"]   = new Dictionary<string, object?>
                        {
                            ["error"]       = "visa",
                            ["operation"]   = ex.Operation,
                            ["status"]      = $"0x{(uint)ex.Status:X8}",
                            ["status_int"]  = ex.Status,
                            ["description"] = ex.Description,
                        },
                    };
                    results.Add(errRow);

                    if (stopOnError)
                    {
                        ResultWriter.WriteError("visa", ex.Operation, ex.Description,
                            context: new Dictionary<string, object?>
                            {
                                ["address"] = address,
                                ["command"] = cmd,
                                ["results"] = (IEnumerable<IDictionary<string, object?>>)results,
                            },
                            statusInt: ex.Status);
                        return 1;
                    }
                }
            }

            ResultWriter.WriteSuccess(new Dictionary<string, object?>
            {
                ["address"] = address,
                ["results"] = (IEnumerable<IDictionary<string, object?>>)results,
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

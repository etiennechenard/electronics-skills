using Visa.Cli;
using Visa.Core.Session;
using Visa.Json;

namespace Visa.Commands;

public class QueryCommand : ICommandHandler
{
    public string HelpText =>
        "query <address> <command> [--timeout <ms>] [--term-char <val>] [--term-char-en <0|1>] [--no-eoi] [serial flags]\n" +
        "  Write a command and read the response.\n" +
        "  --timeout <ms>      VISA timeout in milliseconds (default: 5000)\n" +
        "  --term-char <val>   Read termination character: NL (10), CR (13), or 0-255 (default: NL).\n" +
        "                      Supplying this also enables termchar matching (see --term-char-en).\n" +
        "  --term-char-en <b>  Match the read termination char: 1 on, 0 off. Default is interface-aware\n" +
        "                      (off for GPIB/USB/VXI/TCPIP-INSTR where EOI/END terminates; on for\n" +
        "                      ASRL and TCPIP SOCKET which have no end indicator).\n" +
        "  --no-eoi            Suppress EOI on the write (write-side; does not affect read termination)\n" +
        "  Serial flags: --baud, --data-bits, --parity, --stop-bits, --flow-control";

    public int Run(string[] args)
    {
        var specs = new List<FlagSpec>
        {
            new() { Name = "timeout" },
            new() { Name = "term-char" },
            new() { Name = "term-char-en" },
            new() { Name = "no-eoi", Kind = FlagKind.Switch },
        };
        specs.AddRange(SerialFlags.Specs);

        var parser = new ArgParser(2, specs);
        var parsed = parser.Parse(args);

        var address = parsed.Positionals[0];
        var command = parsed.Positionals[1];

        var timeoutStr = parsed.GetFlagOrDefault("timeout", "5000");
        if (!int.TryParse(timeoutStr, out var timeoutMs) || timeoutMs < 0)
            throw new UsageException($"--timeout: invalid value '{timeoutStr}'.", "argparse");

        var termCharByte = parsed.HasFlag("term-char")
            ? TermChar.Parse(parsed.GetFlag("term-char"))
            : (byte)10;

        var noEoi = parsed.GetSwitch("no-eoi");
        var serialConfig = SerialFlags.Extract(parsed);

        try
        {
            using var session = new VisaSession();
            session.Open(address, TimeSpan.FromMilliseconds(timeoutMs), serialConfig);
            if (termCharByte != 10)
                session.SetTermChar(termCharByte);
            // An explicit --term-char-en wins; otherwise supplying --term-char signals intent to
            // terminate on that char, so enable matching (which the interface default may leave off).
            if (parsed.HasFlag("term-char-en"))
                session.SetTermCharEnabled(TermChar.ParseEnable(parsed.GetFlag("term-char-en")));
            else if (parsed.HasFlag("term-char"))
                session.SetTermCharEnabled(true);
            if (noEoi)
                session.SetSendEndEnabled(false);
            var bytesWritten = session.Write(command);
            var response = session.ReadLine(out var bytesRead);
            ResultWriter.WriteSuccess(new Dictionary<string, object?>
            {
                ["address"]       = address,
                ["command"]       = command,
                ["response"]      = response,
                ["bytes_written"] = bytesWritten,
                ["bytes_read"]    = bytesRead,
            });
            return 0;
        }
        catch (VisaException ex)
        {
            ResultWriter.WriteError("visa", ex.Operation, ex.Description,
                context: new Dictionary<string, object?> { ["address"] = address, ["command"] = command },
                statusInt: ex.Status);
            return 1;
        }
    }
}

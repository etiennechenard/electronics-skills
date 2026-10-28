using Visa.Cli;
using Visa.Commands;
using Visa.Core.Session;
using Visa.Json;

namespace Visa;

public class Program
{
    internal static readonly string[] AllSubcommands =
    [
        "list", "info", "query", "write", "read", "script",
        "clear", "trigger", "serial-poll",
        "ifc", "ren", "atn", "gpib-command",
        "get-attr", "set-attr",
        "usb-control-in", "usb-control-out",
        "read-block", "write-block",
    ];

    internal static Dictionary<string, Func<ICommandHandler>> CreateHandlers() => new()
    {
        ["list"]            = () => new ListCommand(),
        ["info"]            = () => new InfoCommand(),
        ["query"]           = () => new QueryCommand(),
        ["write"]           = () => new WriteCommand(),
        ["read"]            = () => new ReadCommand(),
        ["script"]          = () => new ScriptCommand(),
        ["clear"]           = () => new ClearCommand(),
        ["trigger"]         = () => new TriggerCommand(),
        ["serial-poll"]     = () => new SerialPollCommand(),
        ["ifc"]             = () => new IfcCommand(),
        ["ren"]             = () => new RenCommand(),
        ["atn"]             = () => new AtnCommand(),
        ["gpib-command"]    = () => new GpibCommandCommand(),
        ["get-attr"]        = () => new GetAttrCommand(),
        ["set-attr"]        = () => new SetAttrCommand(),
        ["usb-control-in"]  = () => new UsbControlInCommand(),
        ["usb-control-out"] = () => new UsbControlOutCommand(),
        ["read-block"]      = () => new ReadBlockCommand(),
        ["write-block"]     = () => new WriteBlockCommand(),
    };

    public static int Main(string[] args)
    {
        ConfigureLogger();
        return RunMain(args, CreateHandlers());
    }

    internal static void ConfigureLogger()
    {
        var levelStr = Environment.GetEnvironmentVariable("VISA_LOG_LEVEL") ?? "info";
        var level = levelStr.ToLowerInvariant() switch
        {
            "quiet" => LogLevel.Quiet,
            "debug" => LogLevel.Debug,
            _       => LogLevel.Info,
        };
        VisaLogger.Configure("[visa]", level);
    }

    internal static int RunMain(string[] args, Dictionary<string, Func<ICommandHandler>> handlers)
    {
        try
        {
            if (args.Length == 0 || (args.Length == 1 && args[0] == "--help"))
                return EmitTopLevelHelp();

            var subcommand = args[0];
            var rest = args[1..];

            if (!handlers.TryGetValue(subcommand, out var factory))
            {
                ResultWriter.WriteError("usage", "dispatch",
                    $"Unknown subcommand '{subcommand}'. Run with --help for the list.");
                return 2;
            }

            var handler = factory();

            if (Array.IndexOf(rest, "--help") >= 0)
                return EmitSubcommandHelp(subcommand, handler.HelpText);

            return handler.Run(rest);
        }
        catch (UsageException ex)
        {
            ResultWriter.WriteError("usage", ex.Operation, ex.Message);
            return 2;
        }
        catch (VisaException ex)
        {
            ResultWriter.WriteError("visa", ex.Operation, ex.Description, statusInt: ex.Status);
            return 1;
        }
        catch (DllNotFoundException ex)
        {
            ResultWriter.WriteError("visa_library_missing", "loadlibrary", ex.Message);
            return 3;
        }
        catch (BadImageFormatException ex)
        {
            ResultWriter.WriteError("visa_library_missing", "loadlibrary", ex.Message);
            return 3;
        }
        catch (Exception ex)
        {
            ResultWriter.WriteError("internal", "unhandled", ex.Message);
            return 4;
        }
    }

    private static int EmitTopLevelHelp()
    {
        const string helpText =
            "visa-skill CLI — control VISA-addressable laboratory instruments " +
            "(GPIB, USB-TMC, Serial/ASRL, TCPIP/LXI). " +
            "Usage: dotnet run --project skills/visa/scripts/Visa -- <subcommand> [args]. " +
            "Run '<subcommand> --help' for per-command help.";
        Console.Error.WriteLine(helpText);
        ResultWriter.WriteSuccess(new Dictionary<string, object?>
        {
            ["help"]        = helpText,
            ["subcommands"] = (IEnumerable<string>)AllSubcommands,
        });
        return 0;
    }

    private static int EmitSubcommandHelp(string subcommand, string helpText)
    {
        Console.Error.WriteLine(helpText);
        ResultWriter.WriteSuccess(new Dictionary<string, object?>
        {
            ["subcommand"] = subcommand,
            ["help"]       = helpText,
        });
        return 0;
    }
}

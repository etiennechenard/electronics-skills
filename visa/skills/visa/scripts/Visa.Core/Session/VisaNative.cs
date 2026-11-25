using System.Runtime.InteropServices;
using System.Text;

namespace Visa.Core.Session;

internal static class VisaNative
{
    private const string Visa32 = "visa32";
    private const string Visa64 = "visa64";
    private const string LibraryAlias = "visa";

    static VisaNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(VisaNative).Assembly, Resolver);
    }

    // Override for every OS: an absolute path (or loader name) of the VISA library to load.
    internal const string LibraryEnvVar = "VISA_LIBRARY";

    // macOS: NI-VISA installs VISA.framework, R&S VISA installs RsVisa.framework.
    internal static readonly string[] MacCandidates =
    [
        "/Library/Frameworks/VISA.framework/VISA",
        "/Library/Frameworks/RsVisa.framework/RsVisa",
        "libvisa.dylib",
    ];

    // Linux: NI-VISA ships libvisa.so(.0), R&S VISA librsvisa.so.
    internal static readonly string[] LinuxCandidates = ["libvisa.so.0", "libvisa.so", "librsvisa.so"];

    private static nint Resolver(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != LibraryAlias)
            return nint.Zero;

        var overridePath = Environment.GetEnvironmentVariable(LibraryEnvVar);
        if (!string.IsNullOrWhiteSpace(overridePath))
            return LoadFirst([overridePath], assembly, searchPath);

        if (OperatingSystem.IsWindows())
        {
            var name = Environment.Is64BitProcess ? Visa64 : Visa32;
            return NativeLibrary.TryLoad(name, assembly, searchPath, out var handle) ? handle : nint.Zero;
        }

        return LoadFirst(OperatingSystem.IsMacOS() ? MacCandidates : LinuxCandidates, assembly, searchPath);
    }

    // Loads the first candidate that opens; when none does, throws DllNotFoundException
    // naming every candidate and the loader's reason (e.g. a framework built for another
    // CPU architecture than this process), which Program maps to visa_library_missing.
    private static nint LoadFirst(IReadOnlyList<string> candidates, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
    {
        var reasons = new List<string>();
        foreach (var candidate in candidates)
        {
            try
            {
                return Path.IsPathRooted(candidate)
                    ? NativeLibrary.Load(candidate)
                    : NativeLibrary.Load(candidate, assembly, searchPath);
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
            {
                var exists = !Path.IsPathRooted(candidate) || File.Exists(candidate);
                reasons.Add(exists ? $"{candidate}: {FirstLine(ex.Message)}" : $"{candidate}: not installed");
            }
        }
        throw new DllNotFoundException(
            $"No VISA library could be loaded ({RuntimeInformation.ProcessArchitecture} process). Tried: "
            + string.Join("; ", reasons)
            + $". Install NI-VISA or R&S VISA, or set {LibraryEnvVar} to the library path.");
    }

    private static string FirstLine(string message)
    {
        var line = message.Split('\n', 2)[0].Trim().TrimEnd(':', ' ');
        return line.Length > 300 ? line[..300] : line;
    }

    // Carried-over constants
    internal const uint VI_ATTR_TMO_VALUE   = 0x3FFF001A;
    internal const uint VI_ATTR_TERMCHAR    = 0x3FFF0018;
    internal const uint VI_ATTR_TERMCHAR_EN = 0x3FFF0038;
    internal const uint VI_ATTR_SEND_END_EN = 0x3FFF0016;
    internal const int VI_FIND_BUFLEN       = 256;
    internal const ushort VI_TRIG_PROT_DEFAULT  = 0;
    internal const ushort VI_GPIB_REN_DEASSERT  = 0;
    internal const ushort VI_GPIB_REN_ASSERT    = 1;
    internal const ushort VI_GPIB_ATN_DEASSERT  = 0;
    internal const ushort VI_GPIB_ATN_ASSERT    = 1;

    // New ASRL (serial) attribute constants (G-S3)
    internal const uint VI_ATTR_ASRL_BAUD       = 0x3FFF0021;
    internal const uint VI_ATTR_ASRL_DATA_BITS  = 0x3FFF0022;
    internal const uint VI_ATTR_ASRL_PARITY     = 0x3FFF0023;
    internal const uint VI_ATTR_ASRL_STOP_BITS  = 0x3FFF0024;
    internal const uint VI_ATTR_ASRL_FLOW_CNTRL = 0x3FFF0025;

    // Carried-over P/Invokes
    [DllImport(LibraryAlias, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    internal static extern int viOpenDefaultRM(out uint sesn);

    [DllImport(LibraryAlias, ExactSpelling = true, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    internal static extern int viOpen(uint sesn, string rsrcName, uint accessMode, uint openTimeout, out uint vi);

    [DllImport(LibraryAlias, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    internal static extern int viClose(uint vi);

    // Third parameter stays uint on Windows — deliberately not widened to 64-bit (see
    // TechDesignCliContract §6 note). On 64-bit macOS/Linux, ViAttrState is ViUInt64 and the
    // SysV/AAPCS64 ABIs leave the upper half of a 32-bit argument register unspecified, so
    // the value goes through the 64-bit declaration there (zero-extended, same value).
    internal static int viSetAttribute(uint vi, uint attrName, uint attrValue) =>
        !OperatingSystem.IsWindows() && Environment.Is64BitProcess
            ? viSetAttribute64(vi, attrName, attrValue)
            : viSetAttribute32(vi, attrName, attrValue);

    [DllImport(LibraryAlias, EntryPoint = "viSetAttribute", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern int viSetAttribute32(uint vi, uint attrName, uint attrValue);

    [DllImport(LibraryAlias, EntryPoint = "viSetAttribute", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern int viSetAttribute64(uint vi, uint attrName, ulong attrValue);

    [DllImport(LibraryAlias, ExactSpelling = true, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    internal static extern int viWrite(uint vi, byte[] buf, uint count, out uint retCount);

    [DllImport(LibraryAlias, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    internal static extern int viRead(uint vi, byte[] buf, uint count, out uint retCount);

    [DllImport(LibraryAlias, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    internal static extern int viStatusDesc(uint vi, int status, StringBuilder desc);

    [DllImport(LibraryAlias, ExactSpelling = true, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    internal static extern int viFindRsrc(uint sesn, string expr, out uint findList, out uint retCnt, StringBuilder desc);

    [DllImport(LibraryAlias, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    internal static extern int viFindNext(uint findList, StringBuilder desc);

    [DllImport(LibraryAlias, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    internal static extern int viClear(uint vi);

    [DllImport(LibraryAlias, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    internal static extern int viAssertTrigger(uint vi, ushort protocol);

    [DllImport(LibraryAlias, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    internal static extern int viReadSTB(uint vi, out ushort status);

    [DllImport(LibraryAlias, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    internal static extern int viGpibSendIFC(uint vi);

    [DllImport(LibraryAlias, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    internal static extern int viGpibControlREN(uint vi, ushort mode);

    [DllImport(LibraryAlias, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    internal static extern int viGpibControlATN(uint vi, ushort mode);

    [DllImport(LibraryAlias, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    internal static extern int viGpibCommand(uint vi, [In] byte[] buf, uint count, out uint retCount);

    // New P/Invokes — width-matched viGetAttribute overloads (G-S4)
    [DllImport(LibraryAlias, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    internal static extern int viGetAttribute(uint vi, uint attrName, out uint attrValue);   // u32

    [DllImport(LibraryAlias, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    internal static extern int viGetAttribute(uint vi, uint attrName, out ushort attrValue); // u16

    [DllImport(LibraryAlias, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    internal static extern int viGetAttribute(uint vi, uint attrName, out byte attrValue);   // u8

    [DllImport(LibraryAlias, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    internal static extern int viGetAttribute(uint vi, uint attrName, [Out] byte[] attrValue); // string (caller-sized buffer)

    // New P/Invokes — USB-TMC control transfers (G-S5)
    [DllImport(LibraryAlias, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    internal static extern int viUsbControlOut(uint vi, short bmReqType, short bRequest, ushort wValue, ushort wIndex, ushort wLength, [In] byte[] buf);

    [DllImport(LibraryAlias, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    internal static extern int viUsbControlIn(uint vi, short bmReqType, short bRequest, ushort wValue, ushort wIndex, ushort wLength, [Out] byte[] buf, out ushort retCnt);
}

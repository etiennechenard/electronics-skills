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

    private static nint Resolver(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != LibraryAlias)
            return nint.Zero;
        var name = Environment.Is64BitProcess ? Visa64 : Visa32;
        return NativeLibrary.TryLoad(name, assembly, searchPath, out var handle) ? handle : nint.Zero;
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

    // Third parameter stays uint — deliberately not widened to 64-bit (see TDDCliContract §6 note)
    [DllImport(LibraryAlias, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    internal static extern int viSetAttribute(uint vi, uint attrName, uint attrValue);

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

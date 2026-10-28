namespace Visa.Core.Session;

public static class VisaBoard
{
    private static uint _rm;
    private static readonly object _rmLock = new();

    private static uint EnsureRm()
    {
        if (_rm != 0) return _rm;
        lock (_rmLock)
        {
            if (_rm != 0) return _rm;
            int status = VisaNative.viOpenDefaultRM(out uint rm);
            VisaLogger.Trace("viOpenDefaultRM", "default", status);
            VisaStatus.Throw(0, status, "viOpenDefaultRM");
            _rm = rm;
        }
        return _rm;
    }

    private static uint OpenIntfc(string intfcResource, TimeSpan timeout)
    {
        uint rm = EnsureRm();
        uint ms = (uint)Math.Max(0, (long)timeout.TotalMilliseconds);
        int status = VisaNative.viOpen(rm, intfcResource, 0, ms, out uint vi);
        VisaLogger.Trace("viOpen", intfcResource, status);
        VisaStatus.Throw(vi, status, "viOpen");
        return vi;
    }

    public static void SendIfc(string intfcResource, TimeSpan timeout)
    {
        uint vi = OpenIntfc(intfcResource, timeout);
        try
        {
            int status = VisaNative.viGpibSendIFC(vi);
            VisaLogger.Trace("viGpibSendIFC", intfcResource, status);
            VisaStatus.Throw(vi, status, "viGpibSendIFC");
        }
        finally
        {
            int cs = VisaNative.viClose(vi);
            VisaLogger.Trace("viClose", intfcResource, cs);
        }
    }

    public static void ControlRen(string intfcResource, bool assert, TimeSpan timeout)
    {
        uint vi = OpenIntfc(intfcResource, timeout);
        try
        {
            ushort mode = assert ? VisaNative.VI_GPIB_REN_ASSERT : VisaNative.VI_GPIB_REN_DEASSERT;
            int status = VisaNative.viGpibControlREN(vi, mode);
            VisaLogger.Trace("viGpibControlREN", intfcResource, status);
            VisaStatus.Throw(vi, status, "viGpibControlREN");
        }
        finally
        {
            int cs = VisaNative.viClose(vi);
            VisaLogger.Trace("viClose", intfcResource, cs);
        }
    }

    public static void ControlAtn(string intfcResource, bool assert, TimeSpan timeout)
    {
        uint vi = OpenIntfc(intfcResource, timeout);
        try
        {
            ushort mode = assert ? VisaNative.VI_GPIB_ATN_ASSERT : VisaNative.VI_GPIB_ATN_DEASSERT;
            int status = VisaNative.viGpibControlATN(vi, mode);
            VisaLogger.Trace("viGpibControlATN", intfcResource, status);
            VisaStatus.Throw(vi, status, "viGpibControlATN");
        }
        finally
        {
            int cs = VisaNative.viClose(vi);
            VisaLogger.Trace("viClose", intfcResource, cs);
        }
    }

    public static uint Command(string intfcResource, byte[] bytes, TimeSpan timeout)
    {
        uint vi = OpenIntfc(intfcResource, timeout);
        try
        {
            int status = VisaNative.viGpibCommand(vi, bytes, (uint)bytes.Length, out uint retCount);
            VisaLogger.Trace("viGpibCommand", $"{intfcResource} {bytes.Length} bytes", status);
            VisaStatus.Throw(vi, status, "viGpibCommand");
            return retCount;
        }
        finally
        {
            int cs = VisaNative.viClose(vi);
            VisaLogger.Trace("viClose", intfcResource, cs);
        }
    }
}

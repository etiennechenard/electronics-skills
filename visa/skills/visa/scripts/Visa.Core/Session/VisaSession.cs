using System.Buffers;
using System.Text;
using Visa.Core.Protocol;

namespace Visa.Core.Session;

public class VisaSession : IVisaSession
{
    private static uint _rm;
    private static readonly object _rmLock = new();

    private uint _vi;
    private string _address = string.Empty;

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

    public void Open(string visaAddress, TimeSpan timeout, SerialConfig? serialConfig = null)
    {
        _address = visaAddress;
        uint rm = EnsureRm();
        int status = VisaNative.viOpen(rm, visaAddress, 0, 0, out _vi);
        VisaLogger.Trace("viOpen", visaAddress, status);
        VisaStatus.Throw(_vi, status, "viOpen");

        SetTimeout(timeout);

        int s2 = VisaNative.viSetAttribute(_vi, VisaNative.VI_ATTR_TERMCHAR, '\n');
        VisaLogger.Trace("viSetAttribute(TERMCHAR)", visaAddress, s2);
        VisaStatus.Throw(_vi, s2, "viSetAttribute");

        // Read-termination default is interface-aware. EOI/END-capable buses (GPIB, USB-TMC,
        // VXI/PXI, TCPIP INSTR/HiSLIP) carry a hardware/protocol end indicator on the last byte,
        // so termination-char matching is redundant and can actively break reads on instruments
        // that assert EOI without a trailing '\n' — default it OFF and let END terminate the read.
        // ASRL (serial) has no EOI line and TCPIP SOCKET has no END indicator, so those buses must
        // keep termchar matching on or every read runs to timeout. Callers may override afterwards
        // via SetTermCharEnabled (CLI: --term-char-en).
        bool termCharEn = !TerminatesOnEnd(visaAddress);
        int s3 = VisaNative.viSetAttribute(_vi, VisaNative.VI_ATTR_TERMCHAR_EN, termCharEn ? 1u : 0u);
        VisaLogger.Trace($"viSetAttribute(TERMCHAR_EN={(termCharEn ? 1 : 0)})", visaAddress, s3);
        VisaStatus.Throw(_vi, s3, "viSetAttribute");

        if (serialConfig != null)
            ApplySerial(serialConfig);
    }

    /// <summary>
    /// True when the resource terminates reads with an EOI/END indicator (GPIB, USB-TMC, VXI/PXI,
    /// and TCPIP INSTR/HiSLIP), so termination-char matching is not needed. False for ASRL serial
    /// (no EOI line), TCPIP SOCKET (raw stream, no END), and any unrecognized prefix — those rely
    /// on the termination character and keep it enabled.
    /// </summary>
    internal static bool TerminatesOnEnd(string visaAddress)
    {
        var a = visaAddress.Trim().ToUpperInvariant();
        if (a.StartsWith("ASRL"))
            return false;                          // serial: no EOI hardware line
        // Resource class is the final "::"-segment. Anchor on it so a hostname or port that merely
        // contains "SOCKET"/"RAW" (e.g. TCPIP0::socket01.lab::inst0::INSTR) is not misclassified.
        if (a.EndsWith("::SOCKET"))
            return false;                          // raw TCP socket: no END indicator
        if (a.EndsWith("::RAW"))
            return false;                          // USB raw pipe: no TMC end-of-message framing
        return a.StartsWith("GPIB")
            || a.StartsWith("USB")                 // USBTMC INSTR (RAW handled above)
            || a.StartsWith("VXI")
            || a.StartsWith("PXI")
            || a.StartsWith("TCPIP");              // TCPIP INSTR (VXI-11/HiSLIP) carries END
    }

    public void SetTimeout(TimeSpan timeout)
    {
        uint ms = (uint)Math.Max(0, (long)timeout.TotalMilliseconds);
        int status = VisaNative.viSetAttribute(_vi, VisaNative.VI_ATTR_TMO_VALUE, ms);
        VisaLogger.Trace("viSetAttribute(TMO_VALUE)", _address, status);
        VisaStatus.Throw(_vi, status, "viSetAttribute");
    }

    public void SetSendEndEnabled(bool enabled)
    {
        int status = VisaNative.viSetAttribute(_vi, VisaNative.VI_ATTR_SEND_END_EN, enabled ? 1u : 0u);
        VisaLogger.Trace("viSetAttribute(SEND_END_EN)", _address, status);
        VisaStatus.Throw(_vi, status, "viSetAttribute");
    }

    public void SetTermChar(byte termChar)
    {
        int status = VisaNative.viSetAttribute(_vi, VisaNative.VI_ATTR_TERMCHAR, termChar);
        VisaLogger.Trace("viSetAttribute(TERMCHAR)", _address, status);
        VisaStatus.Throw(_vi, status, "viSetAttribute");
    }

    public void SetTermCharEnabled(bool enabled)
    {
        int status = VisaNative.viSetAttribute(_vi, VisaNative.VI_ATTR_TERMCHAR_EN, enabled ? 1u : 0u);
        VisaLogger.Trace($"viSetAttribute(TERMCHAR_EN={(enabled ? 1 : 0)})", _address, status);
        VisaStatus.Throw(_vi, status, "viSetAttribute");
    }

    public uint Write(string command)
    {
        if (!command.EndsWith('\n'))
            command += '\n';
        var bytes = Encoding.ASCII.GetBytes(command);
        int status = VisaNative.viWrite(_vi, bytes, (uint)bytes.Length, out uint retCount);
        VisaLogger.Trace("viWrite", $"{_address} {bytes.Length} bytes", status);
        VisaLogger.TracePayload("viWrite", _address, bytes);
        VisaStatus.Throw(_vi, status, "viWrite");
        return retCount;
    }

    public uint WriteRaw(byte[] bytes)
    {
        int status = VisaNative.viWrite(_vi, bytes, (uint)bytes.Length, out uint retCount);
        VisaLogger.Trace("viWrite", $"{_address} {bytes.Length} bytes", status);
        VisaLogger.TracePayload("viWrite", _address, bytes);
        VisaStatus.Throw(_vi, status, "viWrite");
        return retCount;
    }

    public string ReadLine(out uint bytesRead)
    {
        var sb = new StringBuilder();
        bytesRead = 0;
        byte[] buf = ArrayPool<byte>.Shared.Rent(1024);
        try
        {
            while (true)
            {
                int status = VisaNative.viRead(_vi, buf, (uint)buf.Length, out uint cnt);
                VisaLogger.Trace("viRead", $"{_address} {cnt} bytes", status);

                if (cnt > 0)
                {
                    VisaLogger.TracePayload("viRead", _address, buf[..(int)cnt]);
                    sb.Append(Encoding.ASCII.GetString(buf, 0, (int)cnt));
                    bytesRead += cnt;
                }

                if (status == VisaStatus.SuccessTermChar || status == VisaStatus.Success)
                    break;
                if (status == VisaStatus.SuccessMaxCnt)
                    continue;
                if (VisaStatus.IsError(status))
                    VisaStatus.Throw(_vi, status, "viRead");
                // other positive warning: accept and end
                break;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }

        return sb.ToString().TrimEnd('\r', '\n');
    }

    public void Clear()
    {
        int status = VisaNative.viClear(_vi);
        VisaLogger.Trace("viClear", _address, status);
        VisaStatus.Throw(_vi, status, "viClear");
    }

    public void AssertTrigger()
    {
        int status = VisaNative.viAssertTrigger(_vi, VisaNative.VI_TRIG_PROT_DEFAULT);
        VisaLogger.Trace("viAssertTrigger", _address, status);
        VisaStatus.Throw(_vi, status, "viAssertTrigger");
    }

    public byte ReadStb()
    {
        int status = VisaNative.viReadSTB(_vi, out ushort stb);
        VisaLogger.Trace("viReadSTB", _address, status);
        VisaStatus.Throw(_vi, status, "viReadSTB");
        return (byte)stb;
    }

    public void ApplySerial(SerialConfig config)
    {
        foreach (var (attrId, encodedValue) in config.GetSetAttributes())
        {
            int status = VisaNative.viSetAttribute(_vi, attrId, encodedValue);
            VisaLogger.Trace($"viSetAttribute(0x{attrId:X8})", _address, status);
            VisaStatus.Throw(_vi, status, "viSetAttribute");
        }
    }

    public object GetAttribute(uint id, AttrType type)
    {
        int status;
        switch (type)
        {
            case AttrType.U32:
            {
                status = VisaNative.viGetAttribute(_vi, id, out uint val);
                VisaLogger.Trace($"viGetAttribute(0x{id:X8})", _address, status);
                VisaStatus.Throw(_vi, status, "viGetAttribute");
                return val;
            }
            case AttrType.U16:
            {
                status = VisaNative.viGetAttribute(_vi, id, out ushort val);
                VisaLogger.Trace($"viGetAttribute(0x{id:X8})", _address, status);
                VisaStatus.Throw(_vi, status, "viGetAttribute");
                return val;
            }
            case AttrType.U8:
            {
                status = VisaNative.viGetAttribute(_vi, id, out byte val);
                VisaLogger.Trace($"viGetAttribute(0x{id:X8})", _address, status);
                VisaStatus.Throw(_vi, status, "viGetAttribute");
                return val;
            }
            case AttrType.Boolean:
            {
                status = VisaNative.viGetAttribute(_vi, id, out ushort val);
                VisaLogger.Trace($"viGetAttribute(0x{id:X8})", _address, status);
                VisaStatus.Throw(_vi, status, "viGetAttribute");
                return val != 0;
            }
            case AttrType.String:
            {
                var buf = new byte[256];
                status = VisaNative.viGetAttribute(_vi, id, buf);
                VisaLogger.Trace($"viGetAttribute(0x{id:X8})", _address, status);
                VisaStatus.Throw(_vi, status, "viGetAttribute");
                int len = Array.IndexOf(buf, (byte)0);
                return Encoding.ASCII.GetString(buf, 0, len < 0 ? buf.Length : len);
            }
            default:
                throw new ArgumentException($"Unknown AttrType {type}");
        }
    }

    public void SetAttribute(uint id, AttrType type, uint value)
    {
        int status = VisaNative.viSetAttribute(_vi, id, value);
        VisaLogger.Trace($"viSetAttribute(0x{id:X8})", _address, status);
        VisaStatus.Throw(_vi, status, "viSetAttribute");
    }

    public (byte[] Data, ushort RetCount) UsbControlIn(short requestType, short request, ushort wValue, ushort wIndex, ushort length)
    {
        var buf = new byte[length];
        int status = VisaNative.viUsbControlIn(_vi, requestType, request, wValue, wIndex, length, buf, out ushort retCnt);
        VisaLogger.Trace("viUsbControlIn", _address, status);
        VisaStatus.Throw(_vi, status, "viUsbControlIn");
        return (buf, retCnt);
    }

    public void UsbControlOut(short requestType, short request, ushort wValue, ushort wIndex, byte[] payload)
    {
        int status = VisaNative.viUsbControlOut(_vi, requestType, request, wValue, wIndex, (ushort)payload.Length, payload);
        VisaLogger.Trace("viUsbControlOut", _address, status);
        VisaStatus.Throw(_vi, status, "viUsbControlOut");
    }

    public byte[] ReadRaw(out uint bytesRead)
    {
        // Save and disable termchar matching so binary data containing 0x0A is not truncated
        bool savedTermCharEn = (bool)GetAttribute(VisaNative.VI_ATTR_TERMCHAR_EN, AttrType.Boolean);
        int s = VisaNative.viSetAttribute(_vi, VisaNative.VI_ATTR_TERMCHAR_EN, 0);
        VisaLogger.Trace("viSetAttribute(TERMCHAR_EN=0)", _address, s);
        VisaStatus.Throw(_vi, s, "viSetAttribute");

        var ms = new System.IO.MemoryStream();
        byte[] buf = ArrayPool<byte>.Shared.Rent(1024);
        try
        {
            while (true)
            {
                int status = VisaNative.viRead(_vi, buf, (uint)buf.Length, out uint cnt);
                VisaLogger.Trace("viRead", $"{_address} {cnt} bytes", status);
                if (cnt > 0)
                {
                    VisaLogger.TraceBlock("viRead", _address, buf[..(int)cnt]);
                    ms.Write(buf, 0, (int)cnt);
                }
                if (status == VisaStatus.Success) break;
                if (status == VisaStatus.SuccessMaxCnt) continue;
                VisaStatus.Throw(_vi, status, "viRead");
                break; // other positive warning — accept and end
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
            // Restore prior termchar-enable state; don't throw (session closed after)
            int rs = VisaNative.viSetAttribute(_vi, VisaNative.VI_ATTR_TERMCHAR_EN, savedTermCharEn ? 1u : 0u);
            VisaLogger.Trace("viSetAttribute(TERMCHAR_EN=restore)", _address, rs);
        }

        var result = ms.ToArray();
        bytesRead = (uint)result.Length;
        return result;
    }

    public static List<string> FindResources(string expression = "?*INSTR")
    {
        uint rm = EnsureRm();
        var result = new List<string>();

        var descBuf = new StringBuilder(VisaNative.VI_FIND_BUFLEN);
        int status = VisaNative.viFindRsrc(rm, expression, out uint findList, out uint retCnt, descBuf);
        VisaLogger.Trace("viFindRsrc", expression, status);

        if (status == VisaStatus.ErrorRsrcNotFound)
            return result;

        VisaStatus.Throw(rm, status, "viFindRsrc");
        result.Add(descBuf.ToString());

        for (uint i = 1; i < retCnt; i++)
        {
            descBuf.Clear();
            int nextStatus = VisaNative.viFindNext(findList, descBuf);
            VisaLogger.Trace("viFindNext", expression, nextStatus);
            VisaStatus.Throw(rm, nextStatus, "viFindNext");
            result.Add(descBuf.ToString());
        }

        VisaNative.viClose(findList);
        return result;
    }

    public void Dispose()
    {
        if (_vi != 0)
        {
            int status = VisaNative.viClose(_vi);
            VisaLogger.Trace("viClose", _address, status);
            _vi = 0;
        }
    }
}

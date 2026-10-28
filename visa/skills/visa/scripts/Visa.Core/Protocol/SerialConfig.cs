using Visa.Core.Session;

namespace Visa.Core.Protocol;

public class SerialConfig
{
    // Nullable — unset means "not written, driver default stands"
    public uint? Baud { get; private set; }
    public byte? DataBits { get; private set; }
    public SerialParity? Parity { get; private set; }
    public SerialStopBits? StopBits { get; private set; }
    public SerialFlowControl? FlowControl { get; private set; }

    public void SetBaud(uint baud)
    {
        if (baud == 0) throw new ArgumentException("Baud rate must be a positive integer.");
        Baud = baud;
    }

    public void SetDataBits(byte bits)
    {
        if (bits < 5 || bits > 8) throw new ArgumentException($"Data bits must be 5–8; got {bits}.");
        DataBits = bits;
    }

    public void SetParity(string spelling)
    {
        Parity = spelling switch
        {
            "none"  => SerialParity.None,
            "odd"   => SerialParity.Odd,
            "even"  => SerialParity.Even,
            "mark"  => SerialParity.Mark,
            "space" => SerialParity.Space,
            _ => throw new ArgumentException($"Unknown parity '{spelling}'. Expected: none, odd, even, mark, space (lowercase).")
        };
    }

    public void SetStopBits(string spelling)
    {
        StopBits = spelling switch
        {
            "1"   => SerialStopBits.One,
            "1.5" => SerialStopBits.OnePointFive,
            "2"   => SerialStopBits.Two,
            _ => throw new ArgumentException($"Unknown stop bits '{spelling}'. Expected: 1, 1.5, 2.")
        };
    }

    public void SetFlowControl(string spelling)
    {
        FlowControl = spelling switch
        {
            "none" => SerialFlowControl.None,
            "xon"  => SerialFlowControl.Xon,
            "rts"  => SerialFlowControl.Rts,
            "dtr"  => SerialFlowControl.Dtr,
            _ => throw new ArgumentException($"Unknown flow control '{spelling}'. Expected: none, xon, rts, dtr (lowercase).")
        };
    }

    /// <summary>
    /// Enumerate (VI_ATTR_ASRL_* id, encoded uint value) for each parameter that has been set.
    /// ApplySerial calls viSetAttribute for each yielded pair.
    /// </summary>
    public IEnumerable<(uint AttrId, uint EncodedValue)> GetSetAttributes()
    {
        if (Baud.HasValue)
            yield return (VisaNative.VI_ATTR_ASRL_BAUD, Baud.Value);
        if (DataBits.HasValue)
            yield return (VisaNative.VI_ATTR_ASRL_DATA_BITS, DataBits.Value);
        if (Parity.HasValue)
            yield return (VisaNative.VI_ATTR_ASRL_PARITY, (uint)Parity.Value);
        if (StopBits.HasValue)
            yield return (VisaNative.VI_ATTR_ASRL_STOP_BITS, (uint)StopBits.Value);
        if (FlowControl.HasValue)
            yield return (VisaNative.VI_ATTR_ASRL_FLOW_CNTRL, (uint)FlowControl.Value);
    }
}

public enum SerialParity
{
    None  = 0,
    Odd   = 1,
    Even  = 2,
    Mark  = 3,
    Space = 4,
}

public enum SerialStopBits
{
    One          = 10,
    OnePointFive = 15,
    Two          = 20,
}

public enum SerialFlowControl
{
    None = 0,
    Xon  = 1,
    Rts  = 2,
    Dtr  = 4,
}

using Visa.Core.Protocol;

namespace Visa.Core.Session;

public interface IVisaSession : IDisposable
{
    void Open(string visaAddress, TimeSpan timeout, SerialConfig? serialConfig = null);
    void SetTimeout(TimeSpan timeout);
    void SetSendEndEnabled(bool enabled);
    void SetTermChar(byte termChar);
    void SetTermCharEnabled(bool enabled);
    uint Write(string command);
    uint WriteRaw(byte[] bytes);
    string ReadLine(out uint bytesRead);
    void Clear();
    void AssertTrigger();
    byte ReadStb();
    void ApplySerial(SerialConfig config);
    object GetAttribute(uint id, AttrType type);
    void SetAttribute(uint id, AttrType type, uint value);
    (byte[] Data, ushort RetCount) UsbControlIn(short requestType, short request, ushort wValue, ushort wIndex, ushort length);
    void UsbControlOut(short requestType, short request, ushort wValue, ushort wIndex, byte[] payload);
    byte[] ReadRaw(out uint bytesRead);
}

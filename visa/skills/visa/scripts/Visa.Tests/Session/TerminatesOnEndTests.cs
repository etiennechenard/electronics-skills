using Visa.Core.Session;
using Xunit;

namespace Visa.Tests.Session;

public class TerminatesOnEndTests
{
    [Theory]
    // EOI/END-capable buses -> termchar matching defaults OFF (EOI/END terminates the read).
    [InlineData("GPIB1::2::INSTR")]
    [InlineData("GPIB0::5::INSTR")]
    [InlineData("USB0::0x0699::0x0401::C000001::INSTR")]
    [InlineData("VXI0::1::INSTR")]
    [InlineData("PXI0::15::INSTR")]
    [InlineData("TCPIP0::192.168.0.5::inst0::INSTR")]
    [InlineData("TCPIP0::192.168.0.5::hislip0::INSTR")]
    [InlineData("gpib1::2::instr")]                          // case-insensitive
    [InlineData("TCPIP0::socket01.lab.local::inst0::INSTR")] // hostname contains "socket": still INSTR
    [InlineData("  GPIB1::2::INSTR  ")]                      // leading/trailing whitespace is trimmed
    public void EndTerminatedBuses_ReturnTrue(string address)
    {
        Assert.True(VisaSession.TerminatesOnEnd(address));
    }

    [Theory]
    // No EOI/END indicator -> termchar matching must stay ON or reads run to timeout.
    [InlineData("ASRL1::INSTR")]
    [InlineData("ASRL10::INSTR")]
    [InlineData("TCPIP0::192.168.0.5::5025::SOCKET")]
    [InlineData("TCPIP0::192.168.0.5::inst0::socket")]           // case-insensitive
    [InlineData("USB0::0x0699::0x0401::C000001::0::RAW")]        // USB raw pipe: no TMC framing
    [InlineData("SomethingUnrecognized")]                        // unknown prefix stays conservative
    [InlineData("")]                                             // empty stays conservative
    public void NonEndTerminatedBuses_ReturnFalse(string address)
    {
        Assert.False(VisaSession.TerminatesOnEnd(address));
    }
}

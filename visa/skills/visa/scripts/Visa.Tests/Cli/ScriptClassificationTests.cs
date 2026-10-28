using Visa.Cli;
using Xunit;

namespace Visa.Tests.Cli;

public class ScriptClassificationTests
{
    [Theory]
    [InlineData("*IDN?",               ScriptClassification.Kind.Query)]
    [InlineData("MEAS:VOLT:DC?",       ScriptClassification.Kind.Query)]
    [InlineData("  MEAS:VOLT:DC?  ",   ScriptClassification.Kind.Query)]
    [InlineData("*RST",                ScriptClassification.Kind.Write)]
    [InlineData("FUNC \"VOLT:DC\"",    ScriptClassification.Kind.Write)]
    [InlineData("CONF?MORE",           ScriptClassification.Kind.Write)]
    [InlineData("TRIG?DELAY 0.5",      ScriptClassification.Kind.Write)]
    public void Classify_ReturnsCorrectKind(string command, ScriptClassification.Kind expected)
    {
        Assert.Equal(expected, ScriptClassification.Classify(command));
    }
}

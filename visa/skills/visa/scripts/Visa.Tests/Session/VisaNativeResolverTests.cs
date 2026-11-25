using Visa.Core.Session;
using Xunit;

namespace Visa.Tests.Session;

public class VisaNativeResolverTests
{
    [Fact]
    public void MacCandidates_StartWithNiThenRsFrameworks()
    {
        Assert.Equal("/Library/Frameworks/VISA.framework/VISA", VisaNative.MacCandidates[0]);
        Assert.Equal("/Library/Frameworks/RsVisa.framework/RsVisa", VisaNative.MacCandidates[1]);
    }

    // With no VISA installed (the usual CI/dev case on macOS/Linux), the first P/Invoke
    // must fail with a DllNotFoundException (-> exit 3, visa_library_missing) whose message
    // names the candidates and the override variable. Skipped where a VISA library exists.
    [Fact]
    public void NoVisaInstalled_FirstCallThrowsDllNotFoundNamingCandidates()
    {
        if (OperatingSystem.IsWindows() || !OperatingSystem.IsMacOS()) return;
        if (VisaNative.MacCandidates.Any(File.Exists)) return;
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(VisaNative.LibraryEnvVar))) return;

        var ex = Assert.Throws<DllNotFoundException>(() => VisaNative.viOpenDefaultRM(out _));
        Assert.Contains("VISA.framework", ex.Message);
        Assert.Contains(VisaNative.LibraryEnvVar, ex.Message);
    }
}

using PartsSearch;
using Xunit;

namespace PartsSearch.Tests;

public class PlaceholderTests
{
    [Fact]
    public async Task HarnessRuns()
    {
        Assert.Equal(2, await Program.Main([]));
    }
}

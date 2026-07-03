using F1Team.Core;

namespace F1Team.Core.Tests;

public class SolutionScaffoldTests
{
    [Fact]
    public void CoreAssemblyIsReferencedByTests()
    {
        Assert.Equal("F1Team.Core", typeof(CoreAssemblyMarker).Assembly.GetName().Name);
    }
}

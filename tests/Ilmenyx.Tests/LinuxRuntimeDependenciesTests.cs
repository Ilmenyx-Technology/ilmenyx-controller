// New Ilmenyx additions: see LICENSING.md.
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public class LinuxRuntimeDependenciesTests
{
    [Fact]
    public void InvariantDoesNotRequireOrProbeNativeIcu()
    {
        var dependencies = new LinuxRuntimeDependencies(_ => throw new InvalidOperationException("No native probe expected"));
        Assert.True(dependencies.CanUseGlobalization(true));
    }

    [Fact]
    public void MissingOrMismatchedIcuIsNotAccepted()
    {
        Assert.False(new LinuxRuntimeDependencies(_ => false).CanUseGlobalization(false));
        Assert.False(new LinuxRuntimeDependencies(name => name is "libicuuc.so.78" or "libicui18n.so.72")
            .CanUseGlobalization(false));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".72")]
    [InlineData(".78")]
    public void MatchingIcuPairIsAccepted(string suffix)
    {
        Assert.True(new LinuxRuntimeDependencies(name => name == "libicuuc.so" + suffix || name == "libicui18n.so" + suffix)
            .CanUseGlobalization(false));
    }
}

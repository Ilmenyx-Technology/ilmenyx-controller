// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio (mantido como Bismuth Technology)
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

    [Fact]
    public void IcuFilesBesideAppAreIgnoredWithoutTheAppLocalSwitch()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ilmenyx-icu-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "libicuuc.so.72.1.0.3"), "x");
            File.WriteAllText(Path.Combine(directory, "libicui18n.so.72.1.0.3"), "x");
            Assert.False(new LinuxRuntimeDependencies(_ => false, directory).CanUseGlobalization(false));
        }
        finally { Directory.Delete(directory, true); }
    }
}

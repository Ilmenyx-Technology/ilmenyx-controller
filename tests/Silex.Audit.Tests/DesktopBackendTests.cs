using Silex.Audit.Desktop;
using Xunit;
namespace Silex.Audit.Tests;
public class DesktopBackendTests
{
    [Theory]
    [InlineData(null,DisplayBackend.Auto)] [InlineData("",DisplayBackend.Auto)]
    [InlineData("auto",DisplayBackend.Auto)] [InlineData("x11",DisplayBackend.X11)] [InlineData("wayland",DisplayBackend.Wayland)]
    public void BackendSelectionIsExplicit(string? text,DisplayBackend expected)=>Assert.Equal(expected,DesktopBackend.Parse(text));
    [Theory]
    [InlineData("Wayland")] [InlineData("unknown")] [InlineData("wayland ")]
    public void TypoCannotSilentlySelectAnotherBackend(string text)=>Assert.Throws<ArgumentException>(()=>DesktopBackend.Parse(text));
}

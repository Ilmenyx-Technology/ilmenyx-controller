// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Bismuth Technology)
using Ilmenyx.Desktop;
using Xunit;
namespace Ilmenyx.Tests;
public class DesktopBackendTests
{
    [Theory]
    [InlineData(null,DisplayBackend.Auto)] [InlineData("",DisplayBackend.Auto)]
    [InlineData("auto",DisplayBackend.Auto)] [InlineData("x11",DisplayBackend.X11)] [InlineData("wayland",DisplayBackend.Wayland)]
    public void BackendSelectionIsExplicit(string? text,DisplayBackend expected)=>Assert.Equal(expected,DesktopBackend.Parse(text));
    [Theory]
    [InlineData("Wayland")] [InlineData("unknown")] [InlineData("wayland ")]
    public void TypoCannotSilentlySelectAnotherBackend(string text)=>Assert.Throws<ArgumentException>(()=>DesktopBackend.Parse(text));
    [Theory]
    [InlineData(DisplayBackend.Auto, ":0", "wayland-0", DisplayBackend.Auto)]      // XWayland present: keep the X11 default
    [InlineData(DisplayBackend.Auto, ":0", null, DisplayBackend.Auto)]
    [InlineData(DisplayBackend.Auto, null, "wayland-0", DisplayBackend.Wayland)]   // Wayland-only session: used to abort in XOpenDisplay
    [InlineData(DisplayBackend.Auto, "", "wayland-1", DisplayBackend.Wayland)]
    [InlineData(DisplayBackend.Auto, null, null, DisplayBackend.Auto)]             // nothing to choose: Avalonia reports the error as before
    [InlineData(DisplayBackend.X11, null, "wayland-0", DisplayBackend.X11)]        // an explicit choice is never overridden
    [InlineData(DisplayBackend.Wayland, ":0", null, DisplayBackend.Wayland)]
    public void AutoFollowsTheSession(DisplayBackend requested, string? display, string? wayland, DisplayBackend expected)=>Assert.Equal(expected,DesktopBackend.ForSession(requested,display,wayland));
}

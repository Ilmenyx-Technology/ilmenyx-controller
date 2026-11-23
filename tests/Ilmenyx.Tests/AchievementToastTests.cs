// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Avalonia;
using Ilmenyx.Desktop;
using Xunit;

namespace Ilmenyx.Tests;

public class AchievementToastTests
{
    [Fact]
    public void PositionIsBottomRightWithScaledMargin()
    {
        Assert.Equal(new PixelPoint(1920 - 404, 1040 - 124), AchievementToast.ToastPosition(new PixelRect(0, 0, 1920, 1040), 1, 380, 100));
        Assert.Equal(new PixelPoint(2000 + 2560 - 808, 2080 - 248), AchievementToast.ToastPosition(new PixelRect(2000, 0, 2560, 2080), 2, 380, 100));
    }
}

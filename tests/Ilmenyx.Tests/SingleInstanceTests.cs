// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Flávio Henrique Meira Roque (mantido como Ilmenyx Technology)
using Ilmenyx.Platform.Linux;
using Xunit;

namespace Ilmenyx.Tests;

public sealed class SingleInstanceTests
{
    [Fact]
    public async Task SecondLaunchWakesTheFirstAndAStaleSocketIsIgnored()
    {
        string dir = Directory.CreateTempSubdirectory("ilx-inst").FullName, path = Path.Combine(dir, "app", "ilmenyx.sock");
        try
        {
            Assert.False(SingleInstance.TryHandOff(path)); // nobody running
            using var stop = new CancellationTokenSource();
            var woke = new TaskCompletionSource();
            SingleInstance.Listen(path, () => woke.TrySetResult(), stop.Token);
            Assert.True(SingleInstance.TryHandOff(path));
            await woke.Task.WaitAsync(TimeSpan.FromSeconds(5));
            stop.Cancel();
            File.WriteAllText(path, ""); // what a crashed copy leaves behind
            Assert.False(SingleInstance.TryHandOff(path));
        }
        finally { Directory.Delete(dir, true); }
    }
}

// New Ilmenyx additions: see LICENSING.md.
using Avalonia.Threading;
using Ilmenyx.Core;

namespace Ilmenyx.Desktop;

/// <summary>Polls device snapshots (read-only, 3 s) while the app is open and shows a toast for each change. The first read is the baseline.</summary>
public sealed class DeviceToastService : IDisposable
{
    private readonly Func<DeviceSnapshot> read;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly Queue<ToastMessage> queue = new();
    private readonly DispatcherTimer drain = new() { Interval = TimeSpan.FromMilliseconds(1800) };
    private ToastWindow? window;
    private DeviceSnapshot? last;
    private bool busy;
    public bool Enabled { get; set; } = true;
    public int Shown { get; private set; }
    /// <summary>Raised for every notice shown (the Overview keeps a session list of them).</summary>
    public event Action<ToastMessage>? Notified;

    public DeviceToastService(Func<DeviceSnapshot> read)
    {
        this.read = read;
        timer.Tick += async (_, _) => await PollAsync();
        drain.Tick += (_, _) => Next();
    }

    public void Start() { timer.Start(); _ = PollAsync(); }

    public async Task PollAsync()
    {
        if (busy) return;
        busy = true;
        try
        {
            if (!Enabled) { last = null; return; } // re-baseline when turned back on
            var now = await Task.Run(read);
            foreach (var toast in DeviceChanges.Diff(last, now)) queue.Enqueue(toast);
            last = now;
            if (queue.Count > 0 && !drain.IsEnabled) { Next(); drain.Start(); }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        finally { busy = false; }
    }

    private void Next()
    {
        if (queue.Count == 0) { drain.Stop(); return; }
        var toast = queue.Dequeue(); Shown++; Notified?.Invoke(toast);
        window ??= new ToastWindow();
        window.Notify(toast.Title, toast.Text);
    }

    public ToastWindow? Window => window;

    public void Dispose() { timer.Stop(); drain.Stop(); window?.Close(); window = null; }
}

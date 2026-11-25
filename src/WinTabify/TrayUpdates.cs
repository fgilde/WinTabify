using System.Reflection;
using System.Windows.Threading;
using Velopack;
using static WinTabify.Copy;

namespace WinTabify;

internal sealed class TrayUpdates : IDisposable
{
    private readonly UpdateManager manager;
    private readonly Func<bool> automatic;
    private readonly Func<bool> idle;
    private readonly Action<string> notify;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromHours(6) };
    private readonly CancellationTokenSource shutdown = new();
    private VelopackAsset? pending;
    private bool busy;
    private bool manual;

    private TrayUpdates(UpdateManager manager, Func<bool> automatic, Func<bool> idle, Action<string> notify)
    {
        this.manager = manager;
        this.automatic = automatic;
        this.idle = idle;
        this.notify = notify;
        timer.Tick += OnTimer;
        timer.Start();
    }

    internal static TrayUpdates? Start(Func<bool> automatic, Func<bool> idle, Action<string> notify)
    {
        string? channel = typeof(TrayUpdates).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "WinTabifyChannel")?.Value;
        if (string.IsNullOrEmpty(channel)) return null;
        var manager = new UpdateManager($"https://fgilde.github.io/WinTabify/downloads/{channel}/");
        if (!manager.IsInstalled) return null;
        var updates = new TrayUpdates(manager, automatic, idle, notify);
        _ = updates.CheckAsync();
        return updates;
    }

    private void OnTimer(object? sender, EventArgs e) => _ = CheckAsync();

    internal async Task CheckAsync(bool requested = false)
    {
        if (shutdown.IsCancellationRequested || (!requested && !automatic())) return;
        manual |= requested;
        if (busy) return;
        busy = true;
        try
        {
            pending = manager.UpdatePendingRestart;
            if (pending == null)
            {
                var update = await manager.CheckForUpdatesAsync();
                if (shutdown.IsCancellationRequested) return;
                if (update != null && (manual || automatic()))
                {
                    await manager.DownloadUpdatesAsync(update, cancelToken: shutdown.Token);
                    pending = update.TargetFullRelease;
                }
                else if (manual)
                    notify(T("Du verwendest die aktuelle Version.", "You have the latest version."));
            }
        }
        catch (Exception ex)
        {
            if (manual && !shutdown.IsCancellationRequested)
                notify(T("Updateprüfung fehlgeschlagen. ", "Could not check for updates. ") + ex.Message);
        }
        finally { busy = false; }
        if (pending == null) manual = false;
        ApplyWhenIdle();
    }

    internal void ApplyWhenIdle()
    {
        if (pending == null || busy || shutdown.IsCancellationRequested || !(manual || automatic()) || !idle()) return;
        var update = pending;
        pending = null;
        manual = false;
        try { manager.ApplyUpdatesAndRestart(update); }
        catch (Exception ex) { notify(T("Update konnte nicht installiert werden. ", "Could not install update. ") + ex.Message); }
    }

    public void Dispose()
    {
        timer.Stop();
        shutdown.Cancel();
        shutdown.Dispose();
    }
}

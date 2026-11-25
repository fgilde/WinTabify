using System.Drawing;
using System.Text.Json;
using System.Windows;
using System.Windows.Forms;
using WinTabify.Explorer;
using WinTabify.Hotkeys;
using Shortcut = WinTabify.Hotkeys.Shortcut;
using Application = System.Windows.Application;
using static WinTabify.Copy;

namespace WinTabify;

internal sealed class Program : Application
{
    private static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinTabify", "settings.json");
    private Settings settings = Settings.Default;
    private SettingsWindow? editor;
    private HotkeyWindow hotkey = null!;
    private NotifyIcon tray = null!;
    private ToolStripItem gather = null!, configure = null!, exit = null!;
    private bool running;
    private string? status;

    [STAThread]
    private static void Main()
    {
        using var instance = new Mutex(true, @"Local\WinTabify.Instance", out bool first);
        using var show = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\WinTabify.Settings");
        if (!first) { show.Set(); return; }
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
        {
            System.Windows.MessageBox.Show(T("WinTabify benötigt Windows 11 mit Explorer-Tabs.",
                "WinTabify requires Windows 11 with File Explorer tabs."), "WinTabify");
            return;
        }
        var app = new Program { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var listener = ThreadPool.RegisterWaitForSingleObject(show,
            (_, _) => app.Dispatcher.BeginInvoke(app.ShowSettings), null, Timeout.Infinite, false);
        try { app.Run(); }
        finally { listener.Unregister(null); instance.ReleaseMutex(); }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        hotkey = new HotkeyWindow(() => { if (editor == null) StartMerge(); });
        var menu = new ContextMenuStrip();
        gather = menu.Items.Add(T("Explorer-Fenster sammeln", "Gather Explorer windows"), null, (_, _) => StartMerge());
        configure = menu.Items.Add(T("Einstellungen…", "Settings…"), null, (_, _) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        exit = menu.Items.Add(T("Beenden", "Quit"), null, (_, _) => Shutdown());
        using var stream = GetResourceStream(new Uri("Assets/app.ico", UriKind.Relative)).Stream;
        tray = new NotifyIcon
        {
            Icon = new Icon(stream, SystemInformation.SmallIconSize), Text = "WinTabify",
            ContextMenuStrip = menu, Visible = true
        };
        tray.DoubleClick += (_, _) => ShowSettings();
        Settings? saved = null;
        bool firstRun = !File.Exists(SettingsFile);
        try
        {
            saved = Settings.Load(SettingsFile);
            settings = saved ?? Settings.Default;
            var candidates = saved == null ? Shortcut.Defaults : [saved.Shortcut];
            bool registered = candidates.Any(candidate =>
            {
                var updated = settings with { Shortcut = candidate };
                if (!hotkey.TryApply(candidate, firstRun ? () => updated.Save(SettingsFile) : null)) return false;
                settings = updated;
                return true;
            });
            if (!registered) status = T("Das Tastenkürzel ist nicht verfügbar. Bitte wähle ein anderes.",
                "The shortcut is unavailable. Please choose another one.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            status = T("Einstellungen konnten nicht geladen oder gespeichert werden. ", "Could not read or save settings. ") + ex.Message;
        }
        if (firstRun || status != null) ShowSettings();
    }

    private void ShowSettings()
    {
        if (running || tray == null) return;
        if (editor != null) { editor.WindowState = WindowState.Normal; editor.Activate(); return; }
        editor = new(settings, next =>
        {
            if (!hotkey.TryApply(next.Shortcut, () => next.Save(SettingsFile))) return false;
            settings = next;
            status = null;
            return true;
        }, status);
        editor.Closed += (_, _) => editor = null;
        editor.Show();
    }

    private async void StartMerge()
    {
        if (running || editor != null) return;
        running = true;
        gather.Enabled = configure.Enabled = exit.Enabled = false;
        try
        {
            var result = await ShellWorker.GatherAsync(settings.IncludeTabbed);
            status = result.Created
                ? T($"{result.Locations} Tabs gesammelt. {result.Closed} Quellfenster geschlossen.",
                    $"Gathered {result.Locations} tabs. Closed {result.Closed} source windows.")
                : T("Keine Fenster zum Zusammenführen.", "No windows to gather.");
            int kept = result.Kept + result.Unreadable;
            if (kept > 0) status += T($" {kept} Fenster zur Prüfung offengelassen.", $" Left {kept} windows open for review.");
            tray.ShowBalloonTip(4000, "WinTabify", status, ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            status = ex is ExplorerChangedException
                ? T("Explorer hat die Tabs nicht bestätigt. Prüfe die offenen Fenster und versuche es erneut.",
                    "Explorer could not confirm the tabs. Check the open windows and try again.")
                : T("Explorer konnte den Vorgang nicht abschließen. ", "Explorer could not finish the operation. ") + ex.Message;
            tray.ShowBalloonTip(5000, "WinTabify", status, ToolTipIcon.Warning);
        }
        finally
        {
            running = false;
            gather.Enabled = configure.Enabled = exit.Enabled = true;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        hotkey.Dispose();
        tray.Visible = false;
        tray.ContextMenuStrip?.Dispose();
        tray.Icon?.Dispose();
        tray.Dispose();
        base.OnExit(e);
    }
}

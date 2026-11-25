using System.Diagnostics;
using System.Windows.Automation;

namespace WinTabify.Explorer;

internal sealed class WindowsExplorerDesktop : IExplorerDesktop
{
    public IReadOnlyList<ExplorerWindow> ReadWindows()
    {
        var frames = NativeShell.Frames();
        using var views = new ShellViews();
        return frames.Select(views.Read).ToList();
    }

    public async Task<ExplorerWindow> CreateWindowAsync(string firstLocation)
    {
        var previous = NativeShell.Frames().ToHashSet();
        var marker = Directory.CreateTempSubdirectory("WinTabify-");
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                Arguments = $"/n,\"{marker.FullName}\"",
                UseShellExecute = false
            });
            var window = await WaitAsync(() => ReadWindows().FirstOrDefault(w => !previous.Contains(w.Handle) &&
                w.Complete && w.Tabs.Count == 1 && string.Equals(w.Tabs[0].Location, marker.FullName, StringComparison.OrdinalIgnoreCase)));
            return await NavigateAsync(window, window.Tabs[0], firstLocation);
        }
        finally
        {
            try { marker.Delete(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public async Task<ExplorerWindow> AddTabAsync(ExplorerWindow target, string location)
    {
        var root = AutomationElement.FromHandle(target.Handle);
        var query = new AndCondition(
            new PropertyCondition(AutomationElement.AutomationIdProperty, "AddButton"),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
        var button = root.FindFirst(TreeScope.Descendants, query);
        if (!Read(target.Handle).Matches(target)) throw new ExplorerChangedException();
        if (button != null && button.TryGetCurrentPattern(InvokePattern.Pattern, out var action))
            ((InvokePattern)action).Invoke();
        else
        {
            var active = target.Tabs.FirstOrDefault(t => NativeShell.IsWindowVisible(t.Handle));
            if (active == null || !NativeShell.PostMessage(active.Handle, 0x0111, 0xA21B, 0))
                throw new ExplorerChangedException();
        }
        var before = target.Tabs.Select(t => t.Handle).ToHashSet();
        var expanded = await WaitAsync(() =>
        {
            var current = Read(target.Handle);
            return current.Complete && current.Tabs.Count == before.Count + 1 && target.Tabs.All(current.Tabs.Contains)
                ? current : null;
        });
        return await NavigateAsync(expanded, expanded.Tabs.Single(t => !before.Contains(t.Handle)), location);
    }

    private static async Task<ExplorerWindow> NavigateAsync(ExplorerWindow window, ExplorerTab tab, string location)
    {
        using (var views = new ShellViews())
        {
            if (!views.Read(window.Handle).Matches(window)) throw new ExplorerChangedException();
            views.Navigate(window.Handle, tab, location);
        }
        var expected = window with { Tabs = window.Tabs.Select(t => t == tab ? t with { Location = location } : t).ToList() };
        return await WaitAsync(() =>
        {
            var current = Read(window.Handle);
            return current.Matches(expected) ? current : null;
        });
    }

    public async Task<bool> CloseIfUnchangedAsync(ExplorerWindow source, ExplorerWindow target)
    {
        while (source.Tabs.Count > 0)
        {
            if (!RequestClose(source, target)) return false;
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(4))
            {
                if (!NativeShell.IsWindow(source.Handle)) return true;
                if (NativeShell.TabHandles(source.Handle).Count < source.Tabs.Count)
                {
                    var remaining = Read(source.Handle);
                    if (remaining.Complete)
                    {
                        if (remaining.Tabs.Count != source.Tabs.Count - 1 || !remaining.Tabs.All(source.Tabs.Contains))
                            return false;
                        source = remaining;
                        break;
                    }
                }
                await Task.Delay(75);
            }
            if (timer.Elapsed >= TimeSpan.FromSeconds(4)) return !NativeShell.IsWindow(source.Handle);
        }
        return false;
    }

    private static bool RequestClose(ExplorerWindow source, ExplorerWindow target)
    {
        if (source.Handle == target.Handle || !NativeShell.IsWindowEnabled(source.Handle)) return false;
        var root = AutomationElement.FromHandle(source.Handle);
        var strip = root.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Tab));
        if (strip != null)
        {
            int count = strip.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem)).Count;
            if (count > 0 && count != source.Tabs.Count) return false;
        }
        using var latest = new ShellViews();
        if (!latest.Read(target.Handle).Matches(target) || !latest.Read(source.Handle).Matches(source))
            return false;
        if (source.Tabs.Count == 1) return NativeShell.PostMessage(source.Handle, 0x0010, 0, 0);
        var selected = source.Tabs.FirstOrDefault(t => NativeShell.IsWindowVisible(t.Handle));
        latest.CloseTab(selected ?? source.Tabs[0]);
        return true;
    }

    private static ExplorerWindow Read(nint frame)
    {
        using var views = new ShellViews();
        return views.Read(frame);
    }

    private static async Task<ExplorerWindow> WaitAsync(Func<ExplorerWindow?> probe)
    {
        var timer = Stopwatch.StartNew();
        do
        {
            var window = probe();
            if (window != null) return window;
            await Task.Delay(100);
        } while (timer.Elapsed < TimeSpan.FromSeconds(12));
        throw new ExplorerChangedException();
    }
}

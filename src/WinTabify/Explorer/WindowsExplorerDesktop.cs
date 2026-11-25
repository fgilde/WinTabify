using System.Diagnostics;
using System.Windows.Automation;

namespace WinTabify.Explorer;

internal sealed class WindowsExplorerDesktop : IExplorerDesktop
{
    private static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(12);

    public IReadOnlyList<ExplorerWindow> ReadWindows()
    {
        using var views = new ShellViews();
        return NativeShell.Frames().Select(views.Read).ToList();
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
            var tab = window.Tabs[0];
            using (var views = new ShellViews()) views.Navigate(window.Handle, tab, firstLocation);
            return await ConfirmAsync(window with { Tabs = [tab with { Location = firstLocation }] });
        }
        finally
        {
            try { marker.Delete(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public async Task<ExplorerWindow> AddTabsAsync(ExplorerWindow target, IReadOnlyList<string> locations)
    {
        if (!Read(target.Handle).Matches(target)) throw new ExplorerChangedException();
        var expected = target.Tabs.ToList();
        var known = expected.Select(t => t.Handle).ToHashSet();
        foreach (var location in locations)
        {
            var handles = NativeShell.TabHandles(target.Handle);
            if (!known.SetEquals(handles)) throw new ExplorerChangedException();
            RequestTab(target.Handle, handles);
            var tab = await WaitAsync(() =>
            {
                var current = NativeShell.TabHandles(target.Handle);
                if (!known.IsSubsetOf(current) || current.Count > known.Count + 1)
                    throw new ExplorerChangedException();
                if (current.Count == known.Count) return null;
                nint handle = current.Single(h => !known.Contains(h));
                using var views = new ShellViews();
                var added = views.ReadTab(handle);
                if (added == null) return null;
                views.Navigate(target.Handle, added, location);
                return added with { Location = location };
            });
            known.Add(tab.Handle);
            expected.Add(tab);
        }
        return await ConfirmAsync(target with { Tabs = expected });
    }

    private static void RequestTab(nint frame, List<nint> handles)
    {
        nint selected = handles.FirstOrDefault(NativeShell.IsWindowVisible);
        if (selected != 0 && NativeShell.PostMessage(selected, 0x0111, 0xA21B, 0)) return;
        var root = AutomationElement.FromHandle(frame);
        var button = root.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.AutomationIdProperty, "AddButton"),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)));
        if (button == null || !button.TryGetCurrentPattern(InvokePattern.Pattern, out var action))
            throw new ExplorerChangedException();
        ((InvokePattern)action).Invoke();
    }

    public async Task<bool> CloseIfUnchangedAsync(ExplorerWindow source, ExplorerWindow target)
    {
        if (source.Handle == target.Handle) return false;
        try
        {
            if (HasUnaccountedTabs(source)) return false;
            while (true)
            {
                if (!NativeShell.IsWindowEnabled(source.Handle)) return false;
                if (!await RequestCloseAsync(source, target)) return false;
                var timer = Stopwatch.StartNew();
                while (NativeShell.IsWindow(source.Handle))
                {
                    if (timer.Elapsed >= TimeSpan.FromSeconds(4)) return false;
                    if (NativeShell.TabHandles(source.Handle).Count < source.Tabs.Count)
                    {
                        var remaining = Read(source.Handle);
                        if (remaining.Complete)
                        {
                            if (!IsVerifiedRemainder(remaining, source)) return false;
                            source = remaining;
                            break;
                        }
                    }
                    await Task.Delay(25);
                }
                if (!NativeShell.IsWindow(source.Handle)) return true;
            }
        }
        catch (Exception ex) when (ShellViews.IsUnavailable(ex) || ex is ElementNotAvailableException or ExplorerChangedException)
        {
            return !NativeShell.IsWindow(source.Handle);
        }
    }

    private static bool HasUnaccountedTabs(ExplorerWindow source)
    {
        var strip = AutomationElement.FromHandle(source.Handle).FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Tab));
        return strip != null && strip.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem)).Count > source.Tabs.Count;
    }

    private static async Task<bool> RequestCloseAsync(ExplorerWindow source, ExplorerWindow target)
    {
        var timer = Stopwatch.StartNew();
        do
        {
            if (!NativeShell.IsWindowEnabled(source.Handle)) return false;
            using (var views = new ShellViews())
            {
                var destination = views.Read(target.Handle);
                var current = views.Read(source.Handle);
                if (destination.Complete && current.Complete)
                {
                    if (!destination.Matches(target) || !current.Matches(source)) return false;
                    return NativeShell.RequestWindowClose(source.Handle);
                }
            }
            await Task.Delay(30);
        } while (timer.Elapsed < TimeSpan.FromSeconds(2));
        return false;
    }

    internal static bool IsVerifiedRemainder(ExplorerWindow current, ExplorerWindow previous)
    {
        if (current.Handle != previous.Handle || !current.Complete || current.Tabs.Count >= previous.Tabs.Count)
            return false;
        var locations = previous.Tabs.Select(t => t.Location).ToList();
        return current.Tabs.All(tab => locations.Remove(tab.Location));
    }

    private static ExplorerWindow Read(nint frame)
    {
        using var views = new ShellViews();
        return views.Read(frame);
    }

    private static Task<ExplorerWindow> ConfirmAsync(ExplorerWindow expected) => WaitAsync(() =>
    {
        var current = Read(expected.Handle);
        return current.Matches(expected) ? current : null;
    });

    private static async Task<T> WaitAsync<T>(Func<T?> probe) where T : class
    {
        var timer = Stopwatch.StartNew();
        do
        {
            if (probe() is { } value) return value;
            await Task.Delay(25);
        } while (timer.Elapsed < NavigationTimeout);
        throw new ExplorerChangedException();
    }
}

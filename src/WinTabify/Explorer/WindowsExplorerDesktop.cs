using System.Diagnostics;
using System.Windows.Automation;

namespace WinTabify.Explorer;

internal sealed class WindowsExplorerDesktop(Action<string>? trace = null) : IExplorerDesktop
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
            var added = await WaitAsync(() =>
            {
                var current = NativeShell.TabHandles(target.Handle);
                if (!known.IsSubsetOf(current) || current.Count > known.Count + 1)
                    throw new ExplorerChangedException();
                if (current.Count == known.Count) return null;
                nint handle = current.Single(h => !known.Contains(h));
                using var views = new ShellViews();
                var ready = views.ReadTab(handle);
                if (ready == null) return null;
                views.Navigate(target.Handle, ready, location);
                return ready with { Location = location };
            });
            known.Add(added.Handle);
            expected.Add(added);
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
            var initialTabIds = new HashSet<string>(StringComparer.Ordinal);
            if (!await RequestCloseAsync(source, target, initialTabIds, true)) return !NativeShell.IsWindow(source.Handle);
            var quiet = Stopwatch.StartNew();
            var limit = Stopwatch.StartNew();
            int lastCount = source.Tabs.Count;
            int attempts = 0;
            while (NativeShell.IsWindow(source.Handle))
            {
                int count = NativeShell.TabHandles(source.Handle).Count;
                if (count != lastCount)
                {
                    if (count < lastCount) limit.Restart();
                    lastCount = count;
                    quiet.Restart();
                    attempts = 0;
                }
                if (count > 0 && attempts < 3 && quiet.ElapsedMilliseconds >= 250 && NativeShell.IsWindowEnabled(source.Handle))
                {
                    var remaining = Read(source.Handle);
                    if (remaining.Matches(source) || IsVerifiedRemainder(remaining, source))
                    {
                        if (await RequestCloseAsync(remaining, target, initialTabIds, false))
                        {
                            source = remaining;
                            attempts++;
                        }
                    }
                    else trace?.Invoke($"Frame {source.Handle}: remainder {remaining.Tabs.Count}/{source.Tabs.Count}, complete={remaining.Complete}.");
                    quiet.Restart();
                }
                if (limit.Elapsed.TotalSeconds >= 7) return !NativeShell.IsWindow(source.Handle);
                await Task.Delay(25);
            }
            return true;
        }
        catch (Exception ex) when (ShellViews.IsUnavailable(ex) || ex is ElementNotAvailableException or ExplorerChangedException)
        {
            trace?.Invoke($"Frame {source.Handle}: {ex.GetType().Name} {ex.Message}");
            return !NativeShell.IsWindow(source.Handle);
        }
    }

    private static AutomationElement[] ReadTabItems(AutomationElement root)
    {
        var strip = root.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Tab));
        return strip?.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty,
            ControlType.TabItem)).Cast<AutomationElement>().ToArray() ?? [];
    }

    private static string TabId(AutomationElement tab) => string.Join(".", tab.GetRuntimeId());

    private static bool CloseTabItems(IEnumerable<AutomationElement> tabs)
    {
        bool requested = false;
        foreach (var tab in tabs)
        {
            try
            {
                var close = tab.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "CloseButton"));
                if (close == null || !close.TryGetCurrentPattern(InvokePattern.Pattern, out var action)) continue;
                ((InvokePattern)action).Invoke();
                requested = true;
            }
            catch (ElementNotAvailableException) { }
            catch (ElementNotEnabledException) { }
        }
        return requested;
    }

    private async Task<bool> RequestCloseAsync(ExplorerWindow source, ExplorerWindow target,
        HashSet<string> initialTabIds, bool first)
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
                    var root = AutomationElement.FromHandle(source.Handle);
                    var items = ReadTabItems(root);
                    bool retained = destination.ContainsExactly(target.Tabs.Select(t => t.Location).ToArray());
                    bool same = current.Matches(source);
                    bool known = first ? items.Length <= current.Tabs.Count :
                        items.All(tab => initialTabIds.Contains(TabId(tab)));
                    if (!retained || !same || !known)
                    {
                        trace?.Invoke($"Frame {source.Handle}: destination={retained}, source={same}, known={known}, accessible={items.Length}, native={current.Tabs.Count}.");
                        return false;
                    }
                    if (first) initialTabIds.UnionWith(items.Select(TabId));
                    if (items.Length > 1)
                    {
                        var selected = items.FirstOrDefault(t => Equals(
                            t.GetCurrentPropertyValue(SelectionItemPattern.IsSelectedProperty), true)) ?? items[0];
                        if (CloseTabItems(items.Where(t => t != selected)))
                        {
                            trace?.Invoke($"Frame {source.Handle}: invoked tab strip close for {items.Length - 1} background tabs.");
                            return true;
                        }
                    }
                    if (current.Tabs.Count > 1)
                    {
                        var titleBar = root.FindFirst(TreeScope.Descendants,
                            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TitleBar));
                        var close = titleBar?.FindFirst(TreeScope.Descendants,
                            new PropertyCondition(AutomationElement.AutomationIdProperty, "Close"));
                        if (close != null && close.TryGetCurrentPattern(InvokePattern.Pattern, out var action))
                        {
                            trace?.Invoke($"Frame {source.Handle}: invoking title bar Close for {current.Tabs.Count} tabs.");
                            ((InvokePattern)action).Invoke();
                            return true;
                        }
                    }
                    trace?.Invoke($"Frame {source.Handle}: requesting Shell close for {current.Tabs.Count} tabs.");
                    return views.RequestClose(current);
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

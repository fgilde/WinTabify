using System.Diagnostics;
using System.Windows.Automation;

namespace WinTabify.Explorer;

internal sealed class WindowsExplorerDesktop(Action<string>? trace = null) : IExplorerDesktop
{
    private static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(12);

    public IReadOnlyList<ExplorerWindow> ReadWindows(bool includeTabbed = true)
    {
        using var views = new ShellViews();
        var windows = NativeShell.Frames().Select(views.Read);
        return (includeTabbed ? windows : windows.Where(IsSingleTabWindow)).ToList();
    }

    private static bool IsSingleTabWindow(ExplorerWindow window)
    {
        if (!window.Complete || window.Tabs.Count != 1) return false;
        try { return ReadTabItems(AutomationElement.FromHandle(window.Handle)).Length == 1; }
        catch (Exception ex) when (ShellViews.IsUnavailable(ex) || ex is ElementNotAvailableException) { return false; }
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
            using var views = new ShellViews();
            var window = await WaitAsync(() => NativeShell.Frames().Where(h => !previous.Contains(h)).Select(views.Read)
                .FirstOrDefault(w => w.Complete && w.Tabs.Count == 1 &&
                    string.Equals(w.Tabs[0].Location, marker.FullName, StringComparison.OrdinalIgnoreCase)));
            var tab = window.Tabs[0];
            views.Navigate(window.Handle, tab, firstLocation);
            return await ConfirmAsync(views, window with { Tabs = [tab with { Location = firstLocation }] });
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
        using var views = new ShellViews();
        if (!views.Read(target.Handle).Matches(target)) throw new ExplorerChangedException();
        var expected = target.Tabs.ToList();
        var known = expected.Select(t => t.Handle).ToHashSet();
        foreach (var batch in locations.Chunk(8))
        {
            var handles = NativeShell.TabHandles(target.Handle);
            if (!known.SetEquals(handles)) throw new ExplorerChangedException();
            for (int i = 0; i < batch.Length; i++) RequestTab(target.Handle, handles);
            int assigned = 0;
            var pending = new List<ExplorerTab>(batch.Length);
            await WaitAsync(() =>
            {
                var current = NativeShell.TabHandles(target.Handle);
                if (!known.IsSubsetOf(current)) throw new ExplorerChangedException();
                foreach (var handle in current.Where(h => !known.Contains(h)))
                {
                    if (assigned == batch.Length) throw new ExplorerChangedException();
                    var tab = new ExplorerTab(handle, batch[assigned++]);
                    known.Add(handle);
                    expected.Add(tab);
                    pending.Add(tab);
                }
                pending.RemoveAll(tab => views.TryNavigateNewTab(target.Handle, tab));
                return assigned == batch.Length && pending.Count == 0 ? target : null;
            });
        }
        return await ConfirmAsync(views, target with { Tabs = expected });
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

    public async Task<int> CloseSourcesAsync(IReadOnlyList<ExplorerWindow> sources, ExplorerWindow target)
    {
        if (sources.Count == 0) return 0;
        var byLocation = target.Tabs.ToDictionary(t => t.Location, FolderLocationComparer.Instance);
        var singles = sources.Where(w => w.Tabs.Count == 1).ToArray();
        int chunkSize = Math.Max(1, (singles.Length + 3) / 4);
        var workers = singles.Chunk(chunkSize).Select(group => ShellWorker.RunAsync(() => CloseGroupAsync(group))).ToList();
        workers.Add(CloseGroupAsync(sources.Where(w => w.Tabs.Count > 1)));
        return (await Task.WhenAll(workers)).Sum();

        async Task<int> CloseGroupAsync(IEnumerable<ExplorerWindow> group)
        {
            using var views = new ShellViews();
            var results = await Task.WhenAll(group.Select(source =>
                CloseIfUnchangedAsync(source, target, byLocation, views)));
            return results.Count(closed => closed);
        }
    }

    private async Task<bool> CloseIfUnchangedAsync(ExplorerWindow source, ExplorerWindow target,
        IReadOnlyDictionary<string, ExplorerTab> byLocation, ShellViews views)
    {
        if (source.Handle == target.Handle) return false;
        var required = source.Tabs.Select(t => byLocation[t.Location]).Distinct().ToArray();
        try
        {
            var initialTabIds = new HashSet<string>(StringComparer.Ordinal);
            if (!await RequestCloseAsync(source, target, required, initialTabIds, true, views)) return !NativeShell.IsWindow(source.Handle);
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
                    var remaining = views.Read(source.Handle);
                    if (remaining.Matches(source) || IsVerifiedRemainder(remaining, source))
                    {
                        if (await RequestCloseAsync(remaining, target, required, initialTabIds, false, views))
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
            trace?.Invoke($"Frame {source.Handle}: {ex}");
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
        ExplorerTab[] required, HashSet<string> initialTabIds, bool first, ShellViews views)
    {
        var timer = Stopwatch.StartNew();
        do
        {
            if (!NativeShell.IsWindowEnabled(source.Handle)) return false;
            var current = views.Read(source.Handle);
            if (current.Complete)
            {
                bool retained = required.All(t => NativeShell.GetAncestor(t.Handle, 2) == target.Handle &&
                    views.ReadTab(t.Handle) == t);
                bool same = current.Matches(source);
                if (!retained || !same) return false;
                if (source.Tabs.Count == 1 && initialTabIds.Count == 0)
                    return views.RequestTabClose(source.Handle, source.Tabs[0]);
                var root = AutomationElement.FromHandle(source.Handle);
                var items = ReadTabItems(root);
                bool known = first ? items.Length <= current.Tabs.Count :
                    items.All(tab => initialTabIds.Contains(TabId(tab)));
                if (!known)
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

    private async Task<ExplorerWindow> ConfirmAsync(ShellViews views, ExplorerWindow expected)
    {
        ExplorerWindow? current = null;
        try
        {
            return await WaitAsync(() =>
            {
                current = views.Read(expected.Handle);
                return current.Matches(expected) ? current : null;
            });
        }
        catch (ExplorerChangedException)
        {
            trace?.Invoke($"Expected [{string.Join(", ", expected.Tabs)}], observed [{string.Join(", ", current?.Tabs ?? [])}].");
            throw;
        }
    }

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

namespace WinTabify.Explorer;

internal sealed class MergeService(IExplorerDesktop desktop)
{
    internal async Task<MergeResult> GatherAsync(bool includeTabbed)
    {
        var windows = desktop.ReadWindows();
        var sources = windows.Where(w => w.Complete && (includeTabbed || w.Tabs.Count == 1)).ToList();
        var comparer = FolderLocationComparer.Instance;
        var locations = sources.SelectMany(w => w.Tabs).Select(t => t.Location).Distinct(comparer).ToList();
        int unreadable = windows.Count(w => !w.Complete);
        if (sources.Count == 0 || (sources.Count == 1 && sources[0].ContainsExactly(locations)))
            return new(locations.Count, 0, unreadable, 0, false);

        var target = sources.Where(w => w.Tabs.Select(t => t.Location).Distinct(comparer).Count() == w.Tabs.Count)
            .MaxBy(w => w.Tabs.Count);
        if (target == null)
        {
            target = await desktop.CreateWindowAsync(locations[0]);
            if (sources.Any(w => w.Handle == target.Handle) || !target.ContainsExactly([locations[0]]))
                throw new ExplorerChangedException();
        }
        else
        {
            sources.Remove(target);
        }

        var missing = locations.Except(target.Tabs.Select(t => t.Location), comparer).ToList();
        if (missing.Count > 0)
        {
            var original = target;
            target = await desktop.AddTabsAsync(original, missing);
            if (target.Handle != original.Handle || !original.Tabs.All(target.Tabs.Contains))
                throw new ExplorerChangedException();
        }
        if (!target.ContainsExactly(locations)) throw new ExplorerChangedException();

        var closed = await Task.WhenAll(sources.Select(w => desktop.CloseIfUnchangedAsync(w, target)));
        return new(locations.Count, closed.Count(x => x), unreadable, closed.Count(x => !x), true);
    }
}

internal sealed class ExplorerChangedException() : Exception("Explorer changed or could not confirm the requested tabs.");

namespace WinTabify.Explorer;

internal sealed class MergeService(IExplorerDesktop desktop)
{
    internal async Task<MergeResult> GatherAsync(bool includeTabbed)
    {
        var windows = desktop.ReadWindows();
        var sources = windows.Where(w => w.Complete && (includeTabbed || w.Tabs.Count == 1)).ToList();
        int unreadable = windows.Count(w => !w.Complete);
        var locations = sources.SelectMany(w => w.Tabs).Select(t => t.Location)
            .Distinct(FolderLocationComparer.Instance).ToList();

        if (sources.Count == 0 || (sources.Count == 1 && sources[0].ContainsExactly(locations)))
            return new(locations.Count, 0, unreadable, 0, false);

        var target = await desktop.CreateWindowAsync(locations[0]);
        var expected = new List<string> { locations[0] };
        ValidateTarget(target, expected, sources);
        foreach (var location in locations.Skip(1))
        {
            var next = await desktop.AddTabAsync(target, location);
            expected.Add(location);
            if (next.Handle != target.Handle || !target.Tabs.All(next.Tabs.Contains))
                throw new ExplorerChangedException();
            ValidateTarget(next, expected, sources);
            target = next;
        }

        int closed = 0;
        foreach (var source in sources)
        {
            if (await desktop.CloseIfUnchangedAsync(source, target))
                closed++;
        }
        return new(locations.Count, closed, unreadable, sources.Count - closed, true);
    }

    private static void ValidateTarget(ExplorerWindow target, IReadOnlyList<string> paths, List<ExplorerWindow> sources)
    {
        if (sources.Any(w => w.Handle == target.Handle) || !target.ContainsExactly(paths))
            throw new ExplorerChangedException();
    }
}

internal sealed class ExplorerChangedException() : Exception("Explorer changed or could not confirm the requested tabs.");

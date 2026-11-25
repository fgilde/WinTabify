namespace WinTabify.Explorer;

internal sealed record ExplorerTab(nint Handle, string Location);

internal sealed record ExplorerWindow(nint Handle, IReadOnlyList<ExplorerTab> Tabs, bool Complete)
{
    internal bool Matches(ExplorerWindow expected) =>
        Complete && expected.Complete && Handle == expected.Handle && Tabs.Count == expected.Tabs.Count &&
        Tabs.OrderBy(t => t.Handle).SequenceEqual(expected.Tabs.OrderBy(t => t.Handle));

    internal bool ContainsExactly(IReadOnlyList<string> locations) =>
        Complete && Tabs.Count == locations.Count &&
        Tabs.Select(t => t.Location).ToHashSet(FolderLocationComparer.Instance).SetEquals(locations);
}

internal interface IExplorerDesktop
{
    IReadOnlyList<ExplorerWindow> ReadWindows(bool includeTabbed = true);
    Task<ExplorerWindow> CreateWindowAsync(string firstLocation);
    Task<ExplorerWindow> AddTabsAsync(ExplorerWindow target, IReadOnlyList<string> locations);
    Task<int> CloseSourcesAsync(IReadOnlyList<ExplorerWindow> sources, ExplorerWindow target);
}

internal sealed record MergeResult(int Locations, int Closed, int Unreadable, int Kept, bool Gathered);

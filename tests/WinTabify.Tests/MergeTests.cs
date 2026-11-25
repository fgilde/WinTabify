using WinTabify.Explorer;
using Xunit;

namespace WinTabify.Tests;

public sealed class MergeTests
{
    [Theory]
    [InlineData(true, 3, 1)]
    [InlineData(false, 2, 2)]
    public async Task GathersSelectedWindowsAndDeduplicatesLocations(bool includeTabbed, int closed, int remaining)
    {
        var desktop = new Desktop(Window(1, "Work", "Photos"), Window(2, "Work"), Window(3, "Music"));
        var result = await new MergeService(desktop).GatherAsync(includeTabbed);
        Assert.Equal(closed, result.Closed);
        Assert.Equal(remaining, desktop.Windows.Count);
        Assert.Equal(includeTabbed ? ["Work", "Photos", "Music"] : new[] { "Work", "Music" },
            desktop.Windows.Last().Tabs.Select(t => t.Location));
        if (!includeTabbed) Assert.Equal(2, desktop.Windows[0].Tabs.Count);
        Assert.False((await new MergeService(desktop).GatherAsync(includeTabbed)).Created);
    }

    [Fact]
    public async Task CollapsesDuplicatesWithinASingleWindow()
    {
        var desktop = new Desktop(Window(1, "Work", "Work"));
        var result = await new MergeService(desktop).GatherAsync(true);
        Assert.Equal(1, result.Locations);
        Assert.Single(Assert.Single(desktop.Windows).Tabs);
    }

    [Fact]
    public async Task NeverClosesSourcesOnUnconfirmedNavigation()
    {
        var desktop = new Desktop(Window(1, "Work"), Window(2, "Music")) { WrongDestination = true };
        await Assert.ThrowsAsync<ExplorerChangedException>(() => new MergeService(desktop).GatherAsync(true));
        Assert.Equal(0, desktop.CloseAttempts);
    }

    [Fact]
    public async Task RejectsAnExistingWindowReturnedAsTheDestination()
    {
        var desktop = new Desktop(Window(1, "Work"), Window(2, "Music")) { ReuseSource = true };
        await Assert.ThrowsAsync<ExplorerChangedException>(() => new MergeService(desktop).GatherAsync(true));
        Assert.Equal(0, desktop.CloseAttempts);
    }

    [Fact]
    public async Task KeepsUnreadableWindowIntact()
    {
        var incomplete = Window(1, "Work") with { Complete = false };
        var desktop = new Desktop(incomplete, Window(2, "Music"));
        var result = await new MergeService(desktop).GatherAsync(true);
        Assert.Equal(1, result.Unreadable);
        Assert.False(result.Created);
        Assert.Contains(incomplete, desktop.Windows);
    }

    [Fact]
    public async Task LeavesChangedSourceOpen()
    {
        var desktop = new Desktop(Window(1, "Work"), Window(2, "Music"));
        desktop.BeforeClose = () => desktop.Windows[0] = Window(1, "Work", "New folder");
        var result = await new MergeService(desktop).GatherAsync(true);
        Assert.Equal(1, result.Kept);
        Assert.Equal("New folder", desktop.Windows[0].Tabs[1].Location);
    }

    [Fact]
    public async Task LeavesRemainingSourcesWhenTargetChanges()
    {
        var desktop = new Desktop(Window(1, "Work"), Window(2, "Music"));
        desktop.BeforeClose = () => desktop.Windows[^1] = Window(99, "Different");
        var result = await new MergeService(desktop).GatherAsync(true);
        Assert.Equal(0, result.Closed);
        Assert.Equal(2, result.Kept);
    }

    [Fact]
    public async Task EmptyDesktopDoesNothing()
    {
        var desktop = new Desktop();
        Assert.False((await new MergeService(desktop).GatherAsync(true)).Created);
        Assert.Empty(desktop.Windows);
    }

    private static ExplorerWindow Window(int frame, params string[] locations) =>
        new(frame, locations.Select((s, i) => new ExplorerTab(100 * frame + i, s)).ToList(), true);

    private sealed class Desktop(params ExplorerWindow[] windows) : IExplorerDesktop
    {
        internal List<ExplorerWindow> Windows { get; } = [.. windows];
        internal bool WrongDestination { get; init; }
        internal bool ReuseSource { get; init; }
        internal int CloseAttempts { get; private set; }
        internal Action? BeforeClose { get; set; }

        public IReadOnlyList<ExplorerWindow> ReadWindows() => Windows.ToList();

        public Task<ExplorerWindow> CreateWindowAsync(string firstLocation)
        {
            if (ReuseSource) return Task.FromResult(Windows[0]);
            var target = Window(99, firstLocation);
            Windows.Add(target);
            return Task.FromResult(target);
        }

        public Task<ExplorerWindow> AddTabAsync(ExplorerWindow target, string location)
        {
            var next = target with { Tabs = [.. target.Tabs, new(9900 + target.Tabs.Count, WrongDestination ? "Other" : location)] };
            Windows[^1] = next;
            return Task.FromResult(next);
        }

        public Task<bool> CloseIfUnchangedAsync(ExplorerWindow source, ExplorerWindow target)
        {
            CloseAttempts++;
            BeforeClose?.Invoke();
            BeforeClose = null;
            var current = Windows.Single(w => w.Handle == source.Handle);
            return Task.FromResult(Windows[^1].Matches(target) && current.Matches(source) && Windows.Remove(current));
        }
    }
}

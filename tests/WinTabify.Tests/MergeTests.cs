using WinTabify.Explorer;
using Xunit;

namespace WinTabify.Tests;

public sealed class MergeTests
{
    [Theory]
    [InlineData(true, 2, 1)]
    [InlineData(false, 1, 2)]
    public async Task GathersSelectedWindowsAndDeduplicatesLocations(bool includeTabbed, int closed, int remaining)
    {
        var group = Window(1, "Work", "Photos");
        var desktop = new Desktop(group, Window(2, "Work"), Window(3, "Music"));
        var result = await new MergeService(desktop).GatherAsync(includeTabbed);
        Assert.Equal(closed, result.Closed);
        Assert.Equal(remaining, desktop.Windows.Count);
        Assert.Equal(["Music"], desktop.OpenedTabs);
        Assert.Equal(0, desktop.CreatedWindows);
        if (includeTabbed) Assert.True(Assert.Single(desktop.Windows).ContainsExactly(["Work", "Photos", "Music"]));
        else Assert.Contains(group, desktop.Windows);
        Assert.False((await new MergeService(desktop).GatherAsync(includeTabbed)).Gathered);
    }

    [Fact]
    public async Task EntirelyOverlappingSourceStillCloses()
    {
        var target = Window(1, "Work", "Photos", "Music");
        var desktop = new Desktop(target, Window(2, "Photos", "Work", "Photos"), Window(3, "Music"));
        var result = await new MergeService(desktop).GatherAsync(true);
        Assert.Equal(2, result.Closed);
        Assert.Empty(desktop.OpenedTabs);
        Assert.Equal(target, Assert.Single(desktop.Windows));
    }

    [Fact]
    public async Task CollapsesDuplicatesWithinASingleWindow()
    {
        var desktop = new Desktop(Window(1, "Work", "Work"));
        var result = await new MergeService(desktop).GatherAsync(true);
        Assert.Equal(1, result.Locations);
        Assert.Equal(1, desktop.CreatedWindows);
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
    public async Task RejectsAnExistingWindowReturnedAsANewDestination()
    {
        var desktop = new Desktop(Window(1, "Work", "Work")) { ReuseSource = true };
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
        Assert.False(result.Gathered);
        Assert.Contains(incomplete, desktop.Windows);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task KeepsSourceWhenEitherWindowChanges(int changed)
    {
        var desktop = new Desktop(Window(1, "Work"), Window(2, "Music"));
        desktop.BeforeClose = () => desktop.Windows[changed - 1] = Window(changed, "New folder");
        var result = await new MergeService(desktop).GatherAsync(true);
        Assert.Equal(1, result.Kept);
        Assert.Equal(0, result.Closed);
    }

    [Fact]
    public void HandlesCanChangeWhenExplorerSelectsTheNextTab()
    {
        var original = Window(1, "A", "B", "C", "C");
        var remaining = new ExplorerWindow(1, [new(478, "C"), new(491, "B")], true);
        Assert.True(WindowsExplorerDesktop.IsVerifiedRemainder(remaining, original));
        Assert.False(WindowsExplorerDesktop.IsVerifiedRemainder(Window(1, "B", "B"), original));
        Assert.False(WindowsExplorerDesktop.IsVerifiedRemainder(Window(1, "New"), original));
    }

    [Fact]
    public async Task EmptyDesktopDoesNothing()
    {
        var desktop = new Desktop();
        Assert.False((await new MergeService(desktop).GatherAsync(true)).Gathered);
        Assert.Empty(desktop.Windows);
    }

    private static ExplorerWindow Window(int frame, params string[] locations) =>
        new(frame, locations.Select((s, i) => new ExplorerTab(100 * frame + i, s)).ToList(), true);

    private sealed class Desktop(params ExplorerWindow[] windows) : IExplorerDesktop
    {
        internal List<ExplorerWindow> Windows { get; } = [.. windows];
        internal List<string> OpenedTabs { get; } = [];
        internal bool WrongDestination { get; init; }
        internal bool ReuseSource { get; init; }
        internal int CloseAttempts { get; private set; }
        internal int CreatedWindows { get; private set; }
        internal Action? BeforeClose { get; set; }

        public IReadOnlyList<ExplorerWindow> ReadWindows() => Windows.ToList();

        public Task<ExplorerWindow> CreateWindowAsync(string firstLocation)
        {
            CreatedWindows++;
            if (ReuseSource) return Task.FromResult(Windows[0]);
            var target = Window(99, firstLocation);
            Windows.Add(target);
            return Task.FromResult(target);
        }

        public Task<ExplorerWindow> AddTabsAsync(ExplorerWindow target, IReadOnlyList<string> locations)
        {
            OpenedTabs.AddRange(locations);
            var tabs = target.Tabs.ToList();
            foreach (var location in locations)
                tabs.Add(new(11000 + tabs.Count, WrongDestination ? "Other" : location));
            var next = target with { Tabs = tabs };
            Windows[Windows.IndexOf(target)] = next;
            return Task.FromResult(next);
        }

        public async Task<int> CloseSourcesAsync(IReadOnlyList<ExplorerWindow> sources, ExplorerWindow target) =>
            (await Task.WhenAll(sources.Select(w => CloseIfUnchangedAsync(w, target)))).Count(x => x);

        private Task<bool> CloseIfUnchangedAsync(ExplorerWindow source, ExplorerWindow target)
        {
            CloseAttempts++;
            BeforeClose?.Invoke();
            BeforeClose = null;
            var destination = Windows.Single(w => w.Handle == target.Handle);
            var current = Windows.Single(w => w.Handle == source.Handle);
            return Task.FromResult(destination.Matches(target) && current.Matches(source) && Windows.Remove(current));
        }
    }
}

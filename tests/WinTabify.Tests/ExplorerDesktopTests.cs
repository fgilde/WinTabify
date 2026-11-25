using System.Windows.Threading;
using System.Diagnostics;
using WinTabify.Explorer;
using Xunit;
using Xunit.Abstractions;

namespace WinTabify.Tests;

public sealed class ExplorerDesktopTests(ITestOutputHelper output)
{
    [InteractiveExplorerFact]
    public Task GathersOnlyTestWindowsInBothModes()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    for (int i = 0; i < 3; i++) await ExerciseExplorerAsync();
                    completion.SetResult();
                }
                catch (Exception ex) { completion.SetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private async Task ExerciseExplorerAsync()
    {
        var directory = Directory.CreateTempSubdirectory("WinTabify-Test-");
        var paths = new[] { "Work", "Music", "Photos", "A", "B", "C", "D" }
            .Select(name => directory.CreateSubdirectory(name).FullName).ToArray();
        var desktop = new TestWindows(paths, output);
        try
        {
            var group = await desktop.CreateWindowAsync(paths[0]);
            await desktop.AddTabsAsync(group, [paths[1]]);
            await desktop.CreateWindowAsync(paths[1]);
            await desktop.CreateWindowAsync(paths[2]);

            var service = new MergeService(desktop);
            var timer = Stopwatch.StartNew();
            var first = await service.GatherAsync(false);
            output.WriteLine($"Two single windows: {timer.ElapsedMilliseconds} ms");
            Assert.Equal(1, first.Closed);
            Assert.Equal(2, desktop.ReadWindows().Count);
            Assert.Contains(desktop.ReadWindows(), w => w.Handle == group.Handle);
            var overlapping = await desktop.CreateWindowAsync(paths[1]);
            await desktop.AddTabsAsync(overlapping, [paths[0], paths[1], paths[3], paths[4]]);
            var extras = await desktop.CreateWindowAsync(paths[5]);
            await desktop.AddTabsAsync(extras, [paths[6]]);
            timer.Restart();
            var second = await service.GatherAsync(true);
            output.WriteLine($"Four windows including a five-tab source with duplicates: {timer.ElapsedMilliseconds} ms, {second}");
            Assert.Equal(3, second.Closed);
            var target = Assert.Single(desktop.ReadWindows());
            Assert.True(target.ContainsExactly(paths));
            var duplicates = await desktop.CreateWindowAsync(paths[3]);
            await desktop.AddTabsAsync(duplicates, [paths[0], paths[3]]);
            timer.Restart();
            var last = await service.GatherAsync(true);
            output.WriteLine($"Fully represented three-tab source: {timer.ElapsedMilliseconds} ms, {last}");
            Assert.Equal(1, last.Closed);
            Assert.True(Assert.Single(desktop.ReadWindows()).Matches(target));
        }
        finally
        {
            foreach (var window in desktop.ReadWindows())
            {
                if (window.Complete && window.Tabs.All(t => paths.Contains(t.Location)))
                    NativeShell.PostMessage(window.Handle, 0x0010, 0, 0);
            }
            foreach (var path in paths) Directory.Delete(path);
            directory.Delete();
        }
    }

    private sealed class TestWindows(string[] paths, ITestOutputHelper output) : IExplorerDesktop
    {
        private readonly WindowsExplorerDesktop desktop = new();
        private readonly HashSet<nint> owned = [];

        public IReadOnlyList<ExplorerWindow> ReadWindows() =>
            desktop.ReadWindows().Where(w => owned.Contains(w.Handle)).ToArray();

        public async Task<ExplorerWindow> CreateWindowAsync(string firstLocation)
        {
            Assert.Contains(firstLocation, paths);
            var created = await desktop.CreateWindowAsync(firstLocation);
            owned.Add(created.Handle);
            return created;
        }

        public Task<ExplorerWindow> AddTabsAsync(ExplorerWindow target, IReadOnlyList<string> locations)
        {
            Assert.Contains(target.Handle, owned);
            Assert.All(locations, location => Assert.Contains(location, paths));
            return desktop.AddTabsAsync(target, locations);
        }

        public async Task<bool> CloseIfUnchangedAsync(ExplorerWindow source, ExplorerWindow target)
        {
            Assert.Contains(source.Handle, owned);
            Assert.Contains(target.Handle, owned);
            Assert.All(source.Tabs, tab => Assert.Contains(tab.Location, paths));
            bool closed = await desktop.CloseIfUnchangedAsync(source, target);
            if (closed) owned.Remove(source.Handle);
            else
            {
                var remaining = ReadWindows().SingleOrDefault(w => w.Handle == source.Handle);
                output.WriteLine($"Frame {source.Handle}: initially {source.Tabs.Count} tabs, now {remaining?.Tabs.Count}, " +
                    $"enabled={NativeShell.IsWindowEnabled(source.Handle)}, complete={remaining?.Complete}, " +
                    $"paths={string.Join(", ", remaining?.Tabs.Select(t => t.Location) ?? [])}.");
            }
            return closed;
        }
    }

    private sealed class InteractiveExplorerFactAttribute : FactAttribute
    {
        public InteractiveExplorerFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("WINTABIFY_EXPLORER_TEST") != "1")
                Skip = "Requires an interactive Windows 11 desktop. Set WINTABIFY_EXPLORER_TEST=1 to run.";
        }
    }
}

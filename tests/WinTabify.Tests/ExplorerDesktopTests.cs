using System.Windows.Threading;
using WinTabify.Explorer;
using Xunit;

namespace WinTabify.Tests;

public sealed class ExplorerDesktopTests
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
                try { await ExerciseExplorerAsync(); completion.SetResult(); }
                catch (Exception ex) { completion.SetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static async Task ExerciseExplorerAsync()
    {
        var directory = Directory.CreateTempSubdirectory("WinTabify-Test-");
        var paths = new[] { "Work", "Music", "Photos" }
            .Select(name => directory.CreateSubdirectory(name).FullName).ToArray();
        var desktop = new TestWindows(paths);
        try
        {
            var group = await desktop.CreateWindowAsync(paths[0]);
            await desktop.AddTabAsync(group, paths[1]);
            await desktop.CreateWindowAsync(paths[1]);
            await desktop.CreateWindowAsync(paths[2]);

            var service = new MergeService(desktop);
            var first = await service.GatherAsync(false);
            Assert.Equal(2, first.Closed);
            Assert.Equal(2, desktop.ReadWindows().Count);
            Assert.Contains(desktop.ReadWindows(), w => w.Handle == group.Handle);
            var second = await service.GatherAsync(true);
            Assert.Equal(2, second.Closed);
            Assert.True(Assert.Single(desktop.ReadWindows()).ContainsExactly(paths));
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

    private sealed class TestWindows(string[] paths) : IExplorerDesktop
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

        public Task<ExplorerWindow> AddTabAsync(ExplorerWindow target, string location)
        {
            Assert.Contains(target.Handle, owned);
            Assert.Contains(location, paths);
            return desktop.AddTabAsync(target, location);
        }

        public Task<bool> CloseIfUnchangedAsync(ExplorerWindow source, ExplorerWindow target)
        {
            Assert.Contains(source.Handle, owned);
            Assert.Contains(target.Handle, owned);
            Assert.All(source.Tabs, tab => Assert.Contains(tab.Location, paths));
            return desktop.CloseIfUnchangedAsync(source, target);
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

using System.Windows.Threading;

namespace WinTabify.Explorer;

internal static class ShellWorker
{
    internal static Task<MergeResult> GatherAsync(bool includeTabbed)
    {
        var result = new TaskCompletionSource<MergeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.InvokeAsync(async () =>
            {
                try { result.SetResult(await new MergeService(new WindowsExplorerDesktop()).GatherAsync(includeTabbed)); }
                catch (Exception ex) { result.SetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true, Name = "WinTabify Explorer" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return result.Task;
    }
}

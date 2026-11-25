using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;

namespace WinTabify.Explorer;

internal sealed class ShellViews : IDisposable
{
    private readonly object collection = Activator.CreateInstance(
        Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"), true)!)!;
    private readonly Dictionary<nint, object> browsers = [];

    internal ShellViews()
    {
        try
        {
            dynamic windows = collection;
            int count = windows.Count;
            for (int i = 0; i < count; i++)
            {
                object? browser = null;
                try
                {
                    browser = windows.Item(i);
                    if (browser is null) continue;
                    nint handle = NativeShell.TabHandle(browser);
                    if (handle != 0 && browsers.TryAdd(handle, browser))
                        browser = null;
                }
                catch (Exception ex) when (IsUnavailable(ex)) { }
                finally { if (browser != null) Marshal.ReleaseComObject(browser); }
            }
        }
        catch { Dispose(); throw; }
    }

    internal static bool IsUnavailable(Exception ex) =>
        ex is COMException or InvalidCastException or RuntimeBinderException or UnauthorizedAccessException;

    internal ExplorerWindow Read(nint frame)
    {
        var handles = NativeShell.TabHandles(frame);
        var tabs = new List<ExplorerTab>(handles.Count);
        foreach (var handle in handles)
        {
            if (ReadTab(handle) is { } tab) tabs.Add(tab);
        }
        return new(frame, tabs, handles.Count > 0 && tabs.Count == handles.Count);
    }

    internal ExplorerTab? ReadTab(nint handle)
    {
        if (!browsers.TryGetValue(handle, out var browser)) return null;
        try { return GetLocation(browser) is { } location ? new(handle, location) : null; }
        catch (Exception ex) when (IsUnavailable(ex)) { return null; }
    }

    private static string? GetLocation(dynamic browser)
    {
        if ((bool)browser.Busy) return null;
        object? document = null, folder = null, item = null;
        try
        {
            document = browser.Document;
            if (document == null) return null;
            folder = ((dynamic)document).Folder;
            item = ((dynamic)folder).Self;
            string? path = ((dynamic)item).Path;
            if (string.IsNullOrWhiteSpace(path)) return null;
            if (Path.IsPathFullyQualified(path))
                return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (path.StartsWith("::", StringComparison.Ordinal)) return "shell:" + path;
            if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("search-ms:", StringComparison.OrdinalIgnoreCase)) return path;
            return null;
        }
        finally
        {
            if (item != null) Marshal.ReleaseComObject(item);
            if (folder != null) Marshal.ReleaseComObject(folder);
            if (document != null) Marshal.ReleaseComObject(document);
        }
    }

    internal void Navigate(nint frame, ExplorerTab tab, string location)
    {
        if (NativeShell.GetAncestor(tab.Handle, 2) != frame ||
            !browsers.TryGetValue(tab.Handle, out var browser) || GetLocation(browser) != tab.Location)
            throw new ExplorerChangedException();
        ((dynamic)browser).Navigate2(location);
    }

    internal bool RequestClose(ExplorerWindow source)
    {
        if (source.Tabs.Count == 1) return NativeShell.RequestWindowClose(source.Handle);
        bool requested = false;
        foreach (var tab in source.Tabs.OrderBy(t => NativeShell.IsWindowVisible(t.Handle)))
        {
            if (NativeShell.GetAncestor(tab.Handle, 2) != source.Handle ||
                !browsers.TryGetValue(tab.Handle, out var browser)) continue;
            try
            {
                if (GetLocation(browser) != tab.Location) continue;
                ((dynamic)browser).Quit();
                requested = true;
            }
            catch (Exception ex) when (IsUnavailable(ex)) { }
        }
        return requested || !NativeShell.IsWindow(source.Handle);
    }

    public void Dispose()
    {
        foreach (var browser in browsers.Values) Marshal.ReleaseComObject(browser);
        Marshal.ReleaseComObject(collection);
    }
}

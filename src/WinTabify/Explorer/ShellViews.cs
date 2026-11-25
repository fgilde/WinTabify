using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;

namespace WinTabify.Explorer;

internal sealed class ShellViews : IDisposable
{
    private readonly object collection = Activator.CreateInstance(
        Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"), true)!)!;
    private readonly Dictionary<nint, object> browsers = [];
    private bool indexed;
    private long lastMiss;

    private object? Find(nint handle)
    {
        if (browsers.TryGetValue(handle, out var cached)) return cached;
        if (lastMiss != 0 && Environment.TickCount64 - lastMiss < 16) return null;
        dynamic windows = collection;
        int count = windows.Count;
        for (int i = count - 1; i >= 0; i--)
        {
            object? browser = null;
            try
            {
                browser = windows.Item(i);
                if (browser == null) continue;
                nint registered = NativeShell.TabHandle(browser);
                if (registered == 0) continue;
                if (browsers.TryAdd(registered, browser)) browser = null;
                if (indexed && registered == handle) return browsers[handle];
            }
            catch (Exception ex) when (IsUnavailable(ex)) { }
            finally { if (browser != null) Marshal.ReleaseComObject(browser); }
        }
        indexed = true;
        if (!browsers.ContainsKey(handle)) lastMiss = Environment.TickCount64;
        return browsers.GetValueOrDefault(handle);
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
        try { return Find(handle) is { } browser && GetLocation(browser) is { } location ? new(handle, location) : null; }
        catch (Exception ex) when (IsUnavailable(ex)) { return null; }
    }

    private static string? GetLocation(dynamic browser)
    {
        if ((bool)browser.Busy) return null;
        string? url = browser.LocationURL;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsFile &&
            uri.Fragment.Length == 0 && uri.Query.Length == 0)
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(uri.LocalPath));
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
            Find(tab.Handle) is not { } browser || GetLocation(browser) != tab.Location)
            throw new ExplorerChangedException();
        ((dynamic)browser).Navigate2(location);
    }

    internal bool TryNavigateNewTab(nint frame, ExplorerTab tab)
    {
        if (NativeShell.GetAncestor(tab.Handle, 2) != frame) throw new ExplorerChangedException();
        try
        {
            if (Find(tab.Handle) is not { } browser) return false;
            ((dynamic)browser).Navigate2(tab.Location);
            return true;
        }
        catch (COMException ex) when (ex.HResult is unchecked((int)0x80010001) or unchecked((int)0x8001010A))
        {
            return false;
        }
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

    internal bool RequestTabClose(nint frame, ExplorerTab tab)
    {
        if (NativeShell.GetAncestor(tab.Handle, 2) != frame || Find(tab.Handle) is not { } browser ||
            GetLocation(browser) != tab.Location) return false;
        ((dynamic)browser).Quit();
        return true;
    }

    public void Dispose()
    {
        foreach (var browser in browsers.Values) Marshal.ReleaseComObject(browser);
        Marshal.ReleaseComObject(collection);
    }
}

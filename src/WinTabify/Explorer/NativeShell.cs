using System.Runtime.InteropServices;
using System.Text;

namespace WinTabify.Explorer;

internal static class NativeShell
{
    private delegate bool EnumWindowsCallback(nint window, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder buffer, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(nint parent, nint previous, string className, string? title);
    [DllImport("user32.dll")]
    internal static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")]
    internal static extern bool IsWindowEnabled(nint window);
    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")]
    internal static extern bool IsWindow(nint window);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    internal static bool RequestWindowClose(nint frame) => PostMessage(frame, 0x0010, 0, 0);

    internal static List<nint> Frames()
    {
        var frames = new List<nint>();
        EnumWindows((window, _) =>
        {
            var name = new StringBuilder(64);
            GetClassName(window, name, name.Capacity);
            if (name.ToString() == "CabinetWClass")
                frames.Add(window);
            return true;
        }, 0);
        return frames;
    }

    internal static List<nint> TabHandles(nint frame)
    {
        var tabs = new List<nint>();
        nint tab = 0;
        while ((tab = FindWindowEx(frame, tab, "ShellTabWindowClass", null)) != 0)
            tabs.Add(tab);
        return tabs;
    }

    private static readonly Guid BrowserId = new("000214E2-0000-0000-C000-000000000046");
    private static readonly Guid TopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");

    internal static nint TabHandle(object browser)
    {
        var provider = (IShellServiceProvider)browser;
        int hr = provider.QueryService(in BrowserId, in BrowserId, out var shellBrowser);
        if (hr < 0)
            Marshal.ThrowExceptionForHR(provider.QueryService(in TopLevelBrowser, in BrowserId, out shellBrowser));
        try
        {
            shellBrowser.GetWindow(out var tab);
            return tab;
        }
        finally { Marshal.ReleaseComObject(shellBrowser); }
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellServiceProvider
    {
        [PreserveSig]
        int QueryService(in Guid service, in Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellBrowser browser);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow(out nint window);
    }
}

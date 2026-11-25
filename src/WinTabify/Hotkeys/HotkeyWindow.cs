using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace WinTabify.Hotkeys;

internal sealed class HotkeyWindow : IDisposable
{
    private readonly HwndSource source;
    private readonly Action pressed;
    private int id;
    internal Shortcut? Active { get; private set; }

    internal HotkeyWindow(Action pressed)
    {
        this.pressed = pressed;
        source = new HwndSource(new HwndSourceParameters("WinTabify.Hotkey")
        {
            ParentWindow = new nint(-3), WindowStyle = 0, Width = 0, Height = 0
        });
        source.AddHook(ProcessMessage);
    }

    internal bool TryApply(Shortcut shortcut, Action? persist = null)
    {
        if (!shortcut.IsValid) return false;
        if (shortcut == Active) { persist?.Invoke(); return true; }
        int candidate = id == 1 ? 2 : 1;
        if (!RegisterHotKey(source.Handle, candidate, (uint)shortcut.Modifiers | 0x4000, (uint)shortcut.Key))
            return false;
        try { persist?.Invoke(); }
        catch { UnregisterHotKey(source.Handle, candidate); throw; }
        if (id != 0) UnregisterHotKey(source.Handle, id);
        id = candidate;
        Active = shortcut;
        return true;
    }

    private nint ProcessMessage(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0312 && wParam == id)
        {
            handled = true;
            pressed();
        }
        return 0;
    }

    public void Dispose()
    {
        if (id != 0) UnregisterHotKey(source.Handle, id);
        source.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(nint window, int id);
}

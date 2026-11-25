using System.Windows.Forms;
using Nextended.UI.Input;

namespace WinTabify.Hotkeys;

[Flags]
internal enum Modifiers : uint { Alt = 1, Ctrl = 2, Shift = 4, Win = 8 }

internal sealed record Shortcut(Modifiers Modifiers, Keys Key)
{
    internal static readonly Shortcut[] Defaults = [
        new(Modifiers.Ctrl | Modifiers.Win, Keys.E),
        new(Modifiers.Alt | Modifiers.Win, Keys.E),
        new(Modifiers.Ctrl | Modifiers.Shift, Keys.F11)
    ];

    private static Modifiers Modifier(Keys key) => key switch
    {
        Keys.LControlKey or Keys.RControlKey or Keys.ControlKey => Modifiers.Ctrl,
        Keys.LMenu or Keys.RMenu or Keys.Menu => Modifiers.Alt,
        Keys.LShiftKey or Keys.RShiftKey or Keys.ShiftKey => Modifiers.Shift,
        Keys.LWin or Keys.RWin => Modifiers.Win,
        _ => 0
    };

    internal bool IsValid => Modifiers != 0 && ((uint)Modifiers & ~15u) == 0 &&
        (int)Key is > 7 and < 255 && Key != Keys.F12 && Modifier(Key) == 0 && Enum.IsDefined(Key);

    internal static Shortcut? FromBinding(StoredInputBinding? binding)
    {
        if (binding == null) return null;
        Modifiers modifiers = 0;
        Keys key = Keys.None;
        foreach (var part in binding.Flatten())
        {
            if (part.KeyboardKey is not { } pressed) return null;
            var modifier = Modifier(pressed);
            if (modifier != 0) modifiers |= modifier;
            else if (key == Keys.None) key = pressed;
            else return null;
        }
        var shortcut = new Shortcut(modifiers, key);
        return shortcut.IsValid ? shortcut : null;
    }

    internal StoredInputBinding ToBinding()
    {
        var parts = new List<StoredInputBinding>();
        if (Modifiers.HasFlag(Modifiers.Ctrl)) parts.Add(Keys.LControlKey);
        if (Modifiers.HasFlag(Modifiers.Alt)) parts.Add(Keys.LMenu);
        if (Modifiers.HasFlag(Modifiers.Shift)) parts.Add(Keys.LShiftKey);
        if (Modifiers.HasFlag(Modifiers.Win)) parts.Add(Keys.LWin);
        parts.Add(Key);
        return StoredInputBinding.Combo(parts);
    }
}

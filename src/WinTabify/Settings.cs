using System.Globalization;
using System.Text.Json;
using WinTabify.Hotkeys;

namespace WinTabify;

internal sealed record Settings(Shortcut Shortcut, bool IncludeTabbed = true, bool AutomaticUpdates = true)
{
    internal static Settings Default => new(Shortcut.Defaults[0]);

    internal static Settings? Load(string file)
    {
        if (!File.Exists(file)) return null;
        var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(file));
        if (settings?.Shortcut?.IsValid != true) throw new InvalidDataException();
        return settings;
    }

    internal void Save(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string temporary = file + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this));
            File.Move(temporary, file, true);
        }
        finally { File.Delete(temporary); }
    }
}

internal static class Copy
{
    internal static string T(string de, string en) =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? de : en;
}

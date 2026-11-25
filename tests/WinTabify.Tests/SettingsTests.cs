using System.Windows.Forms;
using Nextended.UI.Input;
using WinTabify.Explorer;
using WinTabify.Hotkeys;
using Xunit;
using Shortcut = WinTabify.Hotkeys.Shortcut;

namespace WinTabify.Tests;

public sealed class SettingsTests
{
    [Fact]
    public void DefaultShortcutsRoundTripThroughRecorder()
    {
        foreach (var shortcut in Shortcut.Defaults)
            Assert.Equal(shortcut, Shortcut.FromBinding(shortcut.ToBinding()));
    }

    [Fact]
    public void RejectsMouseAndChordsWithTwoNonModifierKeys()
    {
        Assert.Null(Shortcut.FromBinding(StoredInputBinding.Combo([Keys.LControlKey, MouseButtons.Left])));
        Assert.Null(Shortcut.FromBinding(StoredInputBinding.Combo([Keys.LControlKey, Keys.E, Keys.A])));
        Assert.Null(Shortcut.FromBinding(new StoredInputBinding(Keys.E)));
    }

    [Fact]
    public void PersistsAndReplacesBothPreferences()
    {
        var dir = Directory.CreateTempSubdirectory();
        string file = Path.Combine(dir.FullName, "settings.json");
        try
        {
            Assert.Null(Settings.Load(file));
            Settings.Default.Save(file);
            var next = new Settings(Shortcut.Defaults[1], false);
            next.Save(file);
            Assert.Equal(next, Settings.Load(file));
            Assert.False(File.Exists(file + ".tmp"));
        }
        finally { File.Delete(file); dir.Delete(); }
    }

    [Fact]
    public void DistinguishesVirtualSearchesAndConfirmsCaseVariantsOnDisk()
    {
        var dir = Directory.CreateTempSubdirectory("WinTabify-");
        try
        {
            var comparer = FolderLocationComparer.Instance;
            Assert.True(comparer.Equals(dir.FullName, dir.FullName.ToUpperInvariant()));
            Assert.False(comparer.Equals("search-ms:query=Foo", "search-ms:query=foo"));
        }
        finally { dir.Delete(); }
    }
}

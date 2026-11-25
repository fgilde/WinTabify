using System.Windows;
using System.Windows.Input;
using Nextended.UI.Input;
using WinTabify.Hotkeys;
using static WinTabify.Copy;

namespace WinTabify;

public partial class SettingsWindow : Window
{
    private readonly InputBindingManager manager = new(enableGamepad: false);
    private readonly Func<Settings, bool> save;

    internal SettingsWindow(Settings settings, Func<Settings, bool> save, string? status)
    {
        InitializeComponent();
        this.save = save;
        Title = T("WinTabify · Einstellungen", "WinTabify · Settings");
        Recorder.BindingManager = manager;
        Recorder.Text = T("Tastenkürzel", "Keyboard shortcut");
        Recorder.KeyBind = settings.Shortcut.ToBinding();
        Hint.Text = T("Kürzel anklicken und die neue Kombination drücken.", "Click the shortcut and press the new combination.");
        Include.Content = T("Fenster mit mehreren Tabs einbeziehen", "Include windows with multiple tabs");
        Include.IsChecked = settings.IncludeTabbed;
        Description.Text = T("Ausgeschaltet bleiben bestehende Tab-Gruppen offen. Nur einzelne Fenster werden gesammelt.",
            "Turn this off to keep existing tab groups open and gather only single-tab windows.");
        Status.Text = status;
        CancelButton.Content = T("Abbrechen", "Cancel");
        SaveButton.Content = T("Speichern", "Save");
    }

    private void RecorderKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Space or Key.Enter)) return;
        e.Handled = true;
        Recorder.RecordNewBinding();
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        var shortcut = Shortcut.FromBinding(Recorder.KeyBind);
        if (shortcut == null)
        {
            Status.Text = T("Wähle eine Taste zusammen mit Strg, Alt, Umschalt oder Win.",
                "Choose one key together with Ctrl, Alt, Shift or Win.");
            return;
        }
        try
        {
            if (save(new(shortcut, Include.IsChecked == true))) { Close(); return; }
            Status.Text = T("Windows konnte dieses Kürzel nicht registrieren. Wähle eine andere Kombination.",
                "Windows could not register this shortcut. Choose another combination.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status.Text = T("Speichern fehlgeschlagen. ", "Could not save settings. ") + ex.Message;
        }
    }

    private void CancelClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        Recorder.Dispose();
        manager.Dispose();
        base.OnClosed(e);
    }
}

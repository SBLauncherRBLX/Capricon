using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Capricon.Services;
using Microsoft.Win32;

namespace Capricon;

public partial class SettingsWindow : Window
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings, IEnumerable<string> voices)
    {
        _settings = settings;
        InitializeComponent();

        ApiKey.Password = settings.ApiKey;
        Model.Text = settings.Model;
        Select(Effort, settings.Effort);
        VoiceName.Items.Add(new ComboBoxItem { Content = "Автовыбор", Tag = "" });
        foreach (var v in voices) VoiceName.Items.Add(new ComboBoxItem { Content = v, Tag = v });
        VoiceName.SelectedItem = VoiceName.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == settings.VoiceName) ?? VoiceName.Items[0];
        Pitch.Value = settings.VoicePitch;
        Rate.Value = settings.VoiceRate;
        Muted.IsChecked = settings.Muted;
        Scale.Value = settings.Scale;
        FollowCursor.IsChecked = settings.FollowCursor;
        ReactClipboard.IsChecked = settings.ReactClipboard;
        ReactIdle.IsChecked = settings.ReactIdle;
        ReactSystem.IsChecked = settings.ReactSystem;
        Chatter.IsChecked = settings.Chatter;
        using var run = Registry.CurrentUser.OpenSubKey(RunKey);
        Autostart.IsChecked = run?.GetValue("Capricon") is not null;
    }

    public event Action? Saved;

    static void Select(ComboBox box, string value) =>
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Content == value) ?? box.Items[0];

    void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.ApiKey = ApiKey.Password;
        _settings.Model = string.IsNullOrWhiteSpace(Model.Text) ? "claude-opus-5-5" : Model.Text.Trim();
        _settings.Effort = (string)((ComboBoxItem)Effort.SelectedItem).Content;
        _settings.VoiceName = (string)((ComboBoxItem)VoiceName.SelectedItem).Tag;
        _settings.VoicePitch = (int)Pitch.Value;
        _settings.VoiceRate = (int)Rate.Value;
        _settings.Muted = Muted.IsChecked == true;
        _settings.Scale = Scale.Value;
        _settings.FollowCursor = FollowCursor.IsChecked == true;
        _settings.ReactClipboard = ReactClipboard.IsChecked == true;
        _settings.ReactIdle = ReactIdle.IsChecked == true;
        _settings.ReactSystem = ReactSystem.IsChecked == true;
        _settings.Chatter = Chatter.IsChecked == true;
        _settings.Save();

        using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (Autostart.IsChecked == true) run.SetValue("Capricon", $"\"{Environment.ProcessPath}\"");
            else run.DeleteValue("Capricon", throwOnMissingValue: false);
        }

        Saved?.Invoke();
        Close();
    }

    void OpenNotes_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppSettings.Folder);
        Process.Start(new ProcessStartInfo(AppSettings.Folder) { UseShellExecute = true });
    }
}

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LimboTranslate.Core;
using LimboTranslate.Providers;

namespace LimboTranslate.UI;

public partial class SettingsWindow : Window
{
    private static SettingsWindow? _instance;

    private readonly List<CheckBox> _providerChecks = new();

    public SettingsWindow()
    {
        InitializeComponent();

        Loaded += (_, _) => LoadSettings();
        Closed += (_, _) =>
        {
            if (ReferenceEquals(_instance, this))
                _instance = null;
        };
    }

    public event Action? SettingsChanged;

    public static SettingsWindow ShowWindow(Action? settingsChanged = null)
    {
        if (_instance is null)
        {
            _instance = new SettingsWindow();
            if (settingsChanged is not null)
                _instance.SettingsChanged += settingsChanged;

            _instance.Show();
        }
        else
        {
            if (_instance.WindowState == WindowState.Minimized)
                _instance.WindowState = WindowState.Normal;

            _instance.Activate();
        }

        return _instance;
    }

    private void LoadSettings()
    {
        AppSettings settings = App.Settings;

        var languages = Languages.All.ToList();

        SourceLanguageBox.SelectedValuePath = "Key";
        SourceLanguageBox.ItemsSource = languages;
        SourceLanguageBox.SelectedValue = settings.SourceLanguage;
        if (SourceLanguageBox.SelectedItem is null)
            SourceLanguageBox.SelectedValue = "auto";

        TargetLanguageBox.SelectedValuePath = "Key";
        TargetLanguageBox.ItemsSource = languages.Where(l => l.Key != "auto").ToList();
        TargetLanguageBox.SelectedValue = settings.TargetLanguage;
        if (TargetLanguageBox.SelectedItem is null)
            TargetLanguageBox.SelectedValue = "ru";

        _providerChecks.Clear();
        ProvidersPanel.Children.Clear();
        foreach (ITranslationProvider provider in ProviderRegistry.All)
        {
            var check = new CheckBox
            {
                Content = provider.Name,
                Tag = provider.Name,
                IsChecked = settings.EnabledProviders.Contains(provider.Name, StringComparer.OrdinalIgnoreCase)
            };
            check.Checked += ProviderCheck_Changed;
            check.Unchecked += ProviderCheck_Changed;

            _providerChecks.Add(check);
            ProvidersPanel.Children.Add(check);
        }

        RefreshActiveProviders(settings.ActiveProvider);

        HotkeyTranslateBox.Text = settings.HotkeyTranslate;
        HotkeySpeakBox.Text = settings.HotkeySpeak;
        HotkeyMainWindowBox.Text = settings.HotkeyMainWindow;
        PopupEnabledCheck.IsChecked = settings.PopupEnabled;
        MouseSelectionCheck.IsChecked = settings.TranslateOnMouseSelection;

        AutoStartCheck.IsChecked = AutoStart.IsEnabled() || settings.StartWithWindows;
        HistoryLimitBox.Text = settings.HistoryLimit.ToString(CultureInfo.InvariantCulture);
    }

    private void ProviderCheck_Changed(object sender, RoutedEventArgs e)
    {
        RefreshActiveProviders(ActiveProviderBox.SelectedItem as string);
    }

    private void RefreshActiveProviders(string? preferred)
    {
        List<string> enabled = EnabledProviderNames();
        if (enabled.Count == 0)
            enabled = ProviderRegistry.All.Select(p => p.Name).ToList();

        ActiveProviderBox.ItemsSource = enabled;
        ActiveProviderBox.SelectedItem = enabled.FirstOrDefault(
            n => n.Equals(preferred, StringComparison.OrdinalIgnoreCase)) ?? enabled[0];
    }

    private List<string> EnabledProviderNames() =>
        _providerChecks
            .Where(c => c.IsChecked == true)
            .Select(c => (string)c.Tag)
            .ToList();

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        if (sender is not TextBox box)
            return;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.Escape)
        {
            box.Text = string.Empty;
            return;
        }

        if (IsModifier(key))
            return;

        string? token = KeyToken(key);
        if (token is null)
            return;

        ModifierKeys modifiers = Keyboard.Modifiers;
        var parts = new List<string>();

        if (modifiers.HasFlag(ModifierKeys.Control))
            parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Shift))
            parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Alt))
            parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Windows))
            parts.Add("Win");

        if (parts.Count == 0)
            return;

        parts.Add(token);
        box.Text = string.Join("+", parts);
    }

    private static bool IsModifier(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin
            or Key.System;

    private static string? KeyToken(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
            return key.ToString();

        if (key is >= Key.D0 and <= Key.D9)
            return ((char)('0' + (key - Key.D0))).ToString();

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
            return ((char)('0' + (key - Key.NumPad0))).ToString();

        if (key is >= Key.F1 and <= Key.F12)
            return key.ToString();

        return key switch
        {
            Key.Space => "Space",
            Key.Enter => "Enter",
            Key.Tab => "Tab",
            Key.Insert => "Insert",
            Key.Delete => "Delete",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Left => "Left",
            Key.Right => "Right",
            _ => null
        };
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        AppSettings settings = App.Settings;

        if (SourceLanguageBox.SelectedValue is string source && !string.IsNullOrWhiteSpace(source))
            settings.SourceLanguage = source;

        if (TargetLanguageBox.SelectedValue is string target && !string.IsNullOrWhiteSpace(target))
            settings.TargetLanguage = target;

        List<string> enabled = EnabledProviderNames();
        if (enabled.Count == 0)
            enabled = ProviderRegistry.All.Select(p => p.Name).ToList();

        settings.EnabledProviders = enabled;
        settings.ActiveProvider = ActiveProviderBox.SelectedItem as string ?? enabled[0];

        settings.HotkeyTranslate = Validate(HotkeyTranslateBox.Text, settings.HotkeyTranslate);
        settings.HotkeySpeak = Validate(HotkeySpeakBox.Text, settings.HotkeySpeak);
        settings.HotkeyMainWindow = Validate(HotkeyMainWindowBox.Text, settings.HotkeyMainWindow);

        settings.PopupEnabled = PopupEnabledCheck.IsChecked == true;
        settings.TranslateOnMouseSelection = MouseSelectionCheck.IsChecked == true;
        settings.HotkeyPopup = "DoubleCtrl";

        if (int.TryParse(HistoryLimitBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int limit) && limit > 0)
            settings.HistoryLimit = limit;

        bool autoStart = AutoStartCheck.IsChecked == true;
        settings.StartWithWindows = autoStart;
        AutoStart.Set(autoStart);

        SettingsStore.Save(settings);
        SettingsChanged?.Invoke();

        Close();
    }

    private static string Validate(string? candidate, string fallback)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return fallback;

        var (modifiers, virtualKey) = HotkeyParser.Parse(candidate);
        return modifiers != 0 && virtualKey != 0 ? candidate.Trim() : fallback;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

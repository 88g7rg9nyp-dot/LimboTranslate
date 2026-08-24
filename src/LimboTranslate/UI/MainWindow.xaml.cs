using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LimboTranslate.Core;
using LimboTranslate.Data;
using LimboTranslate.Providers;

namespace LimboTranslate.UI;

public partial class MainWindow : Window
{
    private static MainWindow? _instance;

    private readonly ObservableCollection<ProviderTab> _tabs = new();
    private readonly SpeechService _speech = new();

    private CancellationTokenSource? _cts;
    private bool _suppressLanguageEvents;

    public MainWindow()
    {
        InitializeComponent();

        ResultsTabs.ItemsSource = _tabs;

        _suppressLanguageEvents = true;
        SourceCombo.ItemsSource = Languages.All.ToList();
        TargetCombo.ItemsSource = Languages.All.Where(p => p.Key != "auto").ToList();
        SourceCombo.SelectedValue = Languages.All.ContainsKey(App.Settings.SourceLanguage) ? App.Settings.SourceLanguage : "auto";
        TargetCombo.SelectedValue = Languages.All.ContainsKey(App.Settings.TargetLanguage) && App.Settings.TargetLanguage != "auto"
            ? App.Settings.TargetLanguage
            : "ru";
        _suppressLanguageEvents = false;

        UpdateSwapState();
        RebuildTabs();
        UpdateCharCount();
    }

    public static MainWindow ShowWindow()
    {
        _instance ??= new MainWindow();

        if (!_instance.IsVisible)
            _instance.Show();

        if (_instance.WindowState == WindowState.Minimized)
            _instance.WindowState = WindowState.Normal;

        _instance.Activate();
        _instance.InputBox.Focus();
        return _instance;
    }

    public static void ShowWithText(string text)
    {
        MainWindow window = ShowWindow();
        window.TranslateText(text);
    }

    public void TranslateText(string text)
    {
        InputBox.Text = text ?? string.Empty;
        Translate();
    }

    private void Translate()
    {
        string text = InputBox.Text.Trim();

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        RebuildTabs();

        if (text.Length == 0)
        {
            foreach (ProviderTab tab in _tabs)
                tab.Reset();

            StatusText.Text = "Готово";
            return;
        }

        if (_tabs.Count == 0)
        {
            StatusText.Text = "Не выбран ни один сервис перевода";
            return;
        }

        string from = (SourceCombo.SelectedValue as string) ?? "auto";
        string to = (TargetCombo.SelectedValue as string) ?? "ru";

        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;

        StatusText.Text = "Перевод…";

        foreach (ProviderTab tab in _tabs)
        {
            tab.Text = "Перевод…";
            tab.Info = string.Empty;
            RunProvider(tab, text, from, to, token);
        }
    }

    private async void RunProvider(ProviderTab tab, string text, string from, string to, CancellationToken token)
    {
        ITranslationProvider? provider = ProviderRegistry.Get(tab.ProviderName);
        if (provider is null)
        {
            tab.Text = string.Empty;
            tab.Info = "Сервис недоступен";
            return;
        }

        TranslationResult result;
        try
        {
            result = await provider.TranslateAsync(text, from, to, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            result = TranslationResult.Fail(provider.Name, ex.Message);
        }

        if (token.IsCancellationRequested)
            return;

        if (result.Success)
        {
            tab.Text = result.TranslatedText;
            tab.Info = BuildInfo(result);
            SaveToHistory(tab, text, result, from, to);
        }
        else
        {
            tab.Text = string.Empty;
            tab.Info = string.IsNullOrWhiteSpace(result.Error) ? "Ошибка перевода" : result.Error;
        }

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_tabs.Any(t => t.Text == "Перевод…"))
        {
            StatusText.Text = "Перевод…";
            return;
        }

        ProviderTab? selected = ResultsTabs.SelectedItem as ProviderTab;
        if (selected is not null && selected.Text.Length == 0 && selected.Info.Length > 0)
        {
            StatusText.Text = selected.Info;
            return;
        }

        StatusText.Text = "Готово";
    }

    private void SaveToHistory(ProviderTab tab, string sourceText, TranslationResult result, string from, string to)
    {
        HistoryStore? history = App.History;
        if (history is null)
            return;

        if (!ReferenceEquals(ResultsTabs.SelectedItem, tab))
            return;

        try
        {
            history.Add(new HistoryEntry
            {
                SourceText = sourceText,
                TranslatedText = result.TranslatedText,
                SourceLang = string.IsNullOrEmpty(result.DetectedLanguage) ? from : result.DetectedLanguage,
                TargetLang = to,
                Provider = result.ProviderName,
                CreatedAt = DateTime.UtcNow
            });

            history.Trim(App.Settings.HistoryLimit);
        }
        catch
        {
        }
    }

    private static string BuildInfo(TranslationResult result)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(result.Transcription))
            parts.Add("[" + result.Transcription + "]");

        if (result.Dictionary is { Length: > 0 })
            parts.Add(string.Join("; ", result.Dictionary));

        return string.Join("   ", parts);
    }

    private void RebuildTabs()
    {
        List<string> wanted = App.Settings.EnabledProviders
            .Select(name => ProviderRegistry.Get(name))
            .Where(p => p is not null)
            .Select(p => p!.Name)
            .Distinct()
            .ToList();

        if (wanted.Count == 0)
        {
            _tabs.Clear();
            return;
        }

        if (_tabs.Count == wanted.Count && _tabs.Select(t => t.ProviderName).SequenceEqual(wanted))
            return;

        string? selectedName = (ResultsTabs.SelectedItem as ProviderTab)?.ProviderName;

        _tabs.Clear();
        foreach (string name in wanted)
            _tabs.Add(new ProviderTab(name));

        ProviderTab? restore = _tabs.FirstOrDefault(t => t.ProviderName == selectedName)
            ?? _tabs.FirstOrDefault(t => t.ProviderName.Equals(App.Settings.ActiveProvider, StringComparison.OrdinalIgnoreCase))
            ?? _tabs.FirstOrDefault();

        ResultsTabs.SelectedItem = restore;
    }

    private void UpdateSwapState()
    {
        SwapButton.IsEnabled = (SourceCombo.SelectedValue as string) != "auto";
    }

    private void UpdateCharCount()
    {
        CharCountText.Text = InputBox.Text.Length + " симв.";
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            Translate();
            e.Handled = true;
        }
    }

    private void TranslateButton_Click(object sender, RoutedEventArgs e)
    {
        Translate();
    }

    private void SwapButton_Click(object sender, RoutedEventArgs e)
    {
        string? from = SourceCombo.SelectedValue as string;
        string? to = TargetCombo.SelectedValue as string;

        if (from is null || to is null || from == "auto")
            return;

        _suppressLanguageEvents = true;
        SourceCombo.SelectedValue = to;
        TargetCombo.SelectedValue = from;
        _suppressLanguageEvents = false;

        UpdateSwapState();
    }

    private void SourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLanguageEvents)
            return;

        UpdateSwapState();
    }

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateCharCount();
    }

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        HistoryWindow.ShowWindow(ShowWithText);
    }

    private void SpeakTab_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ProviderTab tab || tab.Text.Length == 0)
            return;

        _speech.Speak(tab.Text, (TargetCombo.SelectedValue as string) ?? "ru");
    }

    private void CopyTab_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ProviderTab tab || tab.Text.Length == 0)
            return;

        try
        {
            Clipboard.SetText(tab.Text);
        }
        catch
        {
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}

public sealed class ProviderTab : INotifyPropertyChanged
{
    private string _text = string.Empty;
    private string _info = string.Empty;

    public ProviderTab(string providerName)
    {
        ProviderName = providerName;
    }

    public string ProviderName { get; }

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value)
                return;

            _text = value;
            OnPropertyChanged();
        }
    }

    public string Info
    {
        get => _info;
        set
        {
            if (_info == value)
                return;

            _info = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(InfoVisibility));
        }
    }

    public Visibility InfoVisibility => _info.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

    public void Reset()
    {
        Text = string.Empty;
        Info = string.Empty;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

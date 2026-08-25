using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LimboTranslate.Data;
using LimboTranslate.Providers;

namespace LimboTranslate.UI;

public partial class PopupWindow : Window
{
    private const int AutoCloseSeconds = 12;

    private static PopupWindow? _current;

    public static event Action<string>? OpenInMainWindow;

    public static event Action<string, string>? SpeakRequested;

    private readonly DispatcherTimer _autoClose;
    private CancellationTokenSource? _cts;
    private Point _anchor;
    private string _sourceText = string.Empty;
    private string _translatedText = string.Empty;
    private string _translationLanguage = string.Empty;
    private bool _closing;
    private bool _mouseInside;
    private DispatcherTimer? _copyReset;

    public PopupWindow()
    {
        InitializeComponent();

        _autoClose = new DispatcherTimer { Interval = TimeSpan.FromSeconds(AutoCloseSeconds) };
        _autoClose.Tick += (_, _) => ClosePopup();
    }

    public static void ShowPopup(string text, Point screenPoint)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _current?.ClosePopup();

        var popup = new PopupWindow();
        _current = popup;
        popup.ShowFor(text, screenPoint);
    }

    public void ShowFor(string text, Point screenPoint)
    {
        _anchor = screenPoint;
        _sourceText = text.Trim();

        var settings = App.Settings;
        string provider = settings.ActiveProvider;

        ProviderText.Text = provider;
        SourceTextBlock.Text = _sourceText;
        TranslationTextBlock.Text = "Перевод…";
        TranscriptionTextBlock.Visibility = Visibility.Collapsed;
        DictionaryTextBlock.Visibility = Visibility.Collapsed;
        FooterTextBlock.Text = BuildFooter(settings.SourceLanguage, settings.TargetLanguage, provider);

        Show();
        Reposition();
        Activate();

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
        _autoClose.Start();

        _ = TranslateAsync(_sourceText, settings.SourceLanguage, settings.TargetLanguage, provider);
    }

    private async Task TranslateAsync(string text, string from, string to, string providerName)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        var provider = ProviderRegistry.Get(providerName);
        if (provider is null)
        {
            ShowError("Сервис перевода недоступен");
            return;
        }

        TranslationResult result;
        try
        {
            result = await ProviderRegistry.TranslateWithFallbackAsync(text, from, to, providerName, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            result = TranslationResult.Fail(providerName, ex.Message);
        }

        if (token.IsCancellationRequested || _closing)
        {
            return;
        }

        if (!result.Success)
        {
            ShowError(result.Error ?? "Не удалось перевести текст");
            return;
        }

        _translatedText = result.TranslatedText;
        _translationLanguage = to;

        TranslationTextBlock.Text = result.TranslatedText;

        if (!string.IsNullOrWhiteSpace(result.Transcription))
        {
            TranscriptionTextBlock.Text = "[" + result.Transcription + "]";
            TranscriptionTextBlock.Visibility = Visibility.Visible;
        }

        if (result.Dictionary is { Length: > 0 })
        {
            DictionaryTextBlock.Text = string.Join(Environment.NewLine, result.Dictionary);
            DictionaryTextBlock.Visibility = Visibility.Visible;
        }

        string detected = string.IsNullOrWhiteSpace(result.DetectedLanguage) ? from : result.DetectedLanguage;
        FooterTextBlock.Text = BuildFooter(detected, to, result.ProviderName);

        Reposition();
        SaveToHistory(text, result, detected, to);
    }

    private void ShowError(string message)
    {
        _translatedText = string.Empty;
        TranslationTextBlock.Text = message;
        Reposition();
    }

    private static string BuildFooter(string from, string to, string provider) =>
        Languages.Name(from) + " → " + Languages.Name(to) + " · " + provider;

    private void SaveToHistory(string source, TranslationResult result, string from, string to)
    {
        var history = App.History;
        if (history is null)
        {
            return;
        }

        try
        {
            history.Add(new HistoryEntry
            {
                SourceText = source,
                TranslatedText = result.TranslatedText,
                SourceLang = from,
                TargetLang = to,
                Provider = result.ProviderName,
                CreatedAt = DateTime.UtcNow
            });
        }
        catch
        {
        }
    }

    private void Reposition()
    {
        UpdateLayout();

        Point anchor = ToDeviceIndependent(_anchor);
        var work = SystemParameters.WorkArea;

        double width = ActualWidth > 0 ? ActualWidth : MinWidth;
        double height = ActualHeight > 0 ? ActualHeight : 100;

        double left = anchor.X + 12;
        double top = anchor.Y + 12;

        if (left + width > work.Right)
        {
            left = work.Right - width - 4;
        }

        if (top + height > work.Bottom)
        {
            top = anchor.Y - height - 12;
        }

        Left = Math.Max(work.Left + 4, left);
        Top = Math.Max(work.Top + 4, top);
    }

    private Point ToDeviceIndependent(Point devicePoint)
    {
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice;
        return transform.HasValue ? transform.Value.Transform(devicePoint) : devicePoint;
    }

    private void ClosePopup()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        _autoClose.Stop();
        _cts?.Cancel();

        if (ReferenceEquals(_current, this))
        {
            _current = null;
        }

        Close();
    }

    private void SpeakButton_Click(object sender, RoutedEventArgs e)
    {
        string text = string.IsNullOrEmpty(_translatedText) ? _sourceText : _translatedText;
        string lang = string.IsNullOrEmpty(_translatedText) ? App.Settings.SourceLanguage : _translationLanguage;
        if (!string.IsNullOrWhiteSpace(text))
        {
            SpeakRequested?.Invoke(text, lang);
        }
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_translatedText))
        {
            return;
        }

        if (!TrySetClipboard(_translatedText))
        {
            return;
        }

        _autoClose.Stop();
        CopyActionButton.Content = "Скопировано";

        _copyReset?.Stop();
        _copyReset ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _copyReset.Tick -= CopyReset_Tick;
        _copyReset.Tick += CopyReset_Tick;
        _copyReset.Start();
    }

    private void CopyReset_Tick(object? sender, EventArgs e)
    {
        _copyReset?.Stop();

        if (!_closing)
        {
            CopyActionButton.Content = "Копировать";
        }
    }

    private static bool TrySetClipboard(string text)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch
            {
                Thread.Sleep(50);
            }
        }

        return false;
    }

    private void ExpandButton_Click(object sender, RoutedEventArgs e)
    {
        string text = _sourceText;
        ClosePopup();
        OpenInMainWindow?.Invoke(text);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => ClosePopup();

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(async () =>
        {
            await Task.Delay(200);

            if (!_mouseInside && !IsActive)
            {
                ClosePopup();
            }
        }));
    }

    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        _mouseInside = true;
        _autoClose.Stop();
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        _mouseInside = false;

        if (_closing)
        {
            return;
        }

        _autoClose.Stop();
        _autoClose.Start();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ClosePopup();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoClose.Stop();

        if (_copyReset is not null)
        {
            _copyReset.Stop();
            _copyReset.Tick -= CopyReset_Tick;
            _copyReset = null;
        }

        _cts?.Dispose();
        _cts = null;

        if (ReferenceEquals(_current, this))
        {
            _current = null;
        }

        base.OnClosed(e);
    }
}

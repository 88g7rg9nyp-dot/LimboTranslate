using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LimboTranslate.Data;

namespace LimboTranslate.UI;

public partial class HistoryWindow : Window
{
    private const int LoadLimit = 500;

    private static HistoryWindow? _instance;

    private readonly DispatcherTimer _searchTimer;

    public HistoryWindow()
    {
        InitializeComponent();

        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _searchTimer.Tick += SearchTimer_Tick;

        Loaded += (_, _) => Reload();
        Closed += (_, _) =>
        {
            _searchTimer.Stop();
            if (ReferenceEquals(_instance, this))
                _instance = null;
        };
    }

    public event Action<string>? OpenInMainWindowRequested;

    public static HistoryWindow ShowWindow(Action<string>? openInMainWindow = null)
    {
        if (_instance is null)
        {
            _instance = new HistoryWindow();
            if (openInMainWindow is not null)
                _instance.OpenInMainWindowRequested += openInMainWindow;

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

    public void Reload()
    {
        string query = SearchBox.Text ?? string.Empty;
        HistoryStore? store = App.History;

        if (store is null)
        {
            HistoryList.ItemsSource = null;
            StatusText.Text = "История недоступна";
            return;
        }

        try
        {
            List<HistoryEntry> entries = string.IsNullOrWhiteSpace(query)
                ? store.Recent(LoadLimit)
                : store.Search(query, LoadLimit);

            HistoryList.ItemsSource = entries.Select(HistoryRow.From).ToList();
            StatusText.Text = entries.Count == 0 ? "Записей нет" : "Записей: " + entries.Count;
        }
        catch
        {
            HistoryList.ItemsSource = null;
            StatusText.Text = "Не удалось прочитать историю";
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void SearchTimer_Tick(object? sender, EventArgs e)
    {
        _searchTimer.Stop();
        Reload();
    }

    private void HistoryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenSelected();
    }

    private void HistoryList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            DeleteSelected();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            OpenSelected();
            e.Handled = true;
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        DeleteSelected();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (App.History is null)
            return;

        MessageBoxResult answer = MessageBox.Show(
            this,
            "Удалить всю историю переводов?",
            "LimboTranslate",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            App.History.Clear();
        }
        catch
        {
        }

        Reload();
    }

    private void OpenSelected()
    {
        if (HistoryList.SelectedItem is not HistoryRow row || string.IsNullOrWhiteSpace(row.FullSource))
            return;

        OpenInMainWindowRequested?.Invoke(row.FullSource);
    }

    private void DeleteSelected()
    {
        if (HistoryList.SelectedItem is not HistoryRow row || App.History is null)
            return;

        try
        {
            App.History.Delete(row.Id);
        }
        catch
        {
        }

        Reload();
    }

    public sealed class HistoryRow
    {
        public long Id { get; init; }

        public string Date { get; init; } = string.Empty;

        public string Source { get; init; } = string.Empty;

        public string Translated { get; init; } = string.Empty;

        public string Provider { get; init; } = string.Empty;

        public string FullSource { get; init; } = string.Empty;

        public static HistoryRow From(HistoryEntry entry) => new()
        {
            Id = entry.Id,
            Date = entry.CreatedAt.ToLocalTime().ToString("dd.MM HH:mm"),
            Source = Shorten(entry.SourceText),
            Translated = Shorten(entry.TranslatedText),
            Provider = entry.Provider,
            FullSource = entry.SourceText
        };

        private static string Shorten(string text)
        {
            string flat = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            return flat.Length <= 60 ? flat : flat[..60] + "…";
        }
    }
}

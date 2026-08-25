using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using LimboTranslate.Core;
using LimboTranslate.Data;
using LimboTranslate.UI;

namespace LimboTranslate;

public partial class App : Application
{
    private static Mutex? _instanceMutex;

    private HotkeyManager? _hotkeys;
    private TrayService? _tray;
    private SpeechService? _speech;
    private MouseSelectionWatcher? _mouseWatcher;

    public static AppSettings Settings { get; private set; } = new();

    public static HistoryStore? History { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(true, "LimboTranslate_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }

        Settings = SettingsStore.Load();

        try
        {
            History = new HistoryStore();
            History.EnsureSchema();
        }
        catch
        {
            History = null;
        }

        _speech = new SpeechService();

        _tray = new TrayService();
        _tray.TranslateSelectionRequested += OnTranslateSelection;
        _tray.SpeakSelectionRequested += OnSpeakSelection;
        _tray.MainWindowRequested += OnOpenMainWindow;
        _tray.HistoryRequested += OnOpenHistory;
        _tray.SettingsRequested += OnOpenSettings;
        _tray.ExitRequested += OnExitRequested;

        _hotkeys = new HotkeyManager();
        _hotkeys.DoubleCtrlPressed += OnTranslateSelection;
        ApplyHotkeys();

        PopupWindow.OpenInMainWindow += OnOpenInMainWindow;
        PopupWindow.SpeakRequested += OnPopupSpeak;

        ApplySelectionWatcher();

        base.OnStartup(e);
    }

    public void ApplyHotkeys()
    {
        if (_hotkeys is null)
            return;

        _hotkeys.UnregisterAll();
        _hotkeys.Register(Settings.HotkeyTranslate, OnTranslateSelection);
        _hotkeys.Register(Settings.HotkeySpeak, OnSpeakSelection);
        _hotkeys.Register(Settings.HotkeyMainWindow, OnOpenMainWindow);
        _hotkeys.EnableDoubleCtrl(Settings.PopupEnabled && HotkeyParser.IsDoubleCtrl(Settings.HotkeyPopup));
    }

    private void OnSettingsChanged()
    {
        ApplyHotkeys();
        ApplySelectionWatcher();
    }

    public void ApplySelectionWatcher()
    {
        if (!Settings.TranslateOnMouseSelection)
        {
            if (_mouseWatcher is not null)
            {
                _mouseWatcher.TextSelected -= OnMouseTextSelected;
                _mouseWatcher.Dispose();
                _mouseWatcher = null;
            }

            return;
        }

        if (_mouseWatcher is null)
        {
            _mouseWatcher = new MouseSelectionWatcher(Settings.MouseSelectionDelayMs);
            _mouseWatcher.TextSelected += OnMouseTextSelected;
        }

        _mouseWatcher.Start();
    }

    private void OnMouseTextSelected(string text, Point point)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        SelectionIconWindow.ShowIcon(text, point);
    }

    private async void OnTranslateSelection()
    {
        string? text = await SelectionCapture.GetSelectedTextAsync();
        if (string.IsNullOrWhiteSpace(text))
            return;

        ShowTranslationFor(text);
    }

    private async void OnSpeakSelection()
    {
        string? text = await SelectionCapture.GetSelectedTextAsync();
        if (string.IsNullOrWhiteSpace(text))
            return;

        _speech?.Speak(text, Settings.SourceLanguage);
    }

    private void OnPopupSpeak(string text, string lang)
    {
        _speech?.Speak(text, lang);
    }

    private void OnOpenInMainWindow(string text)
    {
        UI.MainWindow.ShowWithText(text);
    }

    private void ShowTranslationFor(string text)
    {
        if (Settings.PopupEnabled)
            PopupWindow.ShowPopup(text, CursorPosition());
        else
            UI.MainWindow.ShowWithText(text);
    }

    private static Point CursorPosition()
    {
        return GetCursorPos(out POINT point) ? new Point(point.X, point.Y) : new Point(0, 0);
    }

    private void OnOpenMainWindow()
    {
        UI.MainWindow.ShowWindow();
    }

    private void OnOpenHistory()
    {
        HistoryWindow.ShowWindow(OnOpenInMainWindow);
    }

    private void OnOpenSettings()
    {
        SettingsWindow.ShowWindow(OnSettingsChanged);
    }

    private void OnExitRequested()
    {
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        PopupWindow.OpenInMainWindow -= OnOpenInMainWindow;
        PopupWindow.SpeakRequested -= OnPopupSpeak;

        _hotkeys?.Dispose();
        _hotkeys = null;

        _tray?.Dispose();
        _tray = null;

        _speech?.Dispose();
        _speech = null;

        if (_mouseWatcher is not null)
        {
            _mouseWatcher.TextSelected -= OnMouseTextSelected;
            _mouseWatcher.Dispose();
            _mouseWatcher = null;
        }

        if (_instanceMutex is not null)
        {
            try
            {
                _instanceMutex.ReleaseMutex();
            }
            catch
            {
            }

            _instanceMutex.Dispose();
            _instanceMutex = null;
        }

        base.OnExit(e);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);
}

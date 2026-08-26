using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace LimboTranslate.UI;

public partial class SelectionIconWindow : Window
{
    private const int AutoHideSeconds = 6;

    private static SelectionIconWindow? _current;

    private readonly DispatcherTimer _autoHide;
    private Point _anchor;
    private string _text = string.Empty;
    private bool _closing;
    private bool _editable;
    private IntPtr _sourceWindow = IntPtr.Zero;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public SelectionIconWindow()
    {
        InitializeComponent();

        _autoHide = new DispatcherTimer { Interval = TimeSpan.FromSeconds(AutoHideSeconds) };
        _autoHide.Tick += (_, _) => CloseIcon();
    }

    public static void ShowIcon(string text, Point screenPoint)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _current?.CloseIcon();

        var icon = new SelectionIconWindow();
        _current = icon;
        icon.ShowFor(text, screenPoint);
    }

    public static void HideIcon()
    {
        _current?.CloseIcon();
    }

    private void ShowFor(string text, Point screenPoint)
    {
        _anchor = screenPoint;
        _text = text.Trim();
        _sourceWindow = GetForegroundWindow();
        _editable = Core.SelectionCapture.IsEditableFocused();

        RootBorder.ToolTip = _editable
            ? "Заменить выделенный текст переводом"
            : "Перевести выделенный текст";

        Show();
        Reposition();

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
        _autoHide.Start();
    }

    private void Reposition()
    {
        UpdateLayout();

        Point anchor = ToDeviceIndependent(_anchor);
        var work = SystemParameters.WorkArea;

        double width = ActualWidth > 0 ? ActualWidth : 26;
        double height = ActualHeight > 0 ? ActualHeight : 26;

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

    private void CloseIcon()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        _autoHide.Stop();

        if (ReferenceEquals(_current, this))
        {
            _current = null;
        }

        Close();
    }

    private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        string text = _text;
        Point anchor = _anchor;
        bool editable = _editable;
        IntPtr sourceWindow = _sourceWindow;

        CloseIcon();

        if (editable)
        {
            _ = ReplaceInFieldAsync(text, anchor, sourceWindow);
            return;
        }

        PopupWindow.ShowPopup(text, anchor);
    }

    private static async Task ReplaceInFieldAsync(string text, Point anchor, IntPtr sourceWindow)
    {
        try
        {
            var settings = App.Settings;
            string target = Providers.Languages.ResolveTarget(text, settings.SourceLanguage, settings.TargetLanguage);

            var result = await Providers.ProviderRegistry.TranslateWithFallbackAsync(
                text,
                settings.SourceLanguage,
                target,
                settings.ActiveProvider,
                CancellationToken.None).ConfigureAwait(true);

            if (!result.Success || string.IsNullOrWhiteSpace(result.TranslatedText))
            {
                PopupWindow.ShowPopup(text, anchor);
                return;
            }

            if (sourceWindow != IntPtr.Zero)
            {
                SetForegroundWindow(sourceWindow);
                await Task.Delay(80).ConfigureAwait(true);
            }

            bool replaced = await Core.SelectionCapture.ReplaceSelectionAsync(result.TranslatedText).ConfigureAwait(true);
            if (!replaced)
            {
                PopupWindow.ShowPopup(text, anchor);
            }
        }
        catch
        {
            PopupWindow.ShowPopup(text, anchor);
        }
    }

    private void Root_MouseEnter(object sender, MouseEventArgs e)
    {
        _autoHide.Stop();
        RootBorder.Background = (Brush)FindResource("AccentBrush");
    }

    private void Root_MouseLeave(object sender, MouseEventArgs e)
    {
        RootBorder.Background = (Brush)FindResource("PanelBrush");
        _autoHide.Start();
    }
}

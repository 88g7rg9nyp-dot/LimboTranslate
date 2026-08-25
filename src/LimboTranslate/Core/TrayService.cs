using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hardcodet.Wpf.TaskbarNotification;
using MediaColor = System.Windows.Media.Color;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace LimboTranslate.Core;

public sealed class TrayService : IDisposable
{
    private readonly TaskbarIcon _icon;
    private Icon? _generatedIcon;
    private IntPtr _iconHandle = IntPtr.Zero;
    private bool _disposed;

    public event Action? TranslateSelectionRequested;
    public event Action? SpeakSelectionRequested;
    public event Action? MainWindowRequested;
    public event Action? HistoryRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public TrayService()
    {
        _icon = new TaskbarIcon
        {
            ToolTipText = "LimboTranslate",
            Icon = CreateIcon(),
            ContextMenu = BuildMenu(),
            Visibility = Visibility.Visible
        };

        _icon.TrayMouseDoubleClick += (_, _) => MainWindowRequested?.Invoke();
    }

    public void ShowMessage(string title, string message)
    {
        if (_disposed)
            return;

        try
        {
            _icon.ShowBalloonTip(title, message, BalloonIcon.Info);
        }
        catch
        {
        }
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu
        {
            Background = FindBrush("PanelBrush", MediaColor.FromRgb(0x25, 0x25, 0x26)),
            Foreground = FindBrush("TextBrush", MediaColor.FromRgb(0xE0, 0xE0, 0xE0)),
            BorderBrush = FindBrush("BorderBrush2", MediaColor.FromRgb(0x3F, 0x3F, 0x46)),
            BorderThickness = new Thickness(1)
        };

        menu.Items.Add(CreateItem("Перевести выделенное (Ctrl+Q)", () => TranslateSelectionRequested?.Invoke()));
        menu.Items.Add(CreateItem("Озвучить выделенное (Ctrl+E)", () => SpeakSelectionRequested?.Invoke()));
        menu.Items.Add(CreateItem("Главное окно (Ctrl+Shift+Q)", () => MainWindowRequested?.Invoke()));
        menu.Items.Add(CreateItem("История", () => HistoryRequested?.Invoke()));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateItem("Настройки", () => SettingsRequested?.Invoke()));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateItem("Выход", () => ExitRequested?.Invoke()));

        return menu;
    }

    private static MenuItem CreateItem(string header, Action action)
    {
        var item = new MenuItem
        {
            Header = header,
            Background = FindBrush("PanelBrush", MediaColor.FromRgb(0x25, 0x25, 0x26)),
            Foreground = FindBrush("TextBrush", MediaColor.FromRgb(0xE0, 0xE0, 0xE0)),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 13
        };

        item.Click += (_, _) => action();
        return item;
    }

    private static SolidColorBrush FindBrush(string resourceKey, MediaColor fallback)
    {
        if (Application.Current?.TryFindResource(resourceKey) is SolidColorBrush brush)
            return brush;

        return new SolidColorBrush(fallback);
    }

    private Icon? CreateIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute);
            var stream = System.Windows.Application.GetResourceStream(uri)?.Stream;
            if (stream is not null)
            {
                using (stream)
                {
                    _generatedIcon = new Icon(stream, new System.Drawing.Size(16, 16));
                    return _generatedIcon;
                }
            }
        }
        catch
        {
        }

        try
        {
            using var bitmap = new Bitmap(16, 16, DrawingPixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                using var background = new SolidBrush(System.Drawing.Color.FromArgb(0x0E, 0x7A, 0xC4));
                graphics.FillRectangle(background, 0, 0, 16, 16);

                using var font = new Font("Segoe UI", 9f, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
                using var text = new SolidBrush(System.Drawing.Color.White);
                using var format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };

                graphics.DrawString("T", font, text, new RectangleF(0, 0, 16, 16), format);
            }

            _iconHandle = bitmap.GetHicon();
            _generatedIcon = (Icon)Icon.FromHandle(_iconHandle).Clone();
            return _generatedIcon;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            _icon.Dispose();
        }
        catch
        {
        }

        _generatedIcon?.Dispose();
        _generatedIcon = null;

        if (_iconHandle != IntPtr.Zero)
        {
            DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}

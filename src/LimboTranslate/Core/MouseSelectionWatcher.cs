using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace LimboTranslate.Core;

public sealed class MouseSelectionWatcher : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int MinDragDistance = 6;
    private const int MaxTextLength = 5000;
    private const int DefaultDelayMs = 180;

    private readonly LowLevelMouseProc _hookProc;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _timer;

    private IntPtr _hookHandle = IntPtr.Zero;
    private POINT _downPoint;
    private POINT _upPoint;
    private uint _lastDownTime;
    private bool _pendingSelection;
    private bool _doubleClick;
    private bool _busy;
    private string? _lastText;
    private bool _disposed;

    public event Action<string, Point>? TextSelected;

    public MouseSelectionWatcher() : this(DefaultDelayMs)
    {
    }

    public MouseSelectionWatcher(int delayMs)
    {
        _hookProc = HookCallback;
        _timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(delayMs <= 0 ? DefaultDelayMs : delayMs),
        };
        _timer.Tick += OnTimerTick;
    }

    public void Start()
    {
        if (_disposed || _hookHandle != IntPtr.Zero)
            return;

        try
        {
            _lastText = null;
            _pendingSelection = false;
            _doubleClick = false;
            _lastDownTime = 0;
            _hookHandle = SetWindowsHookEx(WhMouseLl, _hookProc, GetModuleHandle(null), 0);
        }
        catch
        {
            _hookHandle = IntPtr.Zero;
        }
    }

    public void Stop()
    {
        _timer.Stop();
        _pendingSelection = false;
        _doubleClick = false;
        _lastText = null;

        if (_hookHandle == IntPtr.Zero)
            return;

        try
        {
            UnhookWindowsHookEx(_hookHandle);
        }
        catch
        {
        }

        _hookHandle = IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                ProcessMessage(wParam.ToInt32(), lParam);
            }
            catch
            {
            }
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private void ProcessMessage(int message, IntPtr lParam)
    {
        MSLLHOOKSTRUCT data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

        switch (message)
        {
            case WmLButtonDown:
                _doubleClick = data.time - _lastDownTime <= GetDoubleClickTime() && !IsDrag(_downPoint, data.pt);
                _lastDownTime = data.time;
                _downPoint = data.pt;
                _lastText = null;
                _pendingSelection = false;
                break;

            case WmLButtonUp:
                _upPoint = data.pt;
                if (_doubleClick || IsDrag(_downPoint, _upPoint))
                    QueueCapture();
                _doubleClick = false;
                break;
        }
    }

    private void QueueCapture()
    {
        if (_busy || _pendingSelection)
            return;

        _pendingSelection = true;

        _dispatcher.BeginInvoke(new Action(() =>
        {
            _timer.Stop();
            _timer.Start();
        }));
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        _pendingSelection = false;

        if (_busy || _hookHandle == IntPtr.Zero)
            return;

        _busy = true;

        try
        {
            if (IsOwnWindow(_upPoint))
                return;

            string? text = await SelectionCapture.GetSelectedTextAsync();
            if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength)
                return;

            if (string.Equals(text, _lastText, StringComparison.Ordinal))
                return;

            _lastText = text;
            TextSelected?.Invoke(text, new Point(_upPoint.X, _upPoint.Y));
        }
        catch
        {
        }
        finally
        {
            _busy = false;
        }
    }

    private static bool IsDrag(POINT from, POINT to)
    {
        int dx = to.X - from.X;
        int dy = to.Y - from.Y;
        return dx * dx + dy * dy > MinDragDistance * MinDragDistance;
    }

    private static bool IsOwnWindow(POINT point)
    {
        try
        {
            IntPtr handle = WindowFromPoint(point);
            if (handle == IntPtr.Zero)
                return false;

            GetWindowThreadProcessId(handle, out uint processId);
            return processId == (uint)Environment.ProcessId;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timer.Tick -= OnTimerTick;
        Stop();
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}

using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace LimboTranslate.Core;

public sealed class HotkeyManager : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int VkLControl = 0xA2;
    private const int VkRControl = 0xA3;
    private const int DoubleCtrlIntervalMs = 400;

    private readonly Dictionary<int, Action> _callbacks = new();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly LowLevelKeyboardProc _hookProc;

    private HwndSource? _source;
    private IntPtr _hookHandle = IntPtr.Zero;
    private int _nextId = 1;
    private DateTime _lastCtrlUp = DateTime.MinValue;
    private bool _interrupted;
    private bool _disposed;

    public event Action? DoubleCtrlPressed;

    public HotkeyManager()
    {
        _hookProc = HookCallback;

        var parameters = new HwndSourceParameters("LimboTranslateHotkeys")
        {
            Width = 0,
            Height = 0,
            ParentWindow = new IntPtr(-3),
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public int Register(string hotkey, Action callback)
    {
        if (_disposed || _source is null || callback is null)
            return -1;

        (uint modifiers, uint virtualKey) = HotkeyParser.Parse(hotkey);
        if (virtualKey == 0)
            return -1;

        int id = _nextId;
        if (!RegisterHotKey(_source.Handle, id, modifiers, virtualKey))
            return -1;

        _nextId++;
        _callbacks[id] = callback;
        return id;
    }

    public void UnregisterAll()
    {
        if (_source is null)
        {
            _callbacks.Clear();
            return;
        }

        foreach (int id in _callbacks.Keys)
            UnregisterHotKey(_source.Handle, id);

        _callbacks.Clear();
    }

    public void EnableDoubleCtrl(bool enabled)
    {
        if (_disposed)
            return;

        if (enabled)
            InstallHook();
        else
            RemoveHook();
    }

    private void InstallHook()
    {
        if (_hookHandle != IntPtr.Zero)
            return;

        _lastCtrlUp = DateTime.MinValue;
        _interrupted = false;
        _hookHandle = SetWindowsHookEx(WhKeyboardLl, _hookProc, GetModuleHandle(null), 0);
    }

    private void RemoveHook()
    {
        if (_hookHandle == IntPtr.Zero)
            return;

        UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey)
            return IntPtr.Zero;

        int id = wParam.ToInt32();
        if (!_callbacks.TryGetValue(id, out Action? callback))
            return IntPtr.Zero;

        handled = true;

        try
        {
            callback();
        }
        catch
        {
        }

        return IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                ProcessKey(wParam.ToInt32(), Marshal.ReadInt32(lParam));
            }
            catch
            {
            }
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private void ProcessKey(int message, int virtualKey)
    {
        bool isCtrl = virtualKey is VkLControl or VkRControl;

        if (message is WmKeyDown or WmSysKeyDown)
        {
            if (!isCtrl)
                _interrupted = true;
            return;
        }

        if (message is not (WmKeyUp or WmSysKeyUp) || !isCtrl)
            return;

        if (_interrupted)
        {
            _interrupted = false;
            _lastCtrlUp = DateTime.MinValue;
            return;
        }

        DateTime now = DateTime.UtcNow;
        if (_lastCtrlUp != DateTime.MinValue &&
            (now - _lastCtrlUp).TotalMilliseconds <= DoubleCtrlIntervalMs)
        {
            _lastCtrlUp = DateTime.MinValue;
            RaiseDoubleCtrl();
            return;
        }

        _lastCtrlUp = now;
    }

    private void RaiseDoubleCtrl()
    {
        Action? handler = DoubleCtrlPressed;
        if (handler is null)
            return;

        _dispatcher.InvokeAsync(() =>
        {
            try
            {
                handler();
            }
            catch
            {
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        RemoveHook();
        UnregisterAll();

        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}

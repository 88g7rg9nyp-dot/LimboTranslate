using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Threading;

namespace LimboTranslate.Core;

public static class SelectionCapture
{
    private const int MaxTextLength = 5000;

    private const byte VkControl = 0x11;
    private const byte VkC = 0x43;
    private const byte VkV = 0x56;
    private const uint KeyEventKeyUp = 0x0002;

    private const int ClipboardRetries = 3;
    private const int ClipboardRetryDelayMs = 50;
    private const int CopyPollDelayMs = 30;
    private const int CopyPollAttempts = 10;
    private const int ClipboardRestoreDelayMs = 100;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    public static async Task<string?> GetSelectedTextAsync()
    {
        string? text = TryGetFromAutomation();
        if (!string.IsNullOrWhiteSpace(text))
            return Normalize(text);

        text = await TryGetViaCopyAsync().ConfigureAwait(true);
        return string.IsNullOrWhiteSpace(text) ? null : Normalize(text);
    }

    public static bool IsEditableFocused()
    {
        try
        {
            AutomationElement? focused = AutomationElement.FocusedElement;
            if (focused is null)
                return false;

            if (!focused.TryGetCurrentPattern(ValuePattern.Pattern, out object? valueObject)
                || valueObject is not ValuePattern valuePattern)
            {
                return false;
            }

            return !valuePattern.Current.IsReadOnly;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> ReplaceSelectionAsync(string replacement)
    {
        if (string.IsNullOrEmpty(replacement))
            return false;

        string? previous = await GetClipboardTextAsync().ConfigureAwait(true);

        try
        {
            bool copied = false;
            for (int attempt = 0; attempt < ClipboardRetries; attempt++)
            {
                try
                {
                    RunOnUi(() => Clipboard.SetText(replacement));
                    copied = true;
                    break;
                }
                catch
                {
                    await Task.Delay(ClipboardRetryDelayMs).ConfigureAwait(true);
                }
            }

            if (!copied)
                return false;

            keybd_event(VkControl, 0, 0, UIntPtr.Zero);
            keybd_event(VkV, 0, 0, UIntPtr.Zero);
            keybd_event(VkV, 0, KeyEventKeyUp, UIntPtr.Zero);
            keybd_event(VkControl, 0, KeyEventKeyUp, UIntPtr.Zero);

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            await Task.Delay(ClipboardRestoreDelayMs).ConfigureAwait(true);
            await RestoreClipboardAsync(previous).ConfigureAwait(true);
        }
    }

    public static string? GetClipboardText()
    {
        for (int attempt = 0; attempt < ClipboardRetries; attempt++)
        {
            try
            {
                return ReadClipboardOnUi();
            }
            catch
            {
                Thread.Sleep(ClipboardRetryDelayMs);
            }
        }

        return null;
    }

    private static string? TryGetFromAutomation()
    {
        try
        {
            AutomationElement? focused = AutomationElement.FocusedElement;
            if (focused is null)
                return null;

            if (focused.TryGetCurrentPattern(TextPattern.Pattern, out object? textObject)
                && textObject is TextPattern textPattern)
            {
                TextPatternRange[] ranges = textPattern.GetSelection();
                if (ranges is not null && ranges.Length > 0)
                {
                    string selected = string.Concat(ranges.Select(range => range.GetText(-1)));
                    if (!string.IsNullOrWhiteSpace(selected))
                        return selected;
                }
            }

            if (focused.TryGetCurrentPattern(ValuePattern.Pattern, out object? valueObject)
                && valueObject is ValuePattern valuePattern)
            {
                string value = valuePattern.Current.Value;
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }
        catch
        {
        }

        return null;
    }

    private static async Task<string?> TryGetViaCopyAsync()
    {
        string? previous = await GetClipboardTextAsync().ConfigureAwait(true);
        string? captured = null;

        try
        {
            if (!SendCopyCommand())
                return null;

            for (int attempt = 0; attempt < CopyPollAttempts; attempt++)
            {
                await Task.Delay(CopyPollDelayMs).ConfigureAwait(true);

                string? current = await GetClipboardTextAsync().ConfigureAwait(true);
                if (!string.IsNullOrWhiteSpace(current) && !string.Equals(current, previous, StringComparison.Ordinal))
                {
                    captured = current;
                    break;
                }
            }

            return captured;
        }
        finally
        {
            await Task.Delay(ClipboardRestoreDelayMs).ConfigureAwait(true);
            await RestoreClipboardAsync(previous).ConfigureAwait(true);
        }
    }

    private static bool SendCopyCommand()
    {
        try
        {
            keybd_event(VkControl, 0, 0, UIntPtr.Zero);
            keybd_event(VkC, 0, 0, UIntPtr.Zero);
            keybd_event(VkC, 0, KeyEventKeyUp, UIntPtr.Zero);
            keybd_event(VkControl, 0, KeyEventKeyUp, UIntPtr.Zero);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string?> GetClipboardTextAsync()
    {
        for (int attempt = 0; attempt < ClipboardRetries; attempt++)
        {
            try
            {
                return ReadClipboardOnUi();
            }
            catch
            {
                await Task.Delay(ClipboardRetryDelayMs).ConfigureAwait(true);
            }
        }

        return null;
    }

    private static async Task RestoreClipboardAsync(string? previous)
    {
        for (int attempt = 0; attempt < ClipboardRetries; attempt++)
        {
            try
            {
                RunOnUi(() =>
                {
                    if (string.IsNullOrEmpty(previous))
                        Clipboard.Clear();
                    else
                        Clipboard.SetText(previous);
                });

                return;
            }
            catch
            {
                await Task.Delay(ClipboardRetryDelayMs).ConfigureAwait(true);
            }
        }
    }

    private static string? ReadClipboardOnUi()
    {
        return RunOnUi(() => Clipboard.ContainsText() ? Clipboard.GetText() : null);
    }

    private static T RunOnUi<T>(Func<T> action)
    {
        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            return action();

        return dispatcher.Invoke(action);
    }

    private static void RunOnUi(Action action)
    {
        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }

    private static string Normalize(string text)
    {
        string trimmed = text.Trim();
        return trimmed.Length > MaxTextLength ? trimmed[..MaxTextLength] : trimmed;
    }
}

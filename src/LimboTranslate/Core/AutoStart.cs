using Microsoft.Win32;

namespace LimboTranslate.Core;

public static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LimboTranslate";

    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true)
                                     ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
            if (key is null)
                return;

            if (!enabled)
            {
                key.DeleteValue(ValueName, false);
                return;
            }

            string? path = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(path))
                return;

            key.SetValue(ValueName, "\"" + path + "\"", RegistryValueKind.String);
        }
        catch
        {
        }
    }
}

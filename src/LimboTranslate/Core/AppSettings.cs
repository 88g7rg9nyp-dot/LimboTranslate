namespace LimboTranslate.Core;

public class AppSettings
{
    public string SourceLanguage { get; set; } = "auto";
    public string TargetLanguage { get; set; } = "ru";
    public List<string> EnabledProviders { get; set; } = new() { "Google", "DeepL", "Yandex", "MyMemory" };
    public string ActiveProvider { get; set; } = "Google";
    public bool StartWithWindows { get; set; } = false;
    public bool DarkTheme { get; set; } = true;
    public int HistoryLimit { get; set; } = 500;
    public string HotkeyTranslate { get; set; } = "Ctrl+Q";
    public string HotkeyPopup { get; set; } = "DoubleCtrl";
    public string HotkeySpeak { get; set; } = "Ctrl+E";
    public string HotkeyMainWindow { get; set; } = "Ctrl+Shift+Q";
    public bool PopupEnabled { get; set; } = true;
    public bool TranslateOnMouseSelection { get; set; } = true;
    public int MouseSelectionDelayMs { get; set; } = 180;
}

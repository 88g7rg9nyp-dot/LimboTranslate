namespace LimboTranslate.Data;

public class HistoryEntry
{
    public long Id { get; set; }

    public string SourceText { get; set; } = string.Empty;

    public string TranslatedText { get; set; } = string.Empty;

    public string SourceLang { get; set; } = string.Empty;

    public string TargetLang { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

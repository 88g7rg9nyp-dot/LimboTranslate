namespace LimboTranslate.Providers;

public sealed class TranslationResult
{
    public required string ProviderName { get; init; }
    public string TranslatedText { get; init; } = string.Empty;
    public string DetectedLanguage { get; init; } = string.Empty;
    public string? Transcription { get; init; }
    public string[]? Dictionary { get; init; }
    public bool Success { get; init; }
    public string? Error { get; init; }

    public static TranslationResult Ok(
        string provider,
        string text,
        string detectedLanguage,
        string? transcription = null,
        string[]? dictionary = null) => new()
    {
        ProviderName = provider,
        TranslatedText = text,
        DetectedLanguage = detectedLanguage,
        Transcription = transcription,
        Dictionary = dictionary,
        Success = true
    };

    public static TranslationResult Fail(string provider, string error) => new()
    {
        ProviderName = provider,
        TranslatedText = string.Empty,
        DetectedLanguage = string.Empty,
        Success = false,
        Error = error
    };
}

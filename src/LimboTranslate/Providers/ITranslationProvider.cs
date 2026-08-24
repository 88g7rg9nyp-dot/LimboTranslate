namespace LimboTranslate.Providers;

public interface ITranslationProvider
{
    string Name { get; }

    bool RequiresKey { get; }

    Task<TranslationResult> TranslateAsync(string text, string from, string to, CancellationToken ct);
}

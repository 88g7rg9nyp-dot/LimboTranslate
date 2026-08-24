namespace LimboTranslate.Providers;

public static class ProviderRegistry
{
    public static IReadOnlyList<ITranslationProvider> All { get; } = new ITranslationProvider[]
    {
        new GoogleProvider(),
        new DeepLProvider()
    };

    public static ITranslationProvider? Get(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return All.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public static Task<TranslationResult[]> TranslateAllAsync(
        string text,
        string from,
        string to,
        IEnumerable<string> enabledNames,
        CancellationToken ct)
    {
        var providers = enabledNames
            .Select(Get)
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();

        if (providers.Count == 0)
        {
            return Task.FromResult(Array.Empty<TranslationResult>());
        }

        var tasks = providers.Select(p => p.TranslateAsync(text, from, to, ct));
        return Task.WhenAll(tasks);
    }
}

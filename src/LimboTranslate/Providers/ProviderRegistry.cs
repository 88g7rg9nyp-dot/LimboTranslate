namespace LimboTranslate.Providers;

public static class ProviderRegistry
{
    public static IReadOnlyList<ITranslationProvider> All { get; } = new ITranslationProvider[]
    {
        new GoogleProvider(),
        new DeepLProvider(),
        new YandexProvider(),
        new MyMemoryProvider()
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

    public static async Task<TranslationResult> TranslateWithFallbackAsync(
        string text,
        string from,
        string to,
        string preferredName,
        CancellationToken ct)
    {
        var order = new List<ITranslationProvider>();

        var preferred = Get(preferredName);
        if (preferred is not null)
        {
            order.Add(preferred);
        }

        foreach (var provider in All)
        {
            if (!order.Any(p => p.Name.Equals(provider.Name, StringComparison.OrdinalIgnoreCase)))
            {
                order.Add(provider);
            }
        }

        TranslationResult? firstFailure = null;

        foreach (var provider in order)
        {
            ct.ThrowIfCancellationRequested();

            TranslationResult result;
            try
            {
                result = await provider.TranslateAsync(text, from, to, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result = TranslationResult.Fail(provider.Name, ex.Message);
            }

            if (result.Success)
            {
                return result;
            }

            firstFailure ??= result;
        }

        return firstFailure ?? TranslationResult.Fail(preferredName, "Не удалось перевести текст");
    }
}

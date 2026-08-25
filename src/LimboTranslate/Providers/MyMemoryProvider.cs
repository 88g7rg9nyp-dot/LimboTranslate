using System.Net.Http;
using System.Text.Json;

namespace LimboTranslate.Providers;

public sealed class MyMemoryProvider : ITranslationProvider
{
    private const string Endpoint = "https://api.mymemory.translated.net/get";
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";

    private static readonly HttpClient Http = CreateClient();

    public string Name => "MyMemory";

    public bool RequiresKey => false;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
        return client;
    }

    public async Task<TranslationResult> TranslateAsync(string text, string from, string to, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return TranslationResult.Fail(Name, "Пустой текст");
        }

        try
        {
            string source = string.IsNullOrWhiteSpace(from) || from.Equals("auto", StringComparison.OrdinalIgnoreCase)
                ? "en"
                : from;

            string url = Endpoint
                + "?q=" + Uri.EscapeDataString(text)
                + "&langpair=" + Uri.EscapeDataString(source + "|" + to);

            using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return TranslationResult.Fail(Name, "MyMemory вернул код " + (int)response.StatusCode);
            }

            string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Parse(json, source);
        }
        catch (OperationCanceledException)
        {
            return TranslationResult.Fail(Name, "Запрос отменён");
        }
        catch (Exception ex)
        {
            return TranslationResult.Fail(Name, "MyMemory недоступен: " + ex.Message);
        }
    }

    private TranslationResult Parse(string json, string from)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("responseData", out var data)
            || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("translatedText", out var translated)
            || translated.ValueKind != JsonValueKind.String)
        {
            return TranslationResult.Fail(Name, "Неожиданный ответ MyMemory");
        }

        string result = translated.GetString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(result)
            || result.StartsWith("QUERY LENGTH LIMIT", StringComparison.OrdinalIgnoreCase)
            || result.StartsWith("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase)
            || result.StartsWith("PLEASE SELECT", StringComparison.OrdinalIgnoreCase))
        {
            return TranslationResult.Fail(Name, "MyMemory отклонил запрос");
        }

        return TranslationResult.Ok(Name, result, from);
    }
}

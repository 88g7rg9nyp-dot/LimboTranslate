using System.Net.Http;
using System.Text.Json;

namespace LimboTranslate.Providers;

public sealed class YandexProvider : ITranslationProvider
{
    private const string Endpoint = "https://translate.yandex.net/api/v1/tr.json/translate";
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";

    private static readonly HttpClient Http = CreateClient();

    public string Name => "Yandex";

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
            string lang = string.IsNullOrWhiteSpace(from) || from.Equals("auto", StringComparison.OrdinalIgnoreCase)
                ? to
                : from + "-" + to;

            string url = Endpoint
                + "?id=" + Guid.NewGuid().ToString("N") + "-0-0"
                + "&srv=android"
                + "&lang=" + Uri.EscapeDataString(lang);

            using var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("text", text)
            });

            using var response = await Http.PostAsync(url, content, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return TranslationResult.Fail(Name, "Yandex вернул код " + (int)response.StatusCode);
            }

            string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Parse(json, from);
        }
        catch (OperationCanceledException)
        {
            return TranslationResult.Fail(Name, "Запрос отменён");
        }
        catch (Exception ex)
        {
            return TranslationResult.Fail(Name, "Yandex недоступен: " + ex.Message);
        }
    }

    private TranslationResult Parse(string json, string from)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("text", out var textArray))
        {
            return TranslationResult.Fail(Name, "Неожиданный ответ Yandex");
        }

        var parts = new List<string>();
        foreach (var item in textArray.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                string? value = item.GetString();
                if (!string.IsNullOrEmpty(value))
                {
                    parts.Add(value);
                }
            }
        }

        if (parts.Count == 0)
        {
            return TranslationResult.Fail(Name, "Пустой перевод");
        }

        string detected = from;
        if (root.TryGetProperty("lang", out var langElement) && langElement.ValueKind == JsonValueKind.String)
        {
            string pair = langElement.GetString() ?? string.Empty;
            int dash = pair.IndexOf('-');
            if (dash > 0)
            {
                detected = pair[..dash];
            }
        }

        return TranslationResult.Ok(Name, string.Join(" ", parts), detected);
    }
}

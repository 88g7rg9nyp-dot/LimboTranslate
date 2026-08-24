using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace LimboTranslate.Providers;

public sealed class GoogleProvider : ITranslationProvider
{
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";

    private static readonly HttpClient Http = CreateClient();

    public string Name => "Google";

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
            var url = "https://translate.googleapis.com/translate_a/single"
                + "?client=gtx"
                + "&sl=" + Uri.EscapeDataString(string.IsNullOrWhiteSpace(from) ? "auto" : from)
                + "&tl=" + Uri.EscapeDataString(to)
                + "&dt=t&dt=bd&dt=rm&dt=at"
                + "&q=" + Uri.EscapeDataString(text);

            using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return TranslationResult.Fail(Name, "Google вернул код " + (int)response.StatusCode);
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Parse(json);
        }
        catch (OperationCanceledException)
        {
            return TranslationResult.Fail(Name, "Запрос отменён");
        }
        catch (Exception ex)
        {
            return TranslationResult.Fail(Name, "Google недоступен: " + ex.Message);
        }
    }

    private TranslationResult Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
        {
            return TranslationResult.Fail(Name, "Неожиданный ответ Google");
        }

        var builder = new StringBuilder();
        string? transcription = null;

        var segments = root[0];
        if (segments.ValueKind == JsonValueKind.Array)
        {
            foreach (var segment in segments.EnumerateArray())
            {
                if (segment.ValueKind != JsonValueKind.Array || segment.GetArrayLength() == 0)
                {
                    continue;
                }

                var part = segment[0];
                if (part.ValueKind == JsonValueKind.String)
                {
                    builder.Append(part.GetString());
                }

                if (transcription is null && segment.GetArrayLength() > 3)
                {
                    var rm = segment[3];
                    if (rm.ValueKind == JsonValueKind.String)
                    {
                        var value = rm.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            transcription = value;
                        }
                    }
                }
            }
        }

        var detected = string.Empty;
        if (root.GetArrayLength() > 2 && root[2].ValueKind == JsonValueKind.String)
        {
            detected = root[2].GetString() ?? string.Empty;
        }

        var dictionary = ParseDictionary(root);
        var translated = builder.ToString();

        if (string.IsNullOrEmpty(translated))
        {
            return TranslationResult.Fail(Name, "Пустой перевод");
        }

        return TranslationResult.Ok(Name, translated, detected, transcription, dictionary);
    }

    private static string[]? ParseDictionary(JsonElement root)
    {
        if (root.GetArrayLength() <= 1 || root[1].ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var lines = new List<string>();

        foreach (var entry in root[1].EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Array || entry.GetArrayLength() < 2)
            {
                continue;
            }

            var partOfSpeech = entry[0].ValueKind == JsonValueKind.String ? entry[0].GetString() : null;
            if (entry[1].ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var words = new List<string>();
            foreach (var word in entry[1].EnumerateArray())
            {
                if (word.ValueKind == JsonValueKind.String)
                {
                    var value = word.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        words.Add(value);
                    }
                }
            }

            if (words.Count == 0)
            {
                continue;
            }

            lines.Add(string.IsNullOrWhiteSpace(partOfSpeech)
                ? string.Join(", ", words)
                : partOfSpeech + ": " + string.Join(", ", words));
        }

        return lines.Count > 0 ? lines.ToArray() : null;
    }
}

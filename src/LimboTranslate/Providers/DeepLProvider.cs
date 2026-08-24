using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace LimboTranslate.Providers;

public sealed class DeepLProvider : ITranslationProvider
{
    private const string Endpoint = "https://www2.deepl.com/jsonrpc";
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";

    private static readonly HttpClient Http = CreateClient();

    public string Name => "DeepL";

    public bool RequiresKey => false;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
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
            var id = NextId();
            var payload = BuildPayload(text, from, to, id);

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync(Endpoint, content, ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return TranslationResult.Fail(Name, "DeepL временно недоступен (лимит запросов)");
            }

            if (!response.IsSuccessStatusCode)
            {
                return TranslationResult.Fail(Name, "DeepL вернул код " + (int)response.StatusCode);
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Parse(json, from);
        }
        catch (OperationCanceledException)
        {
            return TranslationResult.Fail(Name, "Запрос отменён");
        }
        catch (Exception ex)
        {
            return TranslationResult.Fail(Name, "DeepL недоступен: " + ex.Message);
        }
    }

    private static long NextId() => Random.Shared.Next(1, 99_999_999) * 10_000L + 1;

    private static long Timestamp(string text)
    {
        var iCount = text.Count(c => c == 'i') + 1;
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return ts + (iCount - ts % iCount);
    }

    private static string BuildPayload(string text, string from, string to, long id)
    {
        var useSource = !string.IsNullOrWhiteSpace(from)
            && !from.Equals("auto", StringComparison.OrdinalIgnoreCase);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WriteString("method", "LMT_handle_jobs");
            writer.WriteNumber("id", id);

            writer.WritePropertyName("params");
            writer.WriteStartObject();

            writer.WritePropertyName("jobs");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("kind", "default");
            writer.WritePropertyName("sentences");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("text", text);
            writer.WriteNumber("id", 1);
            writer.WriteString("prefix", string.Empty);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WritePropertyName("raw_en_context_before");
            writer.WriteStartArray();
            writer.WriteEndArray();
            writer.WritePropertyName("raw_en_context_after");
            writer.WriteStartArray();
            writer.WriteEndArray();
            writer.WriteNumber("preferred_num_beams", 4);
            writer.WriteEndObject();
            writer.WriteEndArray();

            writer.WritePropertyName("lang");
            writer.WriteStartObject();
            writer.WriteString("target_lang", to.ToUpperInvariant());
            writer.WritePropertyName("preferred_langs");
            writer.WriteStartArray();
            writer.WriteEndArray();
            if (useSource)
            {
                writer.WriteString("source_lang_computed", from.ToUpperInvariant());
            }
            writer.WriteEndObject();

            writer.WriteNumber("priority", 1);

            writer.WritePropertyName("commonJobParams");
            writer.WriteStartObject();
            writer.WriteString("mode", "translate");
            writer.WriteEndObject();

            writer.WriteNumber("timestamp", Timestamp(text));
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        var body = Encoding.UTF8.GetString(stream.ToArray());
        return id % 3 == 0
            ? body.Replace("\"method\":\"", "\"method\" : \"", StringComparison.Ordinal)
            : body;
    }

    private TranslationResult Parse(string json, string from)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var error))
        {
            var message = error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString()
                : "неизвестная ошибка";
            return TranslationResult.Fail(Name, "DeepL: " + message);
        }

        if (!root.TryGetProperty("result", out var result))
        {
            return TranslationResult.Fail(Name, "Неожиданный ответ DeepL");
        }

        var detected = from;
        if (result.TryGetProperty("source_lang", out var lang) && lang.ValueKind == JsonValueKind.String)
        {
            detected = (lang.GetString() ?? from).ToLowerInvariant();
        }

        var text = ExtractFromTranslations(result) ?? ExtractFromTexts(result);

        if (string.IsNullOrEmpty(text))
        {
            return TranslationResult.Fail(Name, "Пустой перевод");
        }

        return TranslationResult.Ok(Name, text, detected);
    }

    private static string? ExtractFromTranslations(JsonElement result)
    {
        if (!result.TryGetProperty("translations", out var translations)
            || translations.ValueKind != JsonValueKind.Array
            || translations.GetArrayLength() == 0)
        {
            return null;
        }

        var builder = new StringBuilder();

        foreach (var translation in translations.EnumerateArray())
        {
            if (!translation.TryGetProperty("beams", out var beams)
                || beams.ValueKind != JsonValueKind.Array
                || beams.GetArrayLength() == 0)
            {
                continue;
            }

            var beam = beams[0];

            if (beam.TryGetProperty("sentences", out var sentences)
                && sentences.ValueKind == JsonValueKind.Array)
            {
                foreach (var sentence in sentences.EnumerateArray())
                {
                    if (sentence.TryGetProperty("text", out var st) && st.ValueKind == JsonValueKind.String)
                    {
                        builder.Append(st.GetString());
                    }
                }
            }
            else if (beam.TryGetProperty("postprocessed_sentence", out var post)
                && post.ValueKind == JsonValueKind.String)
            {
                builder.Append(post.GetString());
            }
        }

        var text = builder.ToString();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static string? ExtractFromTexts(JsonElement result)
    {
        if (!result.TryGetProperty("texts", out var texts)
            || texts.ValueKind != JsonValueKind.Array
            || texts.GetArrayLength() == 0)
        {
            return null;
        }

        var first = texts[0];
        if (first.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
        {
            return text.GetString();
        }

        return null;
    }
}

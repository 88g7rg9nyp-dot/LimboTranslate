namespace LimboTranslate.Providers;

public static class Languages
{
    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
    {
        ["auto"] = "Определить язык",
        ["en"] = "Английский",
        ["ru"] = "Русский",
        ["de"] = "Немецкий",
        ["fr"] = "Французский",
        ["es"] = "Испанский",
        ["it"] = "Итальянский",
        ["pt"] = "Португальский",
        ["pl"] = "Польский",
        ["uk"] = "Украинский",
        ["zh"] = "Китайский",
        ["ja"] = "Японский",
        ["ko"] = "Корейский",
        ["tr"] = "Турецкий",
        ["ar"] = "Арабский",
        ["nl"] = "Нидерландский",
        ["cs"] = "Чешский",
        ["sv"] = "Шведский"
    };

    public static string Name(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        return All.TryGetValue(code, out var name) ? name : code;
    }

    public static bool IsRussian(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        int cyrillic = 0;
        int latin = 0;

        foreach (char c in text)
        {
            if (c >= 'а' && c <= 'я' || c >= 'А' && c <= 'Я' || c == 'ё' || c == 'Ё')
            {
                cyrillic++;
            }
            else if (c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z')
            {
                latin++;
            }
        }

        return cyrillic > latin;
    }

    public static string ResolveTarget(string text, string sourceLanguage, string targetLanguage)
    {
        sourceLanguage ??= string.Empty;
        targetLanguage ??= string.Empty;

        bool autoDetect = string.IsNullOrWhiteSpace(sourceLanguage)
            || sourceLanguage.Equals("auto", StringComparison.OrdinalIgnoreCase);

        bool russianSource = sourceLanguage.Equals("ru", StringComparison.OrdinalIgnoreCase)
            || (autoDetect && IsRussian(text));

        if (russianSource && targetLanguage.Equals("ru", StringComparison.OrdinalIgnoreCase))
        {
            return "en";
        }

        return targetLanguage;
    }
}

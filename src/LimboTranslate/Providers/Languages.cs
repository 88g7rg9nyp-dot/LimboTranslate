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
}

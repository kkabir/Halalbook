namespace HalalBook.Shared;

public record LanguageInfo(string Code, string NativeName, string EnglishName, bool RightToLeft);

/// <summary>Languages HalalBook speaks, chosen for where Muslims live around the world.</summary>
public static class Languages
{
    public static readonly IReadOnlyList<LanguageInfo> All =
    [
        new("en", "English", "English", false),
        new("ar", "العربية", "Arabic", true),
        new("ur", "اردو", "Urdu", true),
        new("bn", "বাংলা", "Bengali", false),
        new("id", "Bahasa Indonesia", "Indonesian", false),
        new("ms", "Bahasa Melayu", "Malay", false),
        new("tr", "Türkçe", "Turkish", false),
        new("fa", "فارسی", "Persian", true),
        new("fr", "Français", "French", false),
        new("hi", "हिन्दी", "Hindi", false),
        new("es", "Español", "Spanish", false),
        new("de", "Deutsch", "German", false),
        new("so", "Soomaali", "Somali", false),
        new("ha", "Hausa", "Hausa", false),
        new("sw", "Kiswahili", "Swahili", false),
        new("ru", "Русский", "Russian", false),
        new("zh", "中文", "Chinese", false),
        new("uz", "Oʻzbekcha", "Uzbek", false),
        new("ps", "پښتو", "Pashto", true),
    ];

    /// <summary>The languages a "Reach the whole Ummah" post is published in.</summary>
    public static readonly string[] Ummah = ["en", "ar", "ur", "bn", "id", "tr", "fr", "ms"];

    public static LanguageInfo Get(string? code) =>
        All.FirstOrDefault(l => l.Code == Normalize(code)) ?? All[0];

    /// <summary>"ar-SA" → "ar"; unknown languages fall back to English.</summary>
    public static string Normalize(string? code)
    {
        var c = (code ?? "en").Split('-', '_')[0].ToLowerInvariant();
        return All.Any(l => l.Code == c) ? c : "en";
    }

    /// <summary>Best guess of a text's language from its script, when no AI is available.</summary>
    public static string? GuessFromScript(string text)
    {
        foreach (var ch in text)
        {
            if (ch is >= 'ঀ' and <= '৿') return "bn";
            if (ch is >= 'ऀ' and <= 'ॿ') return "hi";
            if (ch is >= 'Ѐ' and <= 'ӿ') return "ru";
            if (ch is >= '一' and <= '鿿') return "zh";
            if (ch is >= '؀' and <= 'ۿ')
                return text.Any(c => c is 'ٹ' or 'ڈ' or 'ڑ' or 'ں' or 'ے' or 'ھ') ? "ur" : text.Any(c => c is 'پ' or 'چ' or 'ژ' or 'گ') ? "fa" : "ar";
        }
        return null;
    }
}

public record TranslateUiRequest(string Language, List<string> Texts);
public record TranslatedTextDto(string Text, string Language, string? SourceLanguage, bool UsedAi);

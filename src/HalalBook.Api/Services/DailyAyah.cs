using System.Collections.Concurrent;
using System.Text.Json;
using HalalBook.Shared;

namespace HalalBook.Api.Services;

/// <summary>
/// One verse a day for everyone, in the reader's language. The text comes from the Al Quran Cloud API
/// (Uthmani script plus a well-known translation per language), so nothing is typed or generated here.
/// </summary>
public class DailyAyahService(IHttpClientFactory http, ILogger<DailyAyahService> log)
{
    /// <summary>Hand-picked verses of hope, patience, gratitude and good character, rotated by day.</summary>
    private static readonly string[] Verses =
    [
        "2:286", "94:5", "13:28", "65:3", "2:152", "3:139", "39:53", "2:186", "29:69", "49:13",
        "17:23", "31:18", "2:153", "16:97", "20:114", "25:63", "3:159", "2:45", "14:7", "93:5",
        "8:46", "3:200", "2:255", "41:34", "4:36", "16:90", "24:22", "33:41", "40:60", "57:4",
        "6:162", "21:87", "28:24", "3:8", "2:201", "17:80", "12:87", "15:49", "42:43", "49:10",
        "59:18", "64:11", "67:2", "73:20", "103:3", "9:51", "11:115", "18:46", "30:21", "35:2",
    ];

    /// <summary>Translation used for each app language; Arabic readers get the Arabic text alone.</summary>
    private static readonly Dictionary<string, string> Editions = new()
    {
        ["en"] = "en.sahih", ["ur"] = "ur.jalandhry", ["bn"] = "bn.bengali", ["id"] = "id.indonesian", ["ms"] = "ms.basmeih",
        ["tr"] = "tr.diyanet", ["fa"] = "fa.makarem", ["fr"] = "fr.hamidullah", ["hi"] = "hi.hindi", ["es"] = "es.cortes",
        ["de"] = "de.aburida", ["so"] = "so.abduh", ["ha"] = "ha.gumi", ["sw"] = "sw.barwani", ["ru"] = "ru.kuliev",
        ["zh"] = "zh.jian", ["uz"] = "uz.sodik", ["ps"] = "ps.abdulwali",
    };

    private readonly ConcurrentDictionary<string, AyahDto> _cache = new();

    public static string VerseFor(DateOnly day) => Verses[day.DayNumber % Verses.Length];

    public async Task<AyahDto?> TodayAsync(string? language, CancellationToken ct)
    {
        var lang = Languages.Normalize(language);
        var day = DateOnly.FromDateTime(DateTime.UtcNow);
        var key = $"{day:yyyyMMdd}:{lang}";
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var verse = VerseFor(day);
        var edition = Editions.GetValueOrDefault(lang, "en.sahih");
        try
        {
            using var doc = JsonDocument.Parse(await http.CreateClient().GetStringAsync(
                $"https://api.alquran.cloud/v1/ayah/{verse}/editions/quran-uthmani,{edition}", ct));
            var data = doc.RootElement.GetProperty("data");
            var arabic = data[0];
            var surah = arabic.GetProperty("surah");
            var ayah = new AyahDto(
                $"{surah.GetProperty("number").GetInt32()}:{arabic.GetProperty("numberInSurah").GetInt32()}",
                surah.GetProperty("englishName").GetString() ?? "",
                surah.GetProperty("name").GetString() ?? "",
                arabic.GetProperty("text").GetString() ?? "",
                lang == "ar" ? null : data[1].GetProperty("text").GetString(),
                lang);
            // Keep only today's entries.
            foreach (var old in _cache.Keys.Where(k => !k.StartsWith($"{day:yyyyMMdd}:"))) _cache.TryRemove(old, out _);
            return _cache[key] = ayah;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Couldn't load the verse of the day");
            return null;
        }
    }
}

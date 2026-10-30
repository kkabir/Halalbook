using System.Collections.Concurrent;
using System.Net.Http.Json;
using HalalBook.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace HalalBook.Web.Services;

/// <summary>
/// The interface language for one person. Text is written in English in the code and wrapped in T("…").
/// Translations come from the built-in dictionary, then from the API (which asks the AI once and remembers the
/// result for everyone). Missing text is collected while a page renders and fetched in one batch.
/// </summary>
public sealed class I18n(IHttpClientFactory http, IJSRuntime js, ILogger<I18n> log)
{
    // Shared by every visitor: once a phrase is translated it's instant for everyone.
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> Cache = new();
    private static readonly ConcurrentDictionary<string, byte> InFlight = new();

    private readonly ConcurrentDictionary<string, byte> _missing = new();
    private int _scheduled;

    public string Language { get; private set; } = "en";
    public LanguageInfo Info => Languages.Get(Language);
    public bool RightToLeft => Info.RightToLeft;

    /// <summary>Bumped whenever new translations arrive, so pages re-render in the new language.</summary>
    public int Revision { get; private set; }
    public event Action? Changed;

    public string T(string english)
    {
        if (Language == "en" || string.IsNullOrEmpty(english)) return english;
        var dict = Cache.GetOrAdd(Language, l => new(BuiltInTranslations.For(l)));
        if (dict.TryGetValue(english, out var t)) return t;
        if (!InFlight.ContainsKey(Language + "|" + english)) _missing.TryAdd(english, 0);
        ScheduleFetch();
        return english;
    }

    /// <summary>Loads the saved or browser language on first render.</summary>
    public async Task InitializeAsync(string? userLanguage = null)
    {
        string? saved = null;
        try { saved = await js.InvokeAsync<string?>("halalbook.getLanguage"); } catch (JSException) { }
        await SetLanguageAsync(saved ?? userLanguage ?? "en", persist: false);
    }

    public async Task SetLanguageAsync(string code, bool persist = true)
    {
        var lang = Languages.Normalize(code);
        var changed = lang != Language;
        Language = lang;
        try { await js.InvokeVoidAsync("halalbook.setLanguage", lang, Info.RightToLeft, persist); } catch (JSException) { }
        if (changed) { Revision++; Changed?.Invoke(); }
    }

    private void ScheduleFetch()
    {
        if (Interlocked.Exchange(ref _scheduled, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(120); // let the page finish rendering and collect everything it needs
            Interlocked.Exchange(ref _scheduled, 0);
            var lang = Language;
            var batch = _missing.Keys.Take(200).ToList();
            foreach (var k in batch) { _missing.TryRemove(k, out _); InFlight.TryAdd(lang + "|" + k, 0); }
            if (batch.Count == 0) return;
            try
            {
                using var client = http.CreateClient("api");
                var res = await client.PostAsJsonAsync("api/i18n/translate", new TranslateUiRequest(lang, batch));
                if (!res.IsSuccessStatusCode) return;
                var map = await res.Content.ReadFromJsonAsync<Dictionary<string, string>>() ?? [];
                var dict = Cache.GetOrAdd(lang, l => new(BuiltInTranslations.For(l)));
                foreach (var (k, v) in map) if (!string.IsNullOrEmpty(v)) dict[k] = v;
                if (map.Count > 0 && lang == Language) { Revision++; Changed?.Invoke(); }
            }
            catch (Exception ex) { log.LogDebug(ex, "Could not load translations for {Lang}", lang); }
            finally { foreach (var k in batch) InFlight.TryRemove(lang + "|" + k, out _); }
            if (!_missing.IsEmpty) ScheduleFetch();
        });
    }
}

/// <summary>Base for every component: T("…") translates, and the page re-renders when translations arrive.</summary>
public abstract class LocalizedComponent : ComponentBase
{
    [Inject] protected I18n I18n { get; set; } = default!;
    [CascadingParameter(Name = "i18nRev")] public int I18nRevision { get; set; }
    protected string T(string english) => I18n.T(english);
}

/// <summary>Same as <see cref="LocalizedComponent"/>, for layouts.</summary>
public abstract class LocalizedLayout : LayoutComponentBase
{
    [Inject] protected I18n I18n { get; set; } = default!;
    protected string T(string english) => I18n.T(english);
}

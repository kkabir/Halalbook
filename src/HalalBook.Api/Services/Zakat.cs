using System.Text.Json;
using HalalBook.Shared;

namespace HalalBook.Api.Services;

/// <summary>
/// Today's gold and silver prices in the member's currency, for the Zakat calculator's nisab.
/// Prices come from gold-api.com (USD per troy ounce) and exchange rates from open.er-api.com, cached for 6 hours.
/// </summary>
public class NisabService(IHttpClientFactory http, ILogger<NisabService> log)
{
    public const double GoldNisabGrams = 87.48;   // 20 mithqal
    public const double SilverNisabGrams = 612.36; // 200 dirham
    private const double GramsPerTroyOunce = 31.1034768;

    private (DateTime At, double GoldUsd, double SilverUsd, Dictionary<string, double> Rates)? _cache;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<NisabDto?> GetAsync(string? currency, CancellationToken ct)
    {
        var cur = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();
        var data = await PricesAsync(ct);
        if (data is null) return null;
        var (at, goldUsd, silverUsd, rates) = data.Value;
        if (!rates.TryGetValue(cur, out var rate)) (cur, rate) = ("USD", 1);
        var gold = goldUsd / GramsPerTroyOunce * rate;
        var silver = silverUsd / GramsPerTroyOunce * rate;
        return new NisabDto(cur, Math.Round(gold, 2), Math.Round(silver, 4), GoldNisabGrams, SilverNisabGrams,
            Math.Round(gold * GoldNisabGrams, 2), Math.Round(silver * SilverNisabGrams, 2), at,
            rates.Keys.OrderBy(k => k).ToList());
    }

    private async Task<(DateTime, double, double, Dictionary<string, double>)?> PricesAsync(CancellationToken ct)
    {
        if (_cache is { } c && DateTime.UtcNow - c.At < TimeSpan.FromHours(6)) return c;
        await _lock.WaitAsync(ct);
        try
        {
            if (_cache is { } again && DateTime.UtcNow - again.At < TimeSpan.FromHours(6)) return again;
            var client = http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            double Price(string json) => JsonDocument.Parse(json).RootElement.GetProperty("price").GetDouble();
            var gold = Price(await client.GetStringAsync("https://api.gold-api.com/price/XAU", ct));
            var silver = Price(await client.GetStringAsync("https://api.gold-api.com/price/XAG", ct));
            var fx = JsonDocument.Parse(await client.GetStringAsync("https://open.er-api.com/v6/latest/USD", ct)).RootElement.GetProperty("rates");
            var rates = fx.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetDouble());
            _cache = (DateTime.UtcNow, gold, silver, rates);
            return _cache;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Couldn't load gold and silver prices");
            return _cache; // yesterday's prices are better than none
        }
        finally { _lock.Release(); }
    }
}

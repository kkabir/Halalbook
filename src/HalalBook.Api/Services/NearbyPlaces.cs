using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using HalalBook.Shared;

namespace HalalBook.Api.Services;

/// <summary>
/// Masjids and halal food near a point, from OpenStreetMap (Overpass API). Results are cached per ~1 km square
/// for an hour, which keeps HalalBook a light user of the free service.
/// </summary>
public class NearbyPlacesService(IHttpClientFactory http, ILogger<NearbyPlacesService> log)
{
    // The main server first, then public mirrors; the free service is sometimes busy.
    private static readonly string[] Servers =
    [
        "https://overpass-api.de/api/interpreter",
        "https://maps.mail.ru/osm/tools/overpass/api/interpreter",
        "https://overpass.private.coffee/api/interpreter",
    ];
    private readonly ConcurrentDictionary<string, (DateTime At, List<PlaceDto> Places)> _cache = new();

    public async Task<List<PlaceDto>?> FindAsync(double lat, double lng, string kind, int radius, CancellationToken ct)
    {
        radius = Math.Clamp(radius, 500, 15000);
        kind = kind == "food" ? "food" : "masjid";
        var key = FormattableString.Invariant($"{kind}:{Math.Round(lat, 2)}:{Math.Round(lng, 2)}:{radius}");
        if (_cache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < TimeSpan.FromHours(1)) return Sorted(hit.Places, lat, lng);

        var around = FormattableString.Invariant($"around:{radius},{lat},{lng}");
        var query = kind == "food"
            ? $"[out:json][timeout:25];(nwr({around})[\"amenity\"~\"restaurant|fast_food|cafe|food_court\"][\"diet:halal\"~\"yes|only\"];nwr({around})[\"shop\"~\"butcher|supermarket|convenience\"][\"diet:halal\"~\"yes|only\"];);out center 80;"
            : $"[out:json][timeout:25];nwr({around})[\"amenity\"=\"place_of_worship\"][\"religion\"=\"muslim\"];out center 80;";

        foreach (var server in Servers)
        {
            try
            {
                var client = http.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(28);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("HalalBook/1.0 (+https://halalbook.app)");
                using var res = await client.PostAsync(server, new FormUrlEncodedContent([new("data", query)]), ct);
                if (!res.IsSuccessStatusCode) continue;
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
                var places = doc.RootElement.GetProperty("elements").EnumerateArray().Select(ToPlace).OfType<PlaceDto>().ToList();
                _cache[key] = (DateTime.UtcNow, places);
                return Sorted(places, lat, lng);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                log.LogWarning(ex, "Overpass request to {Server} failed", server);
            }
        }
        return hit.Places is { } stale ? Sorted(stale, lat, lng) : null;
    }

    private static PlaceDto? ToPlace(JsonElement e)
    {
        var tags = e.TryGetProperty("tags", out var t) ? t : default;
        string? Tag(string k) => tags.ValueKind == JsonValueKind.Object && tags.TryGetProperty(k, out var v) ? v.GetString() : null;
        double? lat = e.TryGetProperty("lat", out var la) ? la.GetDouble() : e.TryGetProperty("center", out var c) ? c.GetProperty("lat").GetDouble() : null;
        double? lon = e.TryGetProperty("lon", out var lo) ? lo.GetDouble() : e.TryGetProperty("center", out var c2) ? c2.GetProperty("lon").GetDouble() : null;
        if (lat is null || lon is null) return null;
        var address = string.Join(", ", new[] { Tag("addr:housenumber") + " " + Tag("addr:street"), Tag("addr:city") }
            .Select(s => s?.Trim()).Where(s => !string.IsNullOrEmpty(s)));
        var type = Tag("amenity") ?? Tag("shop") ?? "";
        return new PlaceDto(
            Tag("name") ?? Tag("name:en") ?? (type == "place_of_worship" ? "Masjid" : "Halal food"),
            type switch { "place_of_worship" => "Masjid", "fast_food" => "Fast food", "cafe" => "Café", "butcher" => "Halal butcher",
                "supermarket" or "convenience" => "Halal shop", _ => "Restaurant" },
            lat.Value, lon.Value, string.IsNullOrWhiteSpace(address) ? null : address, Tag("opening_hours"), Tag("phone") ?? Tag("contact:phone"),
            Tag("website") ?? Tag("contact:website"), Tag("cuisine")?.Replace(';', ',').Replace('_', ' '), 0);
    }

    private static List<PlaceDto> Sorted(List<PlaceDto> places, double lat, double lng) =>
        places.Select(p => p with { DistanceKm = Math.Round(Km(lat, lng, p.Latitude, p.Longitude), 2) })
            .OrderBy(p => p.DistanceKm).Take(60).ToList();

    public static double Km(double lat1, double lon1, double lat2, double lon2)
    {
        static double R(double d) => d * Math.PI / 180;
        var a = Math.Pow(Math.Sin(R(lat2 - lat1) / 2), 2) + Math.Cos(R(lat1)) * Math.Cos(R(lat2)) * Math.Pow(Math.Sin(R(lon2 - lon1) / 2), 2);
        return 6371 * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}

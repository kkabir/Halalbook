using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HalalBook.Api.Endpoints;
using HalalBook.Shared;

namespace HalalBook.Api.Services;

/// <summary>
/// A TURN relay account, so calls and live rooms connect on mobile data and strict Wi-Fi where direct
/// connections fail. Credentials are short-lived and fetched from the provider, so no long-term secret reaches browsers.
/// </summary>
public class TurnOptions
{
    /// <summary>"cloudflare" or "metered"; empty uses only the STUN servers in Live:IceServers.</summary>
    public string? Provider { get; set; }
    /// <summary>Cloudflare Realtime TURN key: its ID and API token.</summary>
    public string? KeyId { get; set; }
    public string? ApiToken { get; set; }
    /// <summary>Metered: the app name (from {name}.metered.live) and its API key.</summary>
    public string? MeteredApp { get; set; }
    public string? MeteredApiKey { get; set; }
}

public class IceServerProvider(LiveOptions live, TurnOptions turn, IHttpClientFactory http, ILogger<IceServerProvider> log)
{
    private List<IceServerDto>? _relay;
    private DateTime _expires;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<List<IceServerDto>> GetAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(turn.Provider)) return live.IceServers;
        if (_relay is null || DateTime.UtcNow > _expires)
        {
            await _lock.WaitAsync(ct);
            try
            {
                if (_relay is null || DateTime.UtcNow > _expires)
                {
                    _relay = await FetchAsync(ct);
                    _expires = DateTime.UtcNow.AddHours(12); // credentials are requested for 24 hours
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Couldn't get TURN credentials from {Provider}", turn.Provider);
                _expires = DateTime.UtcNow.AddMinutes(2); // try again soon, and use STUN meanwhile
                _relay ??= [];
            }
            finally { _lock.Release(); }
        }
        return [.. live.IceServers, .. _relay];
    }

    private async Task<List<IceServerDto>> FetchAsync(CancellationToken ct)
    {
        var client = http.CreateClient();
        JsonElement json;
        if (turn.Provider!.Equals("cloudflare", StringComparison.OrdinalIgnoreCase))
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"https://rtc.live.cloudflare.com/v1/turn/keys/{turn.KeyId}/credentials/generate")
            {
                Content = JsonContent.Create(new { ttl = 86400 }),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", turn.ApiToken);
            using var res = await client.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();
            json = (await res.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("iceServers");
        }
        else if (turn.Provider.Equals("metered", StringComparison.OrdinalIgnoreCase))
        {
            json = await client.GetFromJsonAsync<JsonElement>(
                $"https://{turn.MeteredApp}.metered.live/api/v1/turn/credentials?apiKey={Uri.EscapeDataString(turn.MeteredApiKey ?? "")}", ct);
        }
        else throw new InvalidOperationException($"Unknown TURN provider '{turn.Provider}'.");
        return Parse(json);
    }

    /// <summary>Accepts one server or a list, with "urls" as a string or a list, as providers differ.</summary>
    public static List<IceServerDto> Parse(JsonElement json)
    {
        var items = json.ValueKind == JsonValueKind.Array ? json.EnumerateArray().ToList() : [json];
        return items.Select(s =>
        {
            var urls = s.GetProperty("urls");
            string[] list = urls.ValueKind == JsonValueKind.Array ? urls.EnumerateArray().Select(u => u.GetString()!).ToArray() : [urls.GetString()!];
            return new IceServerDto(list,
                s.TryGetProperty("username", out var u) ? u.GetString() : null,
                s.TryGetProperty("credential", out var c) ? c.GetString() : null);
        }).ToList();
    }
}

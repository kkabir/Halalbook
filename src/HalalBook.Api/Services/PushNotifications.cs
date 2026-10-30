using System.Text.Json;
using System.Threading.Channels;
using HalalBook.Api.Data;
using Microsoft.EntityFrameworkCore;
using WebPush;

namespace HalalBook.Api.Services;

/// <summary>
/// Phone and desktop notifications (Web Push) for people who aren't looking at HalalBook right now:
/// messages, calls, friend requests and everything else in their notifications list.
/// </summary>
public class PushQueue
{
    public record Item(Guid UserId, string Title, string Body, string Url, string Tag, bool OnlyWhenAway);

    private readonly Channel<Item> _channel = Channel.CreateUnbounded<Item>();
    public ChannelReader<Item> Reader => _channel.Reader;

    /// <param name="onlyWhenAway">Skip people who have HalalBook open, since they already see it in the app.</param>
    public void Enqueue(Guid userId, string title, string body, string url, string tag, bool onlyWhenAway = true) =>
        _channel.Writer.TryWrite(new Item(userId, title, body.Length > 180 ? body[..180] + "…" : body, url, tag, onlyWhenAway));
}

/// <summary>
/// The site's VAPID key pair, which identifies HalalBook to the browsers' push services.
/// Created once and kept in App_Data, so it survives restarts and deploys without any setup.
/// </summary>
public class VapidKeys
{
    public string PublicKey { get; }
    public string PrivateKey { get; }

    public VapidKeys(IConfiguration config, IWebHostEnvironment env)
    {
        if (config["Push:PublicKey"] is { Length: > 0 } pub && config["Push:PrivateKey"] is { Length: > 0 } priv)
        {
            (PublicKey, PrivateKey) = (pub, priv);
            return;
        }
        var file = Path.Combine(env.ContentRootPath, "App_Data", "vapid.json");
        if (File.Exists(file) && JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) is { } saved)
        {
            (PublicKey, PrivateKey) = (saved["publicKey"], saved["privateKey"]);
            return;
        }
        var keys = VapidHelper.GenerateVapidKeys();
        (PublicKey, PrivateKey) = (keys.PublicKey, keys.PrivateKey);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(new Dictionary<string, string> { ["publicKey"] = PublicKey, ["privateKey"] = PrivateKey }));
    }
}

public class PushWorker(PushQueue queue, VapidKeys vapid, PresenceTracker presence, IServiceScopeFactory scopes,
    IConfiguration config, ILogger<PushWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var client = new WebPushClient();
        var details = new VapidDetails($"mailto:{config["Email:ReplyTo"] ?? "support@halalbook.app"}", vapid.PublicKey, vapid.PrivateKey);
        await foreach (var item in queue.Reader.ReadAllAsync(stoppingToken))
        {
            if (item.OnlyWhenAway && presence.IsOnline(item.UserId)) continue;
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var subs = await db.PushSubscriptions.Where(s => s.UserId == item.UserId).ToListAsync(stoppingToken);
                if (subs.Count == 0) continue;
                var payload = JsonSerializer.Serialize(new { title = item.Title, body = item.Body, url = item.Url, tag = item.Tag });
                foreach (var s in subs)
                {
                    try { await client.SendNotificationAsync(new WebPush.PushSubscription(s.Endpoint, s.P256dh, s.Auth), payload, details, stoppingToken); }
                    catch (WebPushException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Gone or System.Net.HttpStatusCode.NotFound)
                    {
                        db.PushSubscriptions.Remove(s); // the person uninstalled or turned notifications off
                    }
                }
                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Push notification failed"); }
        }
    }
}

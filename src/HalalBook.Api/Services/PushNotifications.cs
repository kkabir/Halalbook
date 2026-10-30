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
    public record Item(Guid UserId, string Title, string Body, string Url, string Tag, bool OnlyWhenAway,
        Guid? SenderId = null, bool Urgent = false, bool IsMessage = false, bool IsCall = false);

    private readonly Channel<Item> _channel = Channel.CreateUnbounded<Item>();
    public ChannelReader<Item> Reader => _channel.Reader;

    /// <param name="onlyWhenAway">Skip people who have HalalBook open, since they already see it in the app.</param>
    /// <param name="senderId">Who it's from, so VIPs (Family and Best friends) can get through Do Not Disturb.</param>
    public void Enqueue(Guid userId, string title, string body, string url, string tag, bool onlyWhenAway = true,
        Guid? senderId = null, bool urgent = false, bool isMessage = false, bool isCall = false) =>
        _channel.Writer.TryWrite(new Item(userId, title, body.Length > 180 ? body[..180] + "…" : body, url, tag, onlyWhenAway,
            senderId, urgent, isMessage, isCall));
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
                var user = await db.Users.FindAsync([item.UserId], stoppingToken);
                if (user is null) continue;
                var vip = item.SenderId is Guid from && await IsVipAsync(db, item.UserId, from, stoppingToken);
                var plan = Plan(item, user.NotifyShowPreview, user.NotifyDisappearing, user.DoNotDisturb, user.VipsBreakThrough, vip);
                if (plan is null) continue;
                var payload = JsonSerializer.Serialize(plan);
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

    /// <summary>VIPs are the people in the reader's Family and Best friends lists.</summary>
    public static Task<bool> IsVipAsync(AppDbContext db, Guid reader, Guid sender, CancellationToken ct) =>
        db.FriendListMembers.AnyAsync(m => m.UserId == sender && db.FriendLists.Any(l => l.Id == m.ListId && l.OwnerId == reader
            && (l.Kind == HalalBook.Shared.FriendListKind.Family || l.Kind == HalalBook.Shared.FriendListKind.BestFriends)), ct);

    public record Payload(string Title, string Body, string Url, string Tag, bool Disappear, bool Urgent);

    /// <summary>
    /// Applies the reader's settings: Do Not Disturb holds everything except VIPs (when allowed); urgent only counts from a VIP;
    /// hidden previews replace the text; disappearing alerts close themselves after a few seconds (never calls or urgent ones).
    /// Returns null when the notification shouldn't be sent at all.
    /// </summary>
    public static Payload? Plan(PushQueue.Item item, bool showPreview, bool disappearing, bool doNotDisturb, bool vipsBreakThrough, bool vip)
    {
        var urgent = item.Urgent && vip;
        if (doNotDisturb && !(vip && vipsBreakThrough)) return null;
        var body = showPreview || item.IsCall ? item.Body : item.IsMessage ? "New message" : "You have a new notification";
        var title = item.Urgent && !urgent ? item.Title.Replace("🚨 ", "") : item.Title;
        return new Payload(title, body, item.Url, item.Tag, disappearing && !urgent && !item.IsCall, urgent);
    }
}

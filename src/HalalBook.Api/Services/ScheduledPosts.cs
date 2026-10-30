using HalalBook.Api.Data;
using HalalBook.Shared;
using Microsoft.EntityFrameworkCore;

namespace HalalBook.Api.Services;

/// <summary>Publishes scheduled posts when their time comes: they appear at the top of feeds as brand new.</summary>
public class ScheduledPostPublisher(IServiceScopeFactory scopes, IndexNowQueue indexNow, ILogger<ScheduledPostPublisher> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try { await PublishDueAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Publishing scheduled posts failed"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task<int> PublishDueAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<Notifier>();
        var now = DateTime.UtcNow;
        var due = await db.Posts.Include(p => p.Author).Where(p => p.PublishAt != null && p.PublishAt <= now && !p.IsDeleted).Take(100).ToListAsync(ct);
        foreach (var p in due)
        {
            p.CreatedAt = p.PublishAt!.Value;
            p.PublishAt = null;
        }
        await db.SaveChangesAsync(ct);
        foreach (var p in due)
        {
            await notifier.SendAsync(p.AuthorId, null, NotificationType.System, "Your scheduled post is now live.", $"/post/{p.Id}");
            if (p.Visibility == Visibility.Public && p.Moderation == ModerationStatus.Approved && p.GroupId is null) indexNow.Enqueue($"/p/{p.Id}");
        }
        return due.Count;
    }
}

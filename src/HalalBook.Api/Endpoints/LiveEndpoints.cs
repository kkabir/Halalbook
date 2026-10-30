using System.Security.Claims;
using HalalBook.Api.Data;
using HalalBook.Api.Hubs;
using HalalBook.Api.Services;
using HalalBook.Shared;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HalalBook.Api.Endpoints;

public class LiveOptions
{
    /// <summary>STUN/TURN servers for WebRTC. Add a TURN server for people behind strict firewalls.</summary>
    public List<IceServerDto> IceServers { get; set; } = [new(["stun:stun.l.google.com:19302", "stun:stun1.l.google.com:19302"], null, null)];
}

/// <summary>Live shows, events, podcasts and video conferences.</summary>
public static class LiveEndpoints
{
    public static void MapLiveEndpoints(this IEndpointRouteBuilder app)
    {
        var live = app.MapGroup("/api/live").WithTags("Live").RequireAuthorization();

        live.MapGet("/", async (string? filter, ClaimsPrincipal cp, AppDbContext db, LiveRoomTracker rooms, PostService posts) =>
        {
            var me = cp.UserId();
            var blocked = await posts.BlockedIdsAsync(me);
            var q = db.LiveSessions.Include(s => s.Host).Include(s => s.Group).Where(s => !blocked.Contains(s.HostId));
            q = filter switch
            {
                "upcoming" => q.Where(s => s.Status == LiveStatus.Scheduled).OrderBy(s => s.ScheduledFor),
                "past" => q.Where(s => s.Status == LiveStatus.Ended).OrderByDescending(s => s.EndedAt),
                "mine" => q.Where(s => s.HostId == me).OrderByDescending(s => s.CreatedAt),
                _ => q.Where(s => s.Status == LiveStatus.Live).OrderByDescending(s => s.StartedAt),
            };
            var list = await q.Take(50).ToListAsync();
            return list.Select(s => ToDto(s, me, rooms)).ToList();
        });

        live.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal cp, AppDbContext db, LiveRoomTracker rooms) =>
            await db.LiveSessions.Include(s => s.Host).Include(s => s.Group).FirstOrDefaultAsync(s => s.Id == id) is { } s
                ? Results.Ok(ToDto(s, cp.UserId(), rooms)) : Results.NotFound());

        live.MapPost("/", async (CreateLiveRequest req, ClaimsPrincipal cp, AppDbContext db, AiService ai, Notifier notifier,
            LiveRoomTracker rooms, IHubContext<RealtimeHub> hub, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Title)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["title"] = ["Give your live a title."] });
            var mod = await ai.ModerateAsync($"{req.Title}\n{req.Description}", ct);
            if (!mod.Allowed) return Results.Problem(string.Join(" ", mod.Reasons), statusCode: 422);
            var me = cp.UserId();
            if (req.GroupId is Guid gid && !await db.GroupMembers.AnyAsync(m => m.GroupId == gid && m.UserId == me && m.IsApproved, ct)) return Results.Forbid();

            var s = new LiveSession
            {
                HostId = me, Title = req.Title.Trim(), Description = req.Description, Kind = req.Kind, CoverUrl = req.CoverUrl,
                GroupId = req.GroupId, EventId = req.EventId,
                ScheduledFor = req.StartNow ? null : req.ScheduledFor?.ToUniversalTime(),
                Status = req.StartNow ? LiveStatus.Live : LiveStatus.Scheduled,
                StartedAt = req.StartNow ? DateTime.UtcNow : null,
            };
            db.LiveSessions.Add(s);
            await db.SaveChangesAsync(ct);
            if (req.StartNow) await AnnounceAsync(s, db, notifier);
            await db.Entry(s).Reference(x => x.Host).LoadAsync(ct);
            return Results.Ok(ToDto(s, me, rooms));
        });

        live.MapPost("/{id:guid}/start", async (Guid id, ClaimsPrincipal cp, AppDbContext db, Notifier notifier, LiveRoomTracker rooms) =>
        {
            var s = await db.LiveSessions.Include(x => x.Host).FirstOrDefaultAsync(x => x.Id == id);
            if (s is null) return Results.NotFound();
            if (s.HostId != cp.UserId()) return Results.Forbid();
            if (s.Status == LiveStatus.Ended) return Results.Problem("This live session has already ended.", statusCode: 400);
            if (s.Status != LiveStatus.Live)
            {
                s.Status = LiveStatus.Live;
                s.StartedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
                await AnnounceAsync(s, db, notifier);
            }
            return Results.Ok(ToDto(s, cp.UserId(), rooms));
        });

        live.MapPost("/{id:guid}/end", async (Guid id, ClaimsPrincipal cp, AppDbContext db, IHubContext<LiveHub> liveHub, LiveRoomTracker rooms) =>
        {
            var s = await db.LiveSessions.Include(x => x.Host).FirstOrDefaultAsync(x => x.Id == id);
            if (s is null) return Results.NotFound();
            if (s.HostId != cp.UserId() && !cp.IsModerator()) return Results.Forbid();
            s.Status = LiveStatus.Ended;
            s.EndedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await liveHub.Clients.Group($"live-{id}").SendAsync("ended");
            return Results.Ok(ToDto(s, cp.UserId(), rooms));
        });

        live.MapGet("/ice-servers", (LiveOptions options) => options.IceServers);
    }

    /// <summary>Notifies the host's friends and followers that they're live.</summary>
    private static async Task AnnounceAsync(LiveSession s, AppDbContext db, Notifier notifier)
    {
        var host = await db.Users.FindAsync(s.HostId);
        var friendIds = await db.Friendships.Where(f => f.Accepted && (f.RequesterId == s.HostId || f.AddresseeId == s.HostId))
            .Select(f => f.RequesterId == s.HostId ? f.AddresseeId : f.RequesterId).ToListAsync();
        var followerIds = await db.Follows.Where(f => f.FolloweeId == s.HostId).Select(f => f.FollowerId).ToListAsync();
        var what = s.Kind switch { LiveKind.Podcast => "a live podcast", LiveKind.Conference => "a video call", LiveKind.Event => "a live event", _ => "live" };
        foreach (var uid in friendIds.Concat(followerIds).Distinct().Take(500))
            await notifier.SendAsync(uid, s.HostId, NotificationType.Live, $"{host?.DisplayName} is {what}: {s.Title}", $"/live/{s.Id}");
    }

    private static LiveSessionDto ToDto(LiveSession s, Guid me, LiveRoomTracker rooms) => new(
        s.Id, s.Title, s.Description, s.Kind, s.Status, s.Host.ToSummary(), s.ScheduledFor, s.StartedAt, s.EndedAt,
        rooms.Count(s.Id), s.PeakViewers, s.Group?.ToSummary(), s.EventId, s.CoverUrl, s.HostId == me);
}

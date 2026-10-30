using System.Security.Claims;
using HalalBook.Api.Data;
using HalalBook.Api.Services;
using HalalBook.Shared;
using Microsoft.EntityFrameworkCore;

namespace HalalBook.Api.Endpoints;

/// <summary>Groups, pages and events.</summary>
public static class CommunityEndpoints
{
    public static IQueryable<GroupDto> ToDtos(this IQueryable<Group> q, AppDbContext db, Guid me) =>
        q.Select(g => new GroupDto(g.Id, g.Name, g.Description, g.CoverUrl, g.Privacy, g.Category,
            db.GroupMembers.Count(m => m.GroupId == g.Id && m.IsApproved),
            db.GroupMembers.Any(m => m.GroupId == g.Id && m.UserId == me && m.IsApproved),
            db.GroupMembers.Any(m => m.GroupId == g.Id && m.UserId == me && m.IsApproved && m.Role == MemberRole.Admin),
            db.GroupMembers.Any(m => m.GroupId == g.Id && m.UserId == me && !m.IsApproved),
            g.CreatedAt));

    public static IQueryable<PageDto> ToDtos(this IQueryable<Page> q, AppDbContext db, Guid me) =>
        q.Select(p => new PageDto(p.Id, p.Name, p.Handle, p.Category, p.Description, p.AvatarUrl, p.CoverUrl, p.Website,
            db.PageFollowers.Count(f => f.PageId == p.Id),
            db.PageFollowers.Any(f => f.PageId == p.Id && f.UserId == me),
            p.OwnerId == me, p.IsVerified, p.CreatedAt));

    public static IQueryable<EventDto> ToDtos(this IQueryable<Event> q, AppDbContext db, Guid me) =>
        q.Select(e => new EventDto(e.Id, e.Title, e.Description, e.StartsAt, e.EndsAt, e.Location, e.OnlineUrl, e.CoverUrl, e.Category,
            new UserSummaryDto(e.Host.Id, e.Host.UserName, e.Host.DisplayName, e.Host.AvatarUrl, e.Host.IsVerified),
            e.Group == null ? null : new GroupSummaryDto(e.Group.Id, e.Group.Name, e.Group.CoverUrl),
            e.Page == null ? null : new PageSummaryDto(e.Page.Id, e.Page.Name, e.Page.Handle, e.Page.AvatarUrl),
            db.EventRsvps.Count(r => r.EventId == e.Id && r.Status == RsvpStatus.Going),
            db.EventRsvps.Count(r => r.EventId == e.Id && r.Status == RsvpStatus.Interested),
            db.EventRsvps.Where(r => r.EventId == e.Id && r.UserId == me).Select(r => (RsvpStatus?)r.Status).FirstOrDefault()));

    public static void MapCommunityEndpoints(this IEndpointRouteBuilder app)
    {
        // ---------- Groups ----------
        var groups = app.MapGroup("/api/groups").WithTags("Groups").RequireAuthorization();

        groups.MapGet("/", async (string? filter, ClaimsPrincipal cp, AppDbContext db) =>
        {
            var me = cp.UserId();
            var q = db.Groups.AsQueryable();
            if (filter == "mine") q = q.Where(g => db.GroupMembers.Any(m => m.GroupId == g.Id && m.UserId == me && m.IsApproved));
            return await q.OrderByDescending(g => db.GroupMembers.Count(m => m.GroupId == g.Id && m.IsApproved)).Take(50).ToDtos(db, me).ToListAsync();
        });

        groups.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal cp, AppDbContext db) =>
            await db.Groups.Where(g => g.Id == id).ToDtos(db, cp.UserId()).FirstOrDefaultAsync() is { } g ? Results.Ok(g) : Results.NotFound());

        groups.MapPost("/", async (CreateGroupRequest req, ClaimsPrincipal cp, AppDbContext db, AiService ai, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name your group."] });
            var mod = await ai.ModerateAsync($"{req.Name}\n{req.Description}", ct);
            if (!mod.Allowed) return Results.Problem(string.Join(" ", mod.Reasons), statusCode: 422);
            var me = cp.UserId();
            var g = new Group { Name = req.Name.Trim(), Description = req.Description, CoverUrl = req.CoverUrl, Privacy = req.Privacy, Category = req.Category, OwnerId = me };
            db.Groups.Add(g);
            db.GroupMembers.Add(new GroupMember { GroupId = g.Id, UserId = me, Role = MemberRole.Admin, IsApproved = true });
            await db.SaveChangesAsync();
            return Results.Ok(await db.Groups.Where(x => x.Id == g.Id).ToDtos(db, me).FirstAsync());
        });

        groups.MapPost("/{id:guid}/join", async (Guid id, ClaimsPrincipal cp, AppDbContext db, Notifier notifier) =>
        {
            var me = cp.UserId();
            var g = await db.Groups.FindAsync(id);
            if (g is null) return Results.NotFound();
            if (!await db.GroupMembers.AnyAsync(m => m.GroupId == id && m.UserId == me))
            {
                db.GroupMembers.Add(new GroupMember { GroupId = id, UserId = me, IsApproved = g.Privacy == GroupPrivacy.Public });
                await db.SaveChangesAsync();
                if (g.Privacy == GroupPrivacy.Private)
                {
                    var meUser = (await db.Users.FindAsync(me))!;
                    await notifier.SendAsync(g.OwnerId, me, NotificationType.GroupInvite, $"{meUser.DisplayName} asked to join {g.Name}.", $"/groups/{id}");
                }
            }
            return Results.Ok();
        });

        groups.MapDelete("/{id:guid}/join", async (Guid id, ClaimsPrincipal cp, AppDbContext db) =>
        {
            var me = cp.UserId();
            var g = await db.Groups.FindAsync(id);
            if (g is null) return Results.NotFound();
            if (g.OwnerId == me) return Results.Problem("The owner can't leave the group.", statusCode: 400);
            await db.GroupMembers.Where(m => m.GroupId == id && m.UserId == me).ExecuteDeleteAsync();
            return Results.NoContent();
        });

        groups.MapGet("/{id:guid}/members", async (Guid id, bool? pending, ClaimsPrincipal cp, AppDbContext db) =>
        {
            var me = cp.UserId();
            var isAdmin = await db.GroupMembers.AnyAsync(m => m.GroupId == id && m.UserId == me && m.Role == MemberRole.Admin);
            var wantPending = pending == true && isAdmin;
            return await db.GroupMembers.Where(m => m.GroupId == id && m.IsApproved != wantPending)
                .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { User = u.ToSummary(), m.Role, m.JoinedAt })
                .Take(200).ToListAsync();
        });

        groups.MapPost("/{id:guid}/members/{userId:guid}/approve", async (Guid id, Guid userId, ClaimsPrincipal cp, AppDbContext db, Notifier notifier) =>
        {
            var me = cp.UserId();
            if (!await db.GroupMembers.AnyAsync(m => m.GroupId == id && m.UserId == me && m.Role == MemberRole.Admin)) return Results.Forbid();
            var member = await db.GroupMembers.FirstOrDefaultAsync(m => m.GroupId == id && m.UserId == userId);
            if (member is null) return Results.NotFound();
            member.IsApproved = true;
            await db.SaveChangesAsync();
            var g = (await db.Groups.FindAsync(id))!;
            await notifier.SendAsync(userId, me, NotificationType.GroupInvite, $"You're now a member of {g.Name}.", $"/groups/{id}");
            return Results.Ok();
        });

        groups.MapGet("/{id:guid}/posts", async (Guid id, int? page, ClaimsPrincipal cp, PostService svc) =>
        {
            var visible = await svc.VisibleToAsync(cp.UserId(), cp.IsModerator());
            return await svc.PageAsync(visible.Where(p => p.GroupId == id), cp.UserId(), Math.Max(1, page ?? 1), 15);
        });

        // ---------- Pages ----------
        var pages = app.MapGroup("/api/pages").WithTags("Pages").RequireAuthorization();

        pages.MapGet("/", async (string? filter, ClaimsPrincipal cp, AppDbContext db) =>
        {
            var me = cp.UserId();
            var q = db.Pages.AsQueryable();
            if (filter == "mine") q = q.Where(p => p.OwnerId == me);
            if (filter == "following") q = q.Where(p => db.PageFollowers.Any(f => f.PageId == p.Id && f.UserId == me));
            return await q.OrderByDescending(p => db.PageFollowers.Count(f => f.PageId == p.Id)).Take(50).ToDtos(db, me).ToListAsync();
        });

        pages.MapGet("/{handle}", async (string handle, ClaimsPrincipal cp, AppDbContext db) =>
            await db.Pages.Where(p => p.Handle == handle.ToLower()).ToDtos(db, cp.UserId()).FirstOrDefaultAsync() is { } p ? Results.Ok(p) : Results.NotFound());

        pages.MapPost("/", async (CreatePageRequest req, ClaimsPrincipal cp, AppDbContext db, AiService ai, CancellationToken ct) =>
        {
            var handle = (req.Handle ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(req.Name) || handle.Length < 3 || !handle.All(c => char.IsLetterOrDigit(c) || c is '_' or '.'))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["handle"] = ["Enter a name and a handle of 3+ letters, numbers, dots or underscores."] });
            if (await db.Pages.AnyAsync(p => p.Handle == handle)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["handle"] = ["That handle is taken."] });
            var mod = await ai.ModerateAsync($"{req.Name}\n{req.Description}", ct);
            if (!mod.Allowed) return Results.Problem(string.Join(" ", mod.Reasons), statusCode: 422);
            var me = cp.UserId();
            var p = new Page { Name = req.Name.Trim(), Handle = handle, Category = req.Category, Description = req.Description, AvatarUrl = req.AvatarUrl, CoverUrl = req.CoverUrl, Website = req.Website, OwnerId = me };
            db.Pages.Add(p);
            db.PageFollowers.Add(new PageFollower { PageId = p.Id, UserId = me });
            await db.SaveChangesAsync();
            return Results.Ok(await db.Pages.Where(x => x.Id == p.Id).ToDtos(db, me).FirstAsync());
        });

        pages.MapPost("/{id:guid}/follow", async (Guid id, ClaimsPrincipal cp, AppDbContext db) =>
        {
            var me = cp.UserId();
            if (!await db.Pages.AnyAsync(p => p.Id == id)) return Results.NotFound();
            if (!await db.PageFollowers.AnyAsync(f => f.PageId == id && f.UserId == me))
            {
                db.PageFollowers.Add(new PageFollower { PageId = id, UserId = me });
                await db.SaveChangesAsync();
            }
            return Results.Ok();
        });

        pages.MapDelete("/{id:guid}/follow", async (Guid id, ClaimsPrincipal cp, AppDbContext db) =>
        {
            await db.PageFollowers.Where(f => f.PageId == id && f.UserId == cp.UserId()).ExecuteDeleteAsync();
            return Results.NoContent();
        });

        pages.MapGet("/{id:guid}/posts", async (Guid id, int? page, ClaimsPrincipal cp, PostService svc) =>
        {
            var visible = await svc.VisibleToAsync(cp.UserId(), cp.IsModerator());
            return await svc.PageAsync(visible.Where(p => p.PageId == id), cp.UserId(), Math.Max(1, page ?? 1), 15);
        });

        // ---------- Events ----------
        var events = app.MapGroup("/api/events").WithTags("Events").RequireAuthorization();

        events.MapGet("/", async (string? filter, Guid? groupId, ClaimsPrincipal cp, AppDbContext db) =>
        {
            var me = cp.UserId();
            var now = DateTime.UtcNow.AddHours(-6);
            var q = db.Events.Where(e => (e.EndsAt ?? e.StartsAt) >= now);
            if (groupId is Guid gid) q = q.Where(e => e.GroupId == gid);
            if (filter == "mine") q = q.Where(e => e.HostId == me || db.EventRsvps.Any(r => r.EventId == e.Id && r.UserId == me && r.Status != RsvpStatus.NotGoing));
            return await q.OrderBy(e => e.StartsAt).Take(50).ToDtos(db, me).ToListAsync();
        });

        events.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal cp, AppDbContext db) =>
            await db.Events.Where(e => e.Id == id).ToDtos(db, cp.UserId()).FirstOrDefaultAsync() is { } e ? Results.Ok(e) : Results.NotFound());

        events.MapPost("/", async (CreateEventRequest req, ClaimsPrincipal cp, AppDbContext db, AiService ai, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Title)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["title"] = ["Give the event a title."] });
            var mod = await ai.ModerateAsync($"{req.Title}\n{req.Description}", ct);
            if (!mod.Allowed) return Results.Problem(string.Join(" ", mod.Reasons), statusCode: 422);
            var me = cp.UserId();
            if (req.GroupId is Guid gid && !await db.GroupMembers.AnyAsync(m => m.GroupId == gid && m.UserId == me && m.IsApproved)) return Results.Forbid();
            if (req.PageId is Guid pid && !await db.Pages.AnyAsync(p => p.Id == pid && p.OwnerId == me)) return Results.Forbid();
            var e = new Event
            {
                Title = req.Title.Trim(), Description = req.Description, StartsAt = req.StartsAt.ToUniversalTime(), EndsAt = req.EndsAt?.ToUniversalTime(),
                Location = req.Location, OnlineUrl = req.OnlineUrl, CoverUrl = req.CoverUrl, Category = req.Category,
                HostId = me, GroupId = req.GroupId, PageId = req.PageId,
            };
            db.Events.Add(e);
            db.EventRsvps.Add(new EventRsvp { EventId = e.Id, UserId = me, Status = RsvpStatus.Going });
            await db.SaveChangesAsync();
            return Results.Ok(await db.Events.Where(x => x.Id == e.Id).ToDtos(db, me).FirstAsync());
        });

        events.MapPut("/{id:guid}/rsvp", async (Guid id, RsvpRequest req, ClaimsPrincipal cp, AppDbContext db) =>
        {
            var me = cp.UserId();
            if (!await db.Events.AnyAsync(e => e.Id == id)) return Results.NotFound();
            var r = await db.EventRsvps.FirstOrDefaultAsync(x => x.EventId == id && x.UserId == me);
            if (r is null) db.EventRsvps.Add(new EventRsvp { EventId = id, UserId = me, Status = req.Status });
            else r.Status = req.Status;
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        events.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal cp, AppDbContext db) =>
        {
            var e = await db.Events.FindAsync(id);
            if (e is null) return Results.NotFound();
            if (e.HostId != cp.UserId() && !cp.IsModerator()) return Results.Forbid();
            await db.EventRsvps.Where(r => r.EventId == id).ExecuteDeleteAsync();
            db.Events.Remove(e);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}

using System.Security.Claims;
using System.Text.RegularExpressions;
using HalalBook.Api.Data;
using HalalBook.Api.Services;
using HalalBook.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HalalBook.Api.Endpoints;

public static partial class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapAuthEndpoints();

        var users = app.MapGroup("/api/users").WithTags("Users").RequireAuthorization();

        users.MapGet("/me", async (ClaimsPrincipal cp, AppDbContext db) =>
            await db.Users.FindAsync(cp.UserId()) is { } u ? Results.Ok(u.ToDto()) : Results.Unauthorized());

        users.MapPut("/me", async (UpdateProfileRequest req, ClaimsPrincipal cp, AppDbContext db) =>
        {
            var u = await db.Users.FindAsync(cp.UserId());
            if (u is null) return Results.Unauthorized();
            if (!string.IsNullOrWhiteSpace(req.DisplayName)) u.DisplayName = req.DisplayName.Trim();
            u.Bio = req.Bio ?? u.Bio;
            u.AvatarUrl = req.AvatarUrl ?? u.AvatarUrl;
            u.CoverUrl = req.CoverUrl ?? u.CoverUrl;
            u.Location = req.Location ?? u.Location;
            u.Website = req.Website ?? u.Website;
            if (req.WhoCanPost is { } who) u.WhoCanPost = who;
            u.Latitude = req.Latitude ?? u.Latitude;
            u.Longitude = req.Longitude ?? u.Longitude;
            if (req.PrayerMethod is not null) u.PrayerMethod = PrayerTimeCalculator.GetMethod(req.PrayerMethod).Key;
            if (req.AsrSchool is "Hanafi" or "Shafi") u.AsrSchool = req.AsrSchool;
            await db.SaveChangesAsync();
            return Results.Ok(u.ToDto());
        });

        users.MapGet("/{userName}", async (string userName, ClaimsPrincipal cp, AppDbContext db, PostService posts, PresenceTracker presence) =>
        {
            var me = cp.UserId();
            var key = userName.ToUpperInvariant();
            var u = await db.Users.FirstOrDefaultAsync(x => x.NormalizedUserName == key);
            if (u is null || (await posts.BlockedIdsAsync(me)).Contains(u.Id)) return Results.NotFound();

            var f = await db.Friendships.FirstOrDefaultAsync(x =>
                (x.RequesterId == me && x.AddresseeId == u.Id) || (x.RequesterId == u.Id && x.AddresseeId == me));
            var status = u.Id == me ? FriendshipStatus.Self
                : f is null ? FriendshipStatus.None
                : f.Accepted ? FriendshipStatus.Accepted
                : f.RequesterId == me ? FriendshipStatus.OutgoingRequest : FriendshipStatus.IncomingRequest;

            return Results.Ok(new ProfileDto(u.ToSummary(), u.Bio, u.CoverUrl, u.Location, u.Website, u.CreatedAt,
                await db.Friendships.CountAsync(x => x.Accepted && (x.RequesterId == u.Id || x.AddresseeId == u.Id)),
                await db.Follows.CountAsync(x => x.FolloweeId == u.Id),
                await db.Follows.CountAsync(x => x.FollowerId == u.Id),
                await db.Posts.CountAsync(p => p.AuthorId == u.Id && !p.IsDeleted && p.GroupId == null && p.PageId == null),
                status,
                await db.Follows.AnyAsync(x => x.FollowerId == me && x.FolloweeId == u.Id),
                await db.Blocks.AnyAsync(x => x.BlockerId == me && x.BlockedId == u.Id),
                await PresenceEndpoints.CanSeePresence(db, me, u) && presence.IsOnline(u.Id),
                await PresenceEndpoints.CanSeePresence(db, me, u) ? u.LastSeenAt : null,
                u.Id != me && await CanPostOnProfileAsync(db, me, u.Id) is null));
        });

        users.MapGet("/{userName}/posts", async (string userName, int? page, ClaimsPrincipal cp, AppDbContext db, PostService posts) =>
        {
            var key = userName.ToUpperInvariant();
            var u = await db.Users.FirstOrDefaultAsync(x => x.NormalizedUserName == key);
            if (u is null) return Results.NotFound();
            var visible = await posts.VisibleToAsync(cp.UserId(), cp.IsModerator());
            return Results.Ok(await posts.PageAsync(visible.Where(p => ((p.AuthorId == u.Id && p.TargetUserId == null) || p.TargetUserId == u.Id) && p.GroupId == null && p.PageId == null), cp.UserId(), page ?? 1, 15));
        });

        users.MapGet("/{userName}/friends", async (string userName, AppDbContext db, PostService posts) =>
        {
            var key = userName.ToUpperInvariant();
            var u = await db.Users.FirstOrDefaultAsync(x => x.NormalizedUserName == key);
            if (u is null) return Results.NotFound();
            var ids = await posts.FriendIdsAsync(u.Id);
            return Results.Ok(await db.Users.Where(x => ids.Contains(x.Id)).OrderBy(x => x.DisplayName).Select(x => x.ToSummary()).ToListAsync());
        });

        // People you may know: friends of friends ranked by mutual friend count.
        users.MapGet("/suggestions", async (ClaimsPrincipal cp, AppDbContext db, PostService posts) =>
        {
            var me = cp.UserId();
            var friends = await posts.FriendIdsAsync(me);
            var blocked = await posts.BlockedIdsAsync(me);
            var related = await db.Friendships.Where(f => f.RequesterId == me || f.AddresseeId == me)
                .Select(f => f.RequesterId == me ? f.AddresseeId : f.RequesterId).ToListAsync();
            var fof = await db.Friendships
                .Where(f => f.Accepted && (friends.Contains(f.RequesterId) || friends.Contains(f.AddresseeId)))
                .Select(f => friends.Contains(f.RequesterId) ? f.AddresseeId : f.RequesterId).ToListAsync();
            var ranked = fof.Where(id => id != me && !related.Contains(id) && !blocked.Contains(id))
                .GroupBy(id => id).OrderByDescending(g => g.Count()).Take(12).Select(g => g.Key).ToList();
            if (ranked.Count < 12)
                ranked.AddRange(await db.Users.Where(u => u.Id != me && !related.Contains(u.Id) && !blocked.Contains(u.Id) && !ranked.Contains(u.Id))
                    .OrderByDescending(u => u.CreatedAt).Take(12 - ranked.Count).Select(u => u.Id).ToListAsync());
            var users = await db.Users.Where(u => ranked.Contains(u.Id)).ToListAsync();
            return Results.Ok(ranked.Select(id => users.First(u => u.Id == id).ToSummary()).ToList());
        });

        // ---- Friends ----
        var friends = app.MapGroup("/api/friends").WithTags("Friends").RequireAuthorization();

        friends.MapGet("/", async (ClaimsPrincipal cp, AppDbContext db, PostService posts) =>
        {
            var ids = await posts.FriendIdsAsync(cp.UserId());
            return await db.Users.Where(u => ids.Contains(u.Id)).OrderBy(u => u.DisplayName).Select(u => u.ToSummary()).ToListAsync();
        });

        friends.MapGet("/requests", async (ClaimsPrincipal cp, AppDbContext db) =>
        {
            var me = cp.UserId();
            return await db.Friendships.Include(f => f.Requester).Include(f => f.Addressee)
                .Where(f => !f.Accepted && (f.AddresseeId == me || f.RequesterId == me))
                .OrderByDescending(f => f.CreatedAt)
                .Select(f => new FriendRequestDto(f.Id, f.Requester.ToSummary(), f.Addressee.ToSummary(), f.CreatedAt)).ToListAsync();
        });

        friends.MapPost("/{userId:guid}", async (Guid userId, ClaimsPrincipal cp, AppDbContext db, Notifier notifier) =>
        {
            var me = cp.UserId();
            if (userId == me || !await db.Users.AnyAsync(u => u.Id == userId)) return Results.BadRequest();
            if (await db.Blocks.AnyAsync(b => (b.BlockerId == me && b.BlockedId == userId) || (b.BlockerId == userId && b.BlockedId == me))) return Results.Forbid();
            var existing = await db.Friendships.FirstOrDefaultAsync(f =>
                (f.RequesterId == me && f.AddresseeId == userId) || (f.RequesterId == userId && f.AddresseeId == me));
            var meUser = (await db.Users.FindAsync(me))!;
            if (existing is not null)
            {
                // Sending a request to someone who already asked you accepts theirs.
                if (!existing.Accepted && existing.RequesterId == userId)
                {
                    existing.Accepted = true;
                    await db.SaveChangesAsync();
                    await notifier.SendAsync(userId, me, NotificationType.FriendAccepted, $"{meUser.DisplayName} accepted your friend request.", $"/u/{meUser.UserName}");
                }
                return Results.Ok();
            }
            db.Friendships.Add(new Friendship { RequesterId = me, AddresseeId = userId });
            if (!await db.Follows.AnyAsync(f => f.FollowerId == me && f.FolloweeId == userId))
                db.Follows.Add(new Follow { FollowerId = me, FolloweeId = userId });
            await db.SaveChangesAsync();
            await notifier.SendAsync(userId, me, NotificationType.FriendRequest, $"{meUser.DisplayName} sent you a friend request.", "/friends");
            return Results.Ok();
        });

        friends.MapPost("/{userId:guid}/accept", async (Guid userId, ClaimsPrincipal cp, AppDbContext db, Notifier notifier) =>
        {
            var me = cp.UserId();
            var f = await db.Friendships.FirstOrDefaultAsync(x => x.RequesterId == userId && x.AddresseeId == me && !x.Accepted);
            if (f is null) return Results.NotFound();
            f.Accepted = true;
            if (!await db.Follows.AnyAsync(x => x.FollowerId == me && x.FolloweeId == userId))
                db.Follows.Add(new Follow { FollowerId = me, FolloweeId = userId });
            await db.SaveChangesAsync();
            var meUser = (await db.Users.FindAsync(me))!;
            await notifier.SendAsync(userId, me, NotificationType.FriendAccepted, $"{meUser.DisplayName} accepted your friend request.", $"/u/{meUser.UserName}");
            return Results.Ok();
        });

        // Decline, cancel or unfriend.
        friends.MapDelete("/{userId:guid}", async (Guid userId, ClaimsPrincipal cp, AppDbContext db) =>
        {
            var me = cp.UserId();
            var f = await db.Friendships.FirstOrDefaultAsync(x =>
                (x.RequesterId == me && x.AddresseeId == userId) || (x.RequesterId == userId && x.AddresseeId == me));
            if (f is null) return Results.NotFound();
            db.Friendships.Remove(f);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // ---- Follow / block ----
        users.MapPost("/{userId:guid}/follow", async (Guid userId, ClaimsPrincipal cp, AppDbContext db, Notifier notifier) =>
        {
            var me = cp.UserId();
            if (userId == me || !await db.Users.AnyAsync(u => u.Id == userId)) return Results.BadRequest();
            if (await db.Follows.AnyAsync(f => f.FollowerId == me && f.FolloweeId == userId)) return Results.Ok();
            db.Follows.Add(new Follow { FollowerId = me, FolloweeId = userId });
            await db.SaveChangesAsync();
            var meUser = (await db.Users.FindAsync(me))!;
            await notifier.SendAsync(userId, me, NotificationType.Follow, $"{meUser.DisplayName} started following you.", $"/u/{meUser.UserName}");
            return Results.Ok();
        });

        users.MapDelete("/{userId:guid}/follow", async (Guid userId, ClaimsPrincipal cp, AppDbContext db) =>
        {
            await db.Follows.Where(f => f.FollowerId == cp.UserId() && f.FolloweeId == userId).ExecuteDeleteAsync();
            return Results.NoContent();
        });

        users.MapPost("/{userId:guid}/block", async (Guid userId, ClaimsPrincipal cp, AppDbContext db) =>
        {
            var me = cp.UserId();
            if (userId == me) return Results.BadRequest();
            if (!await db.Blocks.AnyAsync(b => b.BlockerId == me && b.BlockedId == userId))
                db.Blocks.Add(new Block { BlockerId = me, BlockedId = userId });
            await db.Friendships.Where(x => (x.RequesterId == me && x.AddresseeId == userId) || (x.RequesterId == userId && x.AddresseeId == me)).ExecuteDeleteAsync();
            await db.Follows.Where(x => (x.FollowerId == me && x.FolloweeId == userId) || (x.FollowerId == userId && x.FolloweeId == me)).ExecuteDeleteAsync();
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        users.MapDelete("/{userId:guid}/block", async (Guid userId, ClaimsPrincipal cp, AppDbContext db) =>
        {
            await db.Blocks.Where(b => b.BlockerId == cp.UserId() && b.BlockedId == userId).ExecuteDeleteAsync();
            return Results.NoContent();
        });

        users.MapGet("/me/blocked", async (ClaimsPrincipal cp, AppDbContext db) =>
        {
            var me = cp.UserId();
            return await db.Blocks.Where(b => b.BlockerId == me)
                .Join(db.Users, b => b.BlockedId, u => u.Id, (b, u) => u)
                .OrderBy(u => u.DisplayName).Select(u => u.ToSummary()).ToListAsync();
        });
    }

    /// <summary>Returns why <paramref name="me"/> can't post on <paramref name="ownerId"/>'s profile, or null if they can.</summary>
    public static async Task<string?> CanPostOnProfileAsync(AppDbContext db, Guid me, Guid ownerId)
    {
        var owner = await db.Users.FindAsync(ownerId);
        if (owner is null) return "That person doesn't exist.";
        if (await db.Blocks.AnyAsync(b => (b.BlockerId == me && b.BlockedId == ownerId) || (b.BlockerId == ownerId && b.BlockedId == me)))
            return "You can't post on this profile.";
        return owner.WhoCanPost switch
        {
            ProfilePostPermission.Everyone => null,
            ProfilePostPermission.OnlyMe => $"{owner.DisplayName} doesn't allow posts on their profile.",
            _ => await db.Friendships.AnyAsync(f => f.Accepted && ((f.RequesterId == me && f.AddresseeId == ownerId) || (f.RequesterId == ownerId && f.AddresseeId == me)))
                ? null : $"Only {owner.DisplayName}'s friends can post on their profile.",
        };
    }
}

using System.Security.Claims;
using HalalBook.Api.Data;
using HalalBook.Api.Services;
using HalalBook.Shared;
using Microsoft.EntityFrameworkCore;

namespace HalalBook.Api.Endpoints;

/// <summary>
/// Facebook-style active status. People who hide their status appear offline, and (as on Facebook)
/// they also can't see anyone else's.
/// </summary>
public static class PresenceEndpoints
{
    public static void MapPresenceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/presence/online", async (ClaimsPrincipal cp, AppDbContext db, PostService posts, PresenceTracker presence) =>
        {
            var me = cp.UserId();
            if (!await db.Users.AnyAsync(u => u.Id == me && u.ShowOnlineStatus)) return Results.Ok(new List<OnlineUserDto>());

            var online = presence.OnlineUserIds.Where(id => id != me).ToList();
            var blocked = await posts.BlockedIdsAsync(me);
            var friends = (await posts.FriendIdsAsync(me)).ToHashSet();
            var users = await db.Users
                .Where(u => online.Contains(u.Id) && u.ShowOnlineStatus && !u.IsSuspended && !blocked.Contains(u.Id))
                .ToListAsync();
            return Results.Ok(users
                .OrderByDescending(u => friends.Contains(u.Id)).ThenBy(u => u.DisplayName)
                .Take(100)
                .Select(u => new OnlineUserDto(u.ToSummary(), friends.Contains(u.Id)))
                .ToList());
        }).WithTags("Presence").RequireAuthorization();

        app.MapPut("/api/users/me/online-status", async (OnlineStatusRequest req, ClaimsPrincipal cp, AppDbContext db) =>
        {
            var user = await db.Users.FindAsync(cp.UserId());
            if (user is null) return Results.Unauthorized();
            user.ShowOnlineStatus = req.Visible;
            await db.SaveChangesAsync();
            return Results.Ok(user.ToDto());
        }).WithTags("Presence").RequireAuthorization();
    }

    /// <summary>Whether <paramref name="viewerId"/> may see <paramref name="target"/>'s active status.</summary>
    public static async Task<bool> CanSeePresence(AppDbContext db, Guid viewerId, User target)
    {
        if (target.Id == viewerId) return true;
        if (!target.ShowOnlineStatus) return false;
        if (!await db.Users.AnyAsync(u => u.Id == viewerId && u.ShowOnlineStatus)) return false;
        return !await db.Blocks.AnyAsync(b => (b.BlockerId == viewerId && b.BlockedId == target.Id) || (b.BlockerId == target.Id && b.BlockedId == viewerId));
    }
}

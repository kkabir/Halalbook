using System.Security.Claims;
using HalalBook.Api.Data;
using HalalBook.Api.Services;
using HalalBook.Shared;
using Microsoft.EntityFrameworkCore;

namespace HalalBook.Api.Endpoints;

/// <summary>Family, Close friends, Best friends and custom friend lists.</summary>
public static class FriendListEndpoints
{
    private static readonly (FriendListKind Kind, string Name, string Icon)[] Defaults =
    [
        (FriendListKind.Family, "Family", "👪"),
        (FriendListKind.CloseFriends, "Close friends", "⭐"),
        (FriendListKind.BestFriends, "Best friends", "💚"),
    ];

    public static void MapFriendListEndpoints(this IEndpointRouteBuilder app)
    {
        var lists = app.MapGroup("/api/friend-lists").WithTags("Friend lists").RequireAuthorization();

        lists.MapGet("/", async (ClaimsPrincipal cp, AppDbContext db) =>
        {
            var me = cp.UserId();
            // Everyone starts with Family, Close friends and Best friends.
            var existing = await db.FriendLists.Where(l => l.OwnerId == me).Select(l => l.Kind).ToListAsync();
            foreach (var d in Defaults.Where(d => !existing.Contains(d.Kind)))
                db.FriendLists.Add(new FriendList { OwnerId = me, Kind = d.Kind, Name = d.Name, Icon = d.Icon });
            await db.SaveChangesAsync();
            return await Load(db.FriendLists.Where(l => l.OwnerId == me));
        });

        lists.MapPost("/", async (SaveFriendListRequest req, ClaimsPrincipal cp, AppDbContext db) =>
        {
            var name = (req.Name ?? "").Trim();
            if (name.Length is 0 or > 60) return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Give the list a name (up to 60 characters)."] });
            var me = cp.UserId();
            if (await db.FriendLists.CountAsync(l => l.OwnerId == me) >= 50) return Results.Problem("You can have up to 50 lists.", statusCode: 400);
            var list = new FriendList { OwnerId = me, Name = name, Kind = FriendListKind.Custom, Icon = string.IsNullOrWhiteSpace(req.Icon) ? "📋" : req.Icon.Trim() };
            db.FriendLists.Add(list);
            await db.SaveChangesAsync();
            return Results.Ok((await Load(db.FriendLists.Where(l => l.Id == list.Id))).Single());
        });

        lists.MapPut("/{id:guid}", async (Guid id, SaveFriendListRequest req, ClaimsPrincipal cp, AppDbContext db) =>
        {
            var list = await db.FriendLists.FirstOrDefaultAsync(l => l.Id == id && l.OwnerId == cp.UserId());
            if (list is null) return Results.NotFound();
            if (!string.IsNullOrWhiteSpace(req.Name)) list.Name = req.Name.Trim()[..Math.Min(60, req.Name.Trim().Length)];
            if (!string.IsNullOrWhiteSpace(req.Icon)) list.Icon = req.Icon.Trim();
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        lists.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal cp, AppDbContext db) =>
        {
            var list = await db.FriendLists.FirstOrDefaultAsync(l => l.Id == id && l.OwnerId == cp.UserId());
            if (list is null) return Results.NotFound();
            if (list.Kind != FriendListKind.Custom) return Results.Problem("Family, Close friends and Best friends can't be deleted, but you can empty them.", statusCode: 400);
            // Posts shared with the list become visible to only you.
            await db.Posts.Where(p => p.FriendListId == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Visibility, Visibility.OnlyMe).SetProperty(p => p.FriendListId, (Guid?)null));
            db.FriendLists.Remove(list);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        lists.MapPost("/{id:guid}/members/{userId:guid}", async (Guid id, Guid userId, ClaimsPrincipal cp, AppDbContext db, PostService posts) =>
        {
            var me = cp.UserId();
            if (!await db.FriendLists.AnyAsync(l => l.Id == id && l.OwnerId == me)) return Results.NotFound();
            if (!(await posts.FriendIdsAsync(me)).Contains(userId)) return Results.Problem("You can only add friends to your lists.", statusCode: 400);
            if (!await db.FriendListMembers.AnyAsync(m => m.ListId == id && m.UserId == userId))
            {
                db.FriendListMembers.Add(new FriendListMember { ListId = id, UserId = userId });
                await db.SaveChangesAsync();
            }
            return Results.Ok();
        });

        lists.MapDelete("/{id:guid}/members/{userId:guid}", async (Guid id, Guid userId, ClaimsPrincipal cp, AppDbContext db) =>
        {
            if (!await db.FriendLists.AnyAsync(l => l.Id == id && l.OwnerId == cp.UserId())) return Results.NotFound();
            await db.FriendListMembers.Where(m => m.ListId == id && m.UserId == userId).ExecuteDeleteAsync();
            return Results.NoContent();
        });
    }

    private static async Task<List<FriendListDto>> Load(IQueryable<FriendList> q) =>
        (await q.Include(l => l.Members).ThenInclude(m => m.User).OrderBy(l => l.Kind).ThenBy(l => l.Name).AsSplitQuery().ToListAsync())
        .Select(l => new FriendListDto(l.Id, l.Name, l.Kind, l.Icon, l.Members.Select(m => m.User.ToSummary()).OrderBy(u => u.DisplayName).ToList()))
        .ToList();
}

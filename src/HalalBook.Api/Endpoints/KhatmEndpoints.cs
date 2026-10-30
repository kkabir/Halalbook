using System.Security.Claims;
using HalalBook.Api.Data;
using HalalBook.Api.Services;
using HalalBook.Shared;
using Microsoft.EntityFrameworkCore;

namespace HalalBook.Api.Endpoints;

/// <summary>
/// Quran khatm together: a group shares the 30 juz of one complete recitation, often dedicated to someone
/// (a late parent, a sick friend, a new baby). Anyone with the link can claim a juz and mark it read.
/// </summary>
public static class KhatmEndpoints
{
    public static void MapKhatmEndpoints(this IEndpointRouteBuilder app)
    {
        var khatms = app.MapGroup("/api/khatms").WithTags("Quran khatm").RequireAuthorization();

        // Khatms I started or joined, and open ones from my friends.
        khatms.MapGet("/", async (ClaimsPrincipal cp, AppDbContext db) =>
        {
            var me = cp.UserId();
            var friends = await db.Friendships.Where(f => f.Accepted && (f.RequesterId == me || f.AddresseeId == me))
                .Select(f => f.RequesterId == me ? f.AddresseeId : f.RequesterId).ToListAsync();
            var list = await db.Khatms.Include(k => k.Creator).Include(k => k.Parts)
                .Where(k => k.CreatorId == me || k.Parts.Any(p => p.UserId == me) || (friends.Contains(k.CreatorId) && k.CompletedAt == null))
                .OrderByDescending(k => k.CreatedAt).Take(50).AsSplitQuery().ToListAsync();
            var users = await UsersFor(db, list.SelectMany(k => k.Parts));
            return list.Select(k => ToDto(k, me, users)).ToList();
        });

        khatms.MapPost("/", async (CreateKhatmRequest req, ClaimsPrincipal cp, AppDbContext db) =>
        {
            var title = (req.Title ?? "").Trim();
            if (title.Length is 0 or > 120) return Results.ValidationProblem(new Dictionary<string, string[]> { ["title"] = ["Give the khatm a name (up to 120 characters)."] });
            var k = new Khatm
            {
                Title = title, Intention = string.IsNullOrWhiteSpace(req.Intention) ? null : req.Intention.Trim()[..Math.Min(req.Intention.Trim().Length, 300)],
                DueDate = req.DueDate, CreatorId = cp.UserId(),
            };
            k.Parts.AddRange(Enumerable.Range(1, 30).Select(n => new KhatmPart { Juz = n }));
            db.Khatms.Add(k);
            await db.SaveChangesAsync();
            return Results.Ok(await LoadAsync(db, k.Id, cp.UserId()));
        });

        khatms.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal cp, AppDbContext db) =>
            await LoadAsync(db, id, cp.UserId()) is { } k ? Results.Ok(k) : Results.NotFound());

        // action: claim (take this juz), done (finished reading it), release (give it back)
        khatms.MapPost("/{id:guid}/juz/{juz:int}/{action}", async (Guid id, int juz, string action, ClaimsPrincipal cp, AppDbContext db, Notifier notifier) =>
        {
            var me = cp.UserId();
            var k = await db.Khatms.Include(x => x.Parts).FirstOrDefaultAsync(x => x.Id == id);
            var part = k?.Parts.FirstOrDefault(p => p.Juz == juz);
            if (k is null || part is null) return Results.NotFound();
            if (k.CompletedAt is not null) return Results.Problem("This khatm is already complete. Alhamdulillah!", statusCode: 409);

            switch (action)
            {
                case "claim" when part.UserId is null:
                    (part.UserId, part.ClaimedAt) = (me, DateTime.UtcNow);
                    break;
                case "claim":
                    return Results.Problem("Someone has already taken this juz. Please choose another.", statusCode: 409);
                case "done" when part.UserId is null || part.UserId == me:
                    (part.UserId, part.ClaimedAt, part.DoneAt) = (me, part.ClaimedAt ?? DateTime.UtcNow, DateTime.UtcNow);
                    break;
                case "release" when part.UserId == me || k.CreatorId == me:
                    (part.UserId, part.ClaimedAt, part.DoneAt) = (null, null, null);
                    break;
                default:
                    return Results.Forbid();
            }

            if (k.Parts.All(p => p.DoneAt is not null))
            {
                k.CompletedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
                foreach (var uid in k.Parts.Select(p => p.UserId!.Value).Append(k.CreatorId).Distinct())
                    await notifier.SendAsync(uid, null, NotificationType.System,
                        $"The khatm \"{k.Title}\" is complete! All 30 juz have been read. Alhamdulillah 🎉 Time for the khatm dua.", $"/khatm/{k.Id}");
            }
            else await db.SaveChangesAsync();
            return Results.Ok(await LoadAsync(db, id, me));
        });
    }

    private static async Task<KhatmDto?> LoadAsync(AppDbContext db, Guid id, Guid me)
    {
        var k = await db.Khatms.Include(x => x.Creator).Include(x => x.Parts).FirstOrDefaultAsync(x => x.Id == id);
        return k is null ? null : ToDto(k, me, await UsersFor(db, k.Parts));
    }

    private static async Task<Dictionary<Guid, UserSummaryDto>> UsersFor(AppDbContext db, IEnumerable<KhatmPart> parts)
    {
        var ids = parts.Where(p => p.UserId is not null).Select(p => p.UserId!.Value).Distinct().ToList();
        return (await db.Users.Where(u => ids.Contains(u.Id)).ToListAsync()).ToDictionary(u => u.Id, u => u.ToSummary());
    }

    private static KhatmDto ToDto(Khatm k, Guid me, Dictionary<Guid, UserSummaryDto> users) => new(
        k.Id, k.Title, k.Intention, k.DueDate, k.Creator.ToSummary(), k.CreatedAt, k.CompletedAt,
        k.Parts.OrderBy(p => p.Juz).Select(p => new KhatmPartDto(p.Juz,
            p.UserId is Guid u && users.TryGetValue(u, out var who) ? who : null, p.DoneAt is not null, p.UserId == me)).ToList());
}

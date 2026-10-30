using System.Security.Claims;
using System.Text.Json;
using HalalBook.Api.Data;
using HalalBook.Api.Services;
using HalalBook.Shared;
using Microsoft.EntityFrameworkCore;

namespace HalalBook.Api.Endpoints;

/// <summary>
/// Marketplace: members buy and sell locally. Listings are checked like posts (no alcohol, pork, weapons, adult items or gambling),
/// and buyers contact sellers through Messages.
/// </summary>
public static class MarketEndpoints
{
    public static readonly string[] Categories =
        ["Modest fashion", "Books", "Electronics", "Home & furniture", "Vehicles", "Kids & baby", "Halal food", "Services", "Property", "Other"];

    /// <summary>Items that can't be sold on HalalBook, matched as whole words.</summary>
    private static readonly System.Text.RegularExpressions.Regex NotAllowed = new(
        @"\b(alcohol|beers?|wines?|whiske?y|vodka|rum|pork|bacon|ham|guns?|firearms?|ammo|ammunition|casino|lottery|betting|cigarettes?|vapes?|cannabis|weed)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    public static void MapMarketEndpoints(this IEndpointRouteBuilder app)
    {
        var market = app.MapGroup("/api/market").WithTags("Marketplace").RequireAuthorization();

        market.MapGet("/categories", () => Categories);

        market.MapGet("/", async (string? q, string? category, Guid? seller, int? page, ClaimsPrincipal cp, AppDbContext db, PostService svc) =>
        {
            var me = cp.UserId();
            var blocked = await svc.BlockedIdsAsync(me);
            var query = db.Listings.Include(l => l.Seller)
                .Where(l => !blocked.Contains(l.SellerId) && !l.Seller.IsSuspended && (l.Status == ListingStatus.Active || l.SellerId == me));
            if (seller is Guid s) query = query.Where(l => l.SellerId == s);
            else query = query.Where(l => l.Status == ListingStatus.Active);
            if (!string.IsNullOrWhiteSpace(category)) query = query.Where(l => l.Category == category);
            if (!string.IsNullOrWhiteSpace(q)) query = query.Where(l => l.Title.Contains(q) || l.Description.Contains(q) || (l.Location != null && l.Location.Contains(q)));
            var size = 24;
            var p = Math.Max(1, page ?? 1);
            var items = await query.OrderByDescending(l => l.CreatedAt).Skip((p - 1) * size).Take(size + 1).ToListAsync();
            return new PagedResult<ListingDto>(items.Take(size).Select(l => ToDto(l, me)).ToList(), p, size, items.Count > size);
        });

        market.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal cp, AppDbContext db, PostService svc) =>
        {
            var me = cp.UserId();
            var l = await db.Listings.Include(x => x.Seller).FirstOrDefaultAsync(x => x.Id == id);
            if (l is null || l.Status == ListingStatus.Removed && l.SellerId != me) return Results.NotFound();
            if ((await svc.BlockedIdsAsync(me)).Contains(l.SellerId)) return Results.NotFound();
            return Results.Ok(ToDto(l, me));
        });

        market.MapPost("/", async (CreateListingRequest req, ClaimsPrincipal cp, AppDbContext db, AiService ai, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            var title = (req.Title ?? "").Trim();
            if (title.Length is < 3 or > 100) errors["title"] = ["Give it a title (3 to 100 characters)."];
            if (req.Price is < 0 or > 100_000_000) errors["price"] = ["Enter a price (0 for free)."];
            if (!Categories.Contains(req.Category)) errors["category"] = ["Choose a category."];
            if ((req.Photos?.Count ?? 0) == 0) errors["photos"] = ["Add at least one photo."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var text = $"{title}\n{req.Description}";
            if (NotAllowed.Match(text) is { Success: true } banned)
                return Results.Problem($"HalalBook Marketplace doesn't allow this kind of item (\"{banned.Value}\"). No alcohol, pork, weapons, tobacco, gambling or adult items.", statusCode: 422);
            var currency = (req.Currency ?? "USD").Trim().ToUpperInvariant();
            var moderation = await ai.ModerateAsync(text, ct);
            if (!moderation.Allowed) return Results.Problem(string.Join(" ", moderation.Reasons), statusCode: 422, title: "Listing not allowed");

            var l = new Listing
            {
                SellerId = cp.UserId(), Title = title, Description = (req.Description ?? "").Trim()[..Math.Min((req.Description ?? "").Trim().Length, 3000)],
                Price = Math.Round(req.Price, 2), Currency = currency.Length == 3 ? currency : "USD", Category = req.Category,
                Condition = req.Condition, Location = req.Location?.Trim(), Photos = JsonSerializer.Serialize(req.Photos!.Take(8).ToList()),
            };
            db.Listings.Add(l);
            await db.SaveChangesAsync(ct);
            l.Seller = (await db.Users.FindAsync([l.SellerId], ct))!;
            return Results.Ok(ToDto(l, l.SellerId));
        });

        market.MapPut("/{id:guid}/status", async (Guid id, ListingStatusRequest req, ClaimsPrincipal cp, AppDbContext db) =>
        {
            var l = await db.Listings.Include(x => x.Seller).FirstOrDefaultAsync(x => x.Id == id);
            if (l is null) return Results.NotFound();
            if (l.SellerId != cp.UserId() && !cp.IsModerator()) return Results.Forbid();
            l.Status = req.Status;
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(l, cp.UserId()));
        });
    }

    private static ListingDto ToDto(Listing l, Guid me) => new(
        l.Id, l.Seller.ToSummary(), l.Title, l.Description, l.Price, l.Currency, l.Category, l.Condition, l.Location,
        JsonSerializer.Deserialize<List<string>>(l.Photos) ?? [], l.Status, l.CreatedAt, l.SellerId == me);
}

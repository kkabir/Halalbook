using System.Security.Claims;
using System.Text.Json;

using HalalBook.Api.Data;
using HalalBook.Api.Services;
using HalalBook.Shared;
using Microsoft.EntityFrameworkCore;

namespace HalalBook.Api.Endpoints;

/// <summary>
/// Marketplace: members buy and sell locally. Listings are checked like posts (no alcohol, pork, weapons, adult items or gambling),
/// and buyers contact sellers through Messages. Beyond the basics: AI writes a listing from a photo and estimates a fair price,
/// buyers filter by price and distance, save items (and hear about price drops), make offers, rate sellers, and set search alerts.
/// </summary>
public static class MarketEndpoints
{
    public static readonly string[] Categories =
        ["Modest fashion", "Books", "Electronics", "Home & furniture", "Vehicles", "Kids & baby", "Halal food", "Services", "Property", "Free & giving", "Other"];

    /// <summary>Items that can't be sold on HalalBook, matched as whole words.</summary>
    private static readonly System.Text.RegularExpressions.Regex NotAllowed = new(
        @"\b(alcohol|beers?|wines?|whiske?y|vodka|rum|pork|bacon|ham|guns?|firearms?|ammo|ammunition|casino|lottery|betting|cigarettes?|vapes?|cannabis|weed)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    public static void MapMarketEndpoints(this IEndpointRouteBuilder app)
    {
        var market = app.MapGroup("/api/market").WithTags("Marketplace").RequireAuthorization();

        market.MapGet("/categories", () => Categories);

        // sort: new (default), price, price_desc, near. lat/lng: where the buyer is, for distance and "near". saved: only my saved items.
        market.MapGet("/", async (string? q, string? category, Guid? seller, decimal? minPrice, decimal? maxPrice, bool? free, string? sort,
            double? lat, double? lng, double? radiusKm, bool? saved, int? page, ClaimsPrincipal cp, AppDbContext db, PostService svc) =>
        {
            var me = cp.UserId();
            var blocked = await svc.BlockedIdsAsync(me);
            var query = db.Listings.Include(l => l.Seller)
                .Where(l => !blocked.Contains(l.SellerId) && !l.Seller.IsSuspended && (l.Status == ListingStatus.Active || l.SellerId == me));
            if (seller is Guid s) query = query.Where(l => l.SellerId == s && l.Status != ListingStatus.Removed);
            else query = query.Where(l => l.Status == ListingStatus.Active);
            if (saved == true) query = query.Where(l => db.SavedListings.Any(x => x.UserId == me && x.ListingId == l.Id));
            if (!string.IsNullOrWhiteSpace(category)) query = query.Where(l => l.Category == category);
            if (!string.IsNullOrWhiteSpace(q)) query = query.Where(l => l.Title.Contains(q) || l.Description.Contains(q) || (l.Location != null && l.Location.Contains(q)));
            if (free == true) query = query.Where(l => l.Price == 0);
            if (minPrice is decimal min) query = query.Where(l => l.Price >= min);
            if (maxPrice is decimal max) query = query.Where(l => l.Price <= max);

            var candidates = await query.OrderByDescending(l => l.CreatedAt).Take(600).ToListAsync();
            double? Distance(Listing l) => lat is double la && lng is double lo && l.Latitude is double a && l.Longitude is double b
                ? NearbyPlacesService.Km(la, lo, a, b) : null;
            var withDistance = candidates.Select(l => (l, km: Distance(l))).ToList();
            if (radiusKm is double r and > 0) withDistance = withDistance.Where(x => x.km is null || x.km <= r).ToList();
            withDistance = sort switch
            {
                "price" => withDistance.OrderBy(x => x.l.Price).ToList(),
                "price_desc" => withDistance.OrderByDescending(x => x.l.Price).ToList(),
                "near" => withDistance.OrderBy(x => x.km ?? double.MaxValue).ToList(),
                _ => withDistance,
            };
            const int size = 24;
            var p = Math.Max(1, page ?? 1);
            var pageItems = withDistance.Skip((p - 1) * size).Take(size).ToList();
            var extras = await ExtrasAsync(db, me, pageItems.Select(x => x.l).ToList());
            return new PagedResult<ListingDto>(pageItems.Select(x => ToDto(x.l, me, extras, x.km)).ToList(), p, size, withDistance.Count > p * size);
        });

        market.MapGet("/{id:guid}", async (Guid id, double? lat, double? lng, ClaimsPrincipal cp, AppDbContext db, PostService svc) =>
        {
            var me = cp.UserId();
            var l = await db.Listings.Include(x => x.Seller).FirstOrDefaultAsync(x => x.Id == id);
            if (l is null || l.Status == ListingStatus.Removed && l.SellerId != me) return Results.NotFound();
            if ((await svc.BlockedIdsAsync(me)).Contains(l.SellerId)) return Results.NotFound();
            if (l.SellerId != me) { l.Views++; await db.SaveChangesAsync(); }
            var km = lat is double la && lng is double lo && l.Latitude is double a && l.Longitude is double b ? NearbyPlacesService.Km(la, lo, a, b) : (double?)null;
            return Results.Ok(ToDto(l, me, await ExtrasAsync(db, me, [l], withTrust: true), km));
        });

        // ---- AI: write the listing from a photo ----
        market.MapPost("/ai-draft", async (ListingDraftRequest req, IWebHostEnvironment env, AiService ai, CancellationToken ct) =>
        {
            var currency = NormalizeCurrency(req.Currency);
            var path = UploadedFilePath(env, req.PhotoUrl);
            if (path is null || !File.Exists(path)) return Results.BadRequest("Upload the photo first.");
            var draft = await ai.DraftListingAsync(await File.ReadAllBytesAsync(path, ct), MimeFor(path), currency, ct);
            return Results.Ok(draft ?? new ListingDraftDto("", "", "Other", null, null, null, currency, false));
        }).RequireRateLimiting("ai");

        market.MapPost("/", async (CreateListingRequest req, ClaimsPrincipal cp, AppDbContext db, AiService ai, Notifier notifier, PushQueue push, CancellationToken ct) =>
        {
            var errors = Validate(req.Title, req.Price, req.Category, req.Photos);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var text = $"{req.Title}\n{req.Description}";
            if (await RefuseAsync(text, ai, ct) is { } refused) return refused;

            var l = new Listing
            {
                SellerId = cp.UserId(), Title = req.Title.Trim(), Description = Clip(req.Description, 3000),
                Price = Math.Round(req.Price, 2), Currency = NormalizeCurrency(req.Currency), Category = req.Category,
                Condition = req.Condition, Location = req.Location?.Trim(), Photos = JsonSerializer.Serialize(req.Photos!.Take(8).ToList()),
                Latitude = req.Latitude, Longitude = req.Longitude, FairLow = req.FairLow, FairHigh = req.FairHigh,
            };
            db.Listings.Add(l);
            await db.SaveChangesAsync(ct);
            l.Seller = (await db.Users.FindAsync([l.SellerId], ct))!;
            await NotifyAlertsAsync(db, notifier, l);
            return Results.Ok(ToDto(l, l.SellerId, await ExtrasAsync(db, l.SellerId, [l]), null));
        });

        // Edit; lowering the price tells everyone who saved the item.
        market.MapPut("/{id:guid}", async (Guid id, CreateListingRequest req, ClaimsPrincipal cp, AppDbContext db, AiService ai, Notifier notifier, CancellationToken ct) =>
        {
            var me = cp.UserId();
            var l = await db.Listings.Include(x => x.Seller).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (l is null) return Results.NotFound();
            if (l.SellerId != me) return Results.Forbid();
            var errors = Validate(req.Title, req.Price, req.Category, req.Photos);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            if (await RefuseAsync($"{req.Title}\n{req.Description}", ai, ct) is { } refused) return refused;

            var oldPrice = l.Price;
            (l.Title, l.Description, l.Category, l.Condition, l.Location) = (req.Title.Trim(), Clip(req.Description, 3000), req.Category, req.Condition, req.Location?.Trim());
            l.Photos = JsonSerializer.Serialize(req.Photos!.Take(8).ToList());
            if (req.Latitude is not null) (l.Latitude, l.Longitude) = (req.Latitude, req.Longitude);
            var newPrice = Math.Round(req.Price, 2);
            if (newPrice < oldPrice) l.WasPrice = oldPrice;
            else if (newPrice > oldPrice) l.WasPrice = null;
            l.Price = newPrice;
            await db.SaveChangesAsync(ct);
            if (newPrice < oldPrice)
            {
                var savers = await db.SavedListings.Where(x => x.ListingId == id && x.UserId != me).Select(x => x.UserId).ToListAsync(ct);
                foreach (var uid in savers)
                    await notifier.SendAsync(uid, me, NotificationType.System,
                        $"Price drop 💸 \"{l.Title}\" is now {Money(newPrice, l.Currency)} (was {Money(oldPrice, l.Currency)}).", $"/market/{l.Id}");
            }
            return Results.Ok(ToDto(l, me, await ExtrasAsync(db, me, [l], withTrust: true), null));
        });

        market.MapPut("/{id:guid}/status", async (Guid id, ListingStatusRequest req, ClaimsPrincipal cp, AppDbContext db) =>
        {
            var l = await db.Listings.Include(x => x.Seller).FirstOrDefaultAsync(x => x.Id == id);
            if (l is null) return Results.NotFound();
            if (l.SellerId != cp.UserId() && !cp.IsModerator()) return Results.Forbid();
            l.Status = req.Status;
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(l, cp.UserId(), await ExtrasAsync(db, cp.UserId(), [l], withTrust: true), null));
        });

        // ---- Save for later ----
        market.MapPost("/{id:guid}/save", async (Guid id, ClaimsPrincipal cp, AppDbContext db) =>
        {
            var me = cp.UserId();
            if (!await db.Listings.AnyAsync(l => l.Id == id)) return Results.NotFound();
            if (!await db.SavedListings.AnyAsync(x => x.UserId == me && x.ListingId == id))
            {
                db.SavedListings.Add(new SavedListing { UserId = me, ListingId = id });
                await db.SaveChangesAsync();
            }
            return Results.NoContent();
        });
        market.MapDelete("/{id:guid}/save", async (Guid id, ClaimsPrincipal cp, AppDbContext db) =>
        {
            await db.SavedListings.Where(x => x.UserId == cp.UserId() && x.ListingId == id).ExecuteDeleteAsync();
            return Results.NoContent();
        });

        // ---- Offers ----
        market.MapPost("/{id:guid}/offers", async (Guid id, MakeOfferRequest req, ClaimsPrincipal cp, AppDbContext db, Notifier notifier) =>
        {
            var me = cp.UserId();
            var l = await db.Listings.FirstOrDefaultAsync(x => x.Id == id);
            if (l is null || l.Status != ListingStatus.Active) return Results.NotFound();
            if (l.SellerId == me) return Results.BadRequest("You can't make an offer on your own item.");
            if (req.Amount <= 0 || req.Amount > 100_000_000) return Results.BadRequest("Enter an amount.");
            // One open offer per buyer: a new one replaces the old.
            await db.ListingOffers.Where(o => o.ListingId == id && o.BuyerId == me && o.Status == OfferStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OfferStatus.Withdrawn));
            var offer = new ListingOffer { ListingId = id, BuyerId = me, Amount = Math.Round(req.Amount, 2), Note = Clip(req.Note, 300) };
            db.ListingOffers.Add(offer);
            await db.SaveChangesAsync();
            var buyer = (await db.Users.FindAsync(me))!;
            await notifier.SendAsync(l.SellerId, me, NotificationType.System,
                $"🤝 {buyer.DisplayName} offered {Money(offer.Amount, l.Currency)} for \"{l.Title}\".", $"/market/{l.Id}");
            offer.Buyer = buyer;
            return Results.Ok(ToDto(offer, l.Currency));
        });

        // The seller sees every offer; a buyer sees their own.
        market.MapGet("/{id:guid}/offers", async (Guid id, ClaimsPrincipal cp, AppDbContext db) =>
        {
            var me = cp.UserId();
            var l = await db.Listings.FindAsync(id);
            if (l is null) return Results.NotFound();
            var offers = await db.ListingOffers.Include(o => o.Buyer)
                .Where(o => o.ListingId == id && (l.SellerId == me || o.BuyerId == me) && o.Status != OfferStatus.Withdrawn)
                .OrderByDescending(o => o.CreatedAt).Take(50).ToListAsync();
            return Results.Ok(offers.Select(o => ToDto(o, l.Currency)).ToList());
        });

        // Seller accepts or declines; the buyer can withdraw. Accepting marks the item sold and declines the rest.
        market.MapPut("/offers/{offerId:guid}", async (Guid offerId, OfferDecisionRequest req, ClaimsPrincipal cp, AppDbContext db, Notifier notifier) =>
        {
            var me = cp.UserId();
            var o = await db.ListingOffers.Include(x => x.Listing).Include(x => x.Buyer).FirstOrDefaultAsync(x => x.Id == offerId);
            if (o is null) return Results.NotFound();
            var isSeller = o.Listing.SellerId == me;
            var allowed = req.Status switch
            {
                OfferStatus.Accepted or OfferStatus.Declined => isSeller,
                OfferStatus.Withdrawn => o.BuyerId == me,
                _ => false,
            };
            if (!allowed || o.Status != OfferStatus.Pending) return Results.Forbid();
            o.Status = req.Status;
            if (req.Status == OfferStatus.Accepted)
            {
                o.Listing.Status = ListingStatus.Sold;
                var others = await db.ListingOffers.Where(x => x.ListingId == o.ListingId && x.Id != o.Id && x.Status == OfferStatus.Pending).ToListAsync();
                foreach (var x in others)
                {
                    x.Status = OfferStatus.Declined;
                    await notifier.SendAsync(x.BuyerId, me, NotificationType.System, $"\"{o.Listing.Title}\" has been sold to someone else.", $"/market/{o.ListingId}");
                }
            }
            await db.SaveChangesAsync();
            if (isSeller)
                await notifier.SendAsync(o.BuyerId, me, NotificationType.System, req.Status == OfferStatus.Accepted
                    ? $"🎉 Your offer of {Money(o.Amount, o.Listing.Currency)} for \"{o.Listing.Title}\" was accepted! Message the seller to arrange pickup."
                    : $"Your offer for \"{o.Listing.Title}\" was declined. You can make another one.", $"/market/{o.ListingId}");
            return Results.Ok(ToDto(o, o.Listing.Currency));
        });

        // ---- Seller ratings: buyers whose offer was accepted ----
        market.MapPost("/{id:guid}/review", async (Guid id, ReviewSellerRequest req, ClaimsPrincipal cp, AppDbContext db, Notifier notifier) =>
        {
            var me = cp.UserId();
            var l = await db.Listings.FindAsync(id);
            if (l is null) return Results.NotFound();
            if (!await db.ListingOffers.AnyAsync(o => o.ListingId == id && o.BuyerId == me && o.Status == OfferStatus.Accepted))
                return Results.Problem("Only the buyer whose offer was accepted can rate this sale.", statusCode: 403);
            if (req.Stars is < 1 or > 5) return Results.BadRequest("Choose 1 to 5 stars.");
            var review = await db.SellerReviews.FirstOrDefaultAsync(r => r.ListingId == id && r.ReviewerId == me);
            if (review is null) db.SellerReviews.Add(review = new SellerReview { ListingId = id, SellerId = l.SellerId, ReviewerId = me });
            (review.Stars, review.Text) = (req.Stars, Clip(req.Text, 500));
            await db.SaveChangesAsync();
            await notifier.SendAsync(l.SellerId, me, NotificationType.System, $"⭐ You got a {req.Stars}-star rating for \"{l.Title}\".", $"/market?seller={l.SellerId}");
            return Results.NoContent();
        });

        market.MapGet("/sellers/{sellerId:guid}/reviews", async (Guid sellerId, AppDbContext db) =>
        {
            var reviews = await db.SellerReviews.Include(r => r.Reviewer).Where(r => r.SellerId == sellerId)
                .OrderByDescending(r => r.CreatedAt).Take(30).ToListAsync();
            var titles = await db.Listings.Where(l => reviews.Select(r => r.ListingId).Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Title);
            return reviews.Select(r => new SellerReviewDto(r.Reviewer.ToSummary(), r.Stars, r.Text, r.CreatedAt, titles.GetValueOrDefault(r.ListingId, ""))).ToList();
        });

        // ---- Search alerts ----
        market.MapGet("/alerts", async (ClaimsPrincipal cp, AppDbContext db) =>
            await db.SearchAlerts.Where(a => a.UserId == cp.UserId()).OrderByDescending(a => a.CreatedAt)
                .Select(a => new SearchAlertDto(a.Id, a.Query, a.Category, a.MaxPrice, a.RadiusKm, a.CreatedAt)).ToListAsync());
        market.MapPost("/alerts", async (SearchAlertRequest req, ClaimsPrincipal cp, AppDbContext db) =>
        {
            var me = cp.UserId();
            if (string.IsNullOrWhiteSpace(req.Query) && string.IsNullOrWhiteSpace(req.Category)) return Results.BadRequest("Say what you're looking for.");
            if (await db.SearchAlerts.CountAsync(a => a.UserId == me) >= 20) return Results.BadRequest("You can keep up to 20 alerts.");
            var a = new SearchAlert
            {
                UserId = me, Query = Clip(req.Query, 100), Category = Categories.Contains(req.Category) ? req.Category : null, MaxPrice = req.MaxPrice,
                Latitude = req.Latitude, Longitude = req.Longitude, RadiusKm = req.RadiusKm is > 0 ? req.RadiusKm : null,
            };
            db.SearchAlerts.Add(a);
            await db.SaveChangesAsync();
            return Results.Ok(new SearchAlertDto(a.Id, a.Query, a.Category, a.MaxPrice, a.RadiusKm, a.CreatedAt));
        });
        market.MapDelete("/alerts/{alertId:guid}", async (Guid alertId, ClaimsPrincipal cp, AppDbContext db) =>
        {
            await db.SearchAlerts.Where(a => a.Id == alertId && a.UserId == cp.UserId()).ExecuteDeleteAsync();
            return Results.NoContent();
        });
    }

    /// <summary>Tells everyone whose search alert matches a new listing.</summary>
    private static async Task NotifyAlertsAsync(AppDbContext db, Notifier notifier, Listing l)
    {
        var alerts = await db.SearchAlerts.Where(a => a.UserId != l.SellerId && (a.Category == null || a.Category == l.Category)
            && (a.MaxPrice == null || l.Price <= a.MaxPrice)).ToListAsync();
        var text = $"{l.Title} {l.Description}";
        foreach (var a in alerts.Where(a => (a.Query is null || a.Query.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => text.Contains(w, StringComparison.OrdinalIgnoreCase)))
                     && (a.RadiusKm is null || a.Latitude is null || l.Latitude is null
                         || NearbyPlacesService.Km(a.Latitude.Value, a.Longitude!.Value, l.Latitude.Value, l.Longitude!.Value) <= a.RadiusKm))
                     .DistinctBy(a => a.UserId).Take(500))
            await notifier.SendAsync(a.UserId, l.SellerId, NotificationType.System,
                $"🔔 New in Marketplace: \"{l.Title}\" for {Money(l.Price, l.Currency)}{(a.Query is null ? "" : $" (matches \"{a.Query}\")")}.", $"/market/{l.Id}");
    }

    private record Extras(HashSet<Guid> Saved, Dictionary<Guid, int> Saves, Dictionary<Guid, SellerTrustDto> Trust);

    private static async Task<Extras> ExtrasAsync(AppDbContext db, Guid me, List<Listing> listings, bool withTrust = false)
    {
        var ids = listings.Select(l => l.Id).ToList();
        var saved = (await db.SavedListings.Where(x => x.UserId == me && ids.Contains(x.ListingId)).Select(x => x.ListingId).ToListAsync()).ToHashSet();
        var saves = await db.SavedListings.Where(x => ids.Contains(x.ListingId)).GroupBy(x => x.ListingId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        var trust = new Dictionary<Guid, SellerTrustDto>();
        if (withTrust)
            foreach (var seller in listings.Select(l => l.Seller).DistinctBy(s => s.Id))
            {
                var sold = await db.Listings.CountAsync(l => l.SellerId == seller.Id && l.Status == ListingStatus.Sold);
                var ratings = await db.SellerReviews.Where(r => r.SellerId == seller.Id).Select(r => r.Stars).ToListAsync();
                trust[seller.Id] = new SellerTrustDto(sold, ratings.Count == 0 ? null : Math.Round(ratings.Average(), 1), ratings.Count, seller.CreatedAt,
                    seller.IsVerified || seller.EmailConfirmed);
            }
        return new Extras(saved, saves, trust);
    }

    private static ListingDto ToDto(Listing l, Guid me, Extras x, double? km) => new(
        l.Id, l.Seller.ToSummary(), l.Title, l.Description, l.Price, l.Currency, l.Category, l.Condition, l.Location,
        JsonSerializer.Deserialize<List<string>>(l.Photos) ?? [], l.Status, l.CreatedAt, l.SellerId == me,
        x.Saved.Contains(l.Id), l.WasPrice, l.FairLow, l.FairHigh, km is double d ? Math.Round(d, 1) : null,
        l.Views, x.Saves.GetValueOrDefault(l.Id), x.Trust.GetValueOrDefault(l.SellerId));

    private static OfferDto ToDto(ListingOffer o, string currency) =>
        new(o.Id, o.ListingId, o.Buyer.ToSummary(), o.Amount, currency, o.Note, o.Status, o.CreatedAt);

    private static Dictionary<string, string[]> Validate(string? title, decimal price, string? category, List<string>? photos)
    {
        var errors = new Dictionary<string, string[]>();
        if ((title ?? "").Trim().Length is < 3 or > 100) errors["title"] = ["Give it a title (3 to 100 characters)."];
        if (price is < 0 or > 100_000_000) errors["price"] = ["Enter a price (0 for free)."];
        if (!Categories.Contains(category)) errors["category"] = ["Choose a category."];
        if ((photos?.Count ?? 0) == 0) errors["photos"] = ["Add at least one photo."];
        return errors;
    }

    private static async Task<IResult?> RefuseAsync(string text, AiService ai, CancellationToken ct)
    {
        if (NotAllowed.Match(text) is { Success: true } banned)
            return Results.Problem($"HalalBook Marketplace doesn't allow this kind of item (\"{banned.Value}\"). No alcohol, pork, weapons, tobacco, gambling or adult items.", statusCode: 422);
        var moderation = await ai.ModerateAsync(text, ct);
        return moderation.Allowed ? null : Results.Problem(string.Join(" ", moderation.Reasons), statusCode: 422, title: "Listing not allowed");
    }

    private static string NormalizeCurrency(string? c)
    {
        var cur = (c ?? "USD").Trim().ToUpperInvariant();
        return cur.Length == 3 ? cur : "USD";
    }

    private static string Clip(string? s, int max) => (s ?? "").Trim() is var t && t.Length > max ? t[..max] : (s ?? "").Trim();

    public static string Money(decimal amount, string currency) => amount == 0 ? "free" : $"{amount:N0} {currency}";

    /// <summary>Maps an uploaded file's public URL back to its file under wwwroot/uploads (nothing else).</summary>
    private static string? UploadedFilePath(IWebHostEnvironment env, string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        var rel = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        if (!rel.StartsWith("uploads/") || rel.Contains("..")) return null;
        var root = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
        var full = Path.GetFullPath(Path.Combine(root, rel));
        return full.StartsWith(Path.GetFullPath(Path.Combine(root, "uploads"))) ? full : null;
    }

    private static string MimeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png", ".gif" => "image/gif", ".webp" => "image/webp", _ => "image/jpeg",
    };
}

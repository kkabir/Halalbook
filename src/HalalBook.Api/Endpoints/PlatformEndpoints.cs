using System.Security.Claims;
using HalalBook.Api.Data;
using HalalBook.Api.Services;
using HalalBook.Shared;
using Microsoft.EntityFrameworkCore;

namespace HalalBook.Api.Endpoints;

/// <summary>Search, media upload, Islamic tools, AI and moderation.</summary>
public static class PlatformEndpoints
{
    private static readonly Dictionary<string, MediaType> AllowedMedia = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = MediaType.Image, [".jpeg"] = MediaType.Image, [".png"] = MediaType.Image, [".gif"] = MediaType.Image, [".webp"] = MediaType.Image,
        [".mp4"] = MediaType.Video, [".webm"] = MediaType.Video, [".mov"] = MediaType.Video,
    };

    /// <summary>Checks the file's leading bytes so renamed scripts or documents can't be uploaded as media.</summary>
    public static async Task<bool> HasValidSignatureAsync(IFormFile file, MediaType type)
    {
        var head = new byte[16];
        await using var s = file.OpenReadStream();
        int n = await s.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false);
        if (n < 12) return false;
        bool Starts(params byte[] sig) => head.AsSpan().StartsWith(sig);
        return type == MediaType.Image
            ? Starts(0xFF, 0xD8, 0xFF)                                              // JPEG
              || Starts(0x89, 0x50, 0x4E, 0x47)                                     // PNG
              || Starts((byte)'G', (byte)'I', (byte)'F', (byte)'8')                 // GIF
              || (Starts((byte)'R', (byte)'I', (byte)'F', (byte)'F') && head.AsSpan(8, 4).SequenceEqual("WEBP"u8))
            : head.AsSpan(4, 4).SequenceEqual("ftyp"u8)                              // MP4 / MOV / M4V
              || Starts(0x1A, 0x45, 0xDF, 0xA3);                                    // WebM / Matroska
    }

    private const int AutoHideReports = 3;
    private const int StrikesBeforeSuspension = 3;

    /// <summary>After 3 removed posts in 30 days an account is suspended (admins and moderators excepted).</summary>
    private static async Task ApplyStrikeAsync(AppDbContext db, Notifier notifier, Guid userId)
    {
        var since = DateTime.UtcNow.AddDays(-30);
        var strikes = await db.Posts.CountAsync(p => p.AuthorId == userId && p.Moderation == ModerationStatus.Removed && p.CreatedAt > since);
        if (strikes >= StrikesBeforeSuspension)
        {
            await db.Users.Where(u => u.Id == userId && u.Role == UserRole.User).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsSuspended, true));
            await notifier.SendAsync(userId, null, NotificationType.Moderation,
                "Your account has been suspended for repeatedly breaking the community guidelines. Contact support@halalbook.app to appeal.", null);
        }
        else
        {
            await notifier.SendAsync(userId, null, NotificationType.Moderation,
                $"Your content was removed for breaking the community guidelines (warning {strikes} of {StrikesBeforeSuspension}).", null);
        }
    }

    public static void MapPlatformEndpoints(this IEndpointRouteBuilder app)
    {
        // ---------- Search ----------
        app.MapGet("/api/search", async (string q, ClaimsPrincipal cp, AppDbContext db, PostService svc, PrivacyService privacy) =>
        {
            var me = cp.UserId();
            var term = (q ?? "").Trim();
            if (term.Length < 2) return Results.Ok(new SearchResultDto([], [], [], [], [], []));
            var blocked = await svc.BlockedIdsAsync(me);
            var tag = term.TrimStart('#').ToLowerInvariant();
            var like = $"%{term}%";

            var friendIds = await svc.FriendIdsAsync(me);
            var fof = (await privacy.FriendsOfFriendsAsync(me)).ToList();
            var people = await db.Users.Where(u => !blocked.Contains(u.Id) && !u.IsSuspended &&
                    (u.WhoCanFindMe == PrivacyAudience.Everyone || friendIds.Contains(u.Id)
                     || (u.WhoCanFindMe == PrivacyAudience.FriendsOfFriends && fof.Contains(u.Id))) &&
                    (EF.Functions.Like(u.DisplayName, like) || EF.Functions.Like(u.UserName, like)))
                .OrderBy(u => u.DisplayName).Take(10).Select(u => u.ToSummary()).ToListAsync();
            var visible = await svc.VisibleToAsync(me);
            var posts = (await svc.PageAsync(visible.Where(p => EF.Functions.Like(p.Content, like) || p.Hashtags.Any(h => h.Hashtag.Tag == tag)), me, 1, 10)).Items;
            var groups = await db.Groups.Where(g => EF.Functions.Like(g.Name, like) || (g.Description != null && EF.Functions.Like(g.Description, like))).Take(10).ToDtos(db, me).ToListAsync();
            var pages = await db.Pages.Where(p => EF.Functions.Like(p.Name, like) || EF.Functions.Like(p.Handle, like)).Take(10).ToDtos(db, me).ToListAsync();
            var events = await db.Events.Where(e => e.StartsAt > DateTime.UtcNow.AddDays(-1) && EF.Functions.Like(e.Title, like)).Take(10).ToDtos(db, me).ToListAsync();
            var tags = await db.Hashtags.Where(h => h.Tag.StartsWith(tag)).Take(10)
                .Select(h => new HashtagDto(h.Tag, db.PostHashtags.Count(ph => ph.HashtagId == h.Id))).ToListAsync();
            return Results.Ok(new SearchResultDto(people, posts, groups, pages, events, tags));
        }).WithTags("Search").RequireAuthorization();

        // ---------- Interface translations (works before sign-in too) ----------
        app.MapPost("/api/i18n/translate", async (TranslateUiRequest req, UiTranslator translator, CancellationToken ct) =>
            await translator.TranslateAsync(req.Language, req.Texts ?? [], ct))
            .WithTags("Languages").RequireRateLimiting("auth");
        app.MapGet("/api/i18n/languages", () => Languages.All).WithTags("Languages");

        // ---------- Media upload ----------
        // purpose: post (default), reel, story, comment, message, profile or cover. Each has its own size and length limits.
        app.MapPost("/api/media", async (IFormFile file, string? purpose, HttpRequest request, IWebHostEnvironment env, IConfiguration config, MediaPolicy policy, MediaScreening screening, CancellationToken ct) =>
        {
            var ext = Path.GetExtension(file.FileName);
            if (!AllowedMedia.TryGetValue(ext, out var type)) return Results.Problem("Only images (jpg, png, gif, webp) and videos (mp4, webm, mov) are supported.", statusCode: 415);
            var use = Enum.TryParse<UploadPurpose>(purpose, true, out var p) ? p : UploadPurpose.Post;
            if (await policy.CheckAsync(file, type, use) is { } problem) return Results.Problem(problem, statusCode: 413, title: "Upload too large");
            if (!await HasValidSignatureAsync(file, type)) return Results.Problem("That file doesn't look like a real image or video.", statusCode: 415);

            var folder = Path.Combine(DateTime.UtcNow.ToString("yyyy"), DateTime.UtcNow.ToString("MM"));
            var root = Path.Combine(env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"), "uploads", folder);
            Directory.CreateDirectory(root);
            var name = $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
            var path = Path.Combine(root, name);
            await using (var stream = File.Create(path))
                await file.CopyToAsync(stream, ct);

            // Screening: AI vision checks photos and frames from videos (sent by the browser as "frames").
            // Blocked files are deleted; files that need a person get a ".review" marker, so posts using them wait for a moderator.
            var frames = request.HasFormContentType ? request.Form.Files.GetFiles("frames") : [];
            var verdict = await screening.ScreenAsync(path, type, ext, use, frames, ct);
            if (!verdict.Allowed)
            {
                File.Delete(path);
                return Results.Problem($"This {(type == MediaType.Video ? "video" : "photo")} doesn't meet HalalBook's community guidelines (no nudity, sexual or adult content, gore or haram promotion)."
                    + (string.IsNullOrWhiteSpace(verdict.Reason) ? "" : " " + verdict.Reason), statusCode: 422, title: "Not allowed");
            }
            if (verdict.NeedsReview) await File.WriteAllTextAsync(path + ".review", verdict.Reason ?? "", ct);
            var alt = verdict.AltText;
            var needsReview = verdict.NeedsReview;

            var baseUrl = config["PublicBaseUrl"]?.TrimEnd('/') ?? $"{request.Scheme}://{request.Host}";
            return Results.Ok(new UploadResult($"{baseUrl}/uploads/{folder.Replace('\\', '/')}/{name}", type, alt, needsReview));
        }).WithTags("Media").RequireAuthorization().DisableAntiforgery().RequireRateLimiting("upload");

        app.MapGet("/api/media/limits", (MediaOptions o) => o).WithTags("Media").RequireAuthorization();

        // ---------- Islamic tools (public) ----------
        var islamic = app.MapGroup("/api/islamic").WithTags("Islamic");

        islamic.MapGet("/prayer-times", (double lat, double lng, DateOnly? date, int? offset, string? method, string? asr) =>
        {
            if (lat is < -90 or > 90 || lng is < -180 or > 180) return Results.BadRequest("Invalid coordinates.");
            var tz = offset ?? (int)Math.Round(lng / 15) * 60;
            var day = date ?? DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(tz));
            return Results.Ok(PrayerTimeCalculator.Calculate(day, lat, lng, tz, method, asr));
        });

        islamic.MapGet("/prayer-times/month", (double lat, double lng, int year, int month, int? offset, string? method, string? asr) =>
        {
            var tz = offset ?? (int)Math.Round(lng / 15) * 60;
            return Enumerable.Range(1, DateTime.DaysInMonth(year, month))
                .Select(d => PrayerTimeCalculator.Calculate(new DateOnly(year, month, d), lat, lng, tz, method, asr)).ToList();
        });

        islamic.MapGet("/methods", () => PrayerTimeCalculator.Methods.Select(m => new PrayerMethodDto(m.Key, m.Name)));
        islamic.MapGet("/qibla", (double lat, double lng) => QiblaCalculator.Calculate(lat, lng));
        islamic.MapGet("/hijri", (DateOnly? date, int? adjust) => HijriService.ToHijri(date ?? DateOnly.FromDateTime(DateTime.UtcNow), adjust ?? 0));
        islamic.MapGet("/calendar", (int year, int month) => HijriService.Month(year, month));
        islamic.MapGet("/occasions", () => HijriService.Upcoming(DateOnly.FromDateTime(DateTime.UtcNow)));

        // ---------- AI ----------
        var ai = app.MapGroup("/api/ai").WithTags("AI").RequireAuthorization().RequireRateLimiting("ai");
        ai.MapGet("/status", (AiService svc) => new { enabled = svc.IsEnabled });
        ai.MapPost("/compose", (AiComposeRequest req, AiService svc, CancellationToken ct) => svc.ComposeAsync(req, ct));
        ai.MapPost("/hashtags", async (AiComposeRequest req, AiService svc, CancellationToken ct) => await svc.SuggestHashtagsAsync(req.Text, ct));
        ai.MapPost("/moderate", (AiComposeRequest req, AiService svc, CancellationToken ct) => svc.ModerateAsync(req.Text, ct));
        ai.MapPost("/translate", (AiTranslateRequest req, AiService svc, CancellationToken ct) => svc.TranslateAsync(req, ct));
        ai.MapPost("/ask", (AiAskRequest req, AiService svc, CancellationToken ct) => svc.AskAsync(req, ct));
        ai.MapPost("/replies", (SmartRepliesRequest req, AiService svc, CancellationToken ct) => svc.SmartRepliesAsync(req, ct));
        ai.MapPost("/tone", (ToneRequest req, AiService svc, CancellationToken ct) => svc.ToneCheckAsync(req.Text, ct));

        // "Catch me up": the most relevant posts from the last two days, summarized.
        ai.MapGet("/digest", async (ClaimsPrincipal cp, AppDbContext db, PostService posts, AiService svc, CancellationToken ct) =>
        {
            var me = cp.UserId();
            var feed = await posts.FeedAsync(me, 1, 30, cp.IsModerator());
            var since = DateTime.UtcNow.AddDays(-2);
            var recent = feed.Items.Where(p => p.CreatedAt > since && p.Author.Id != me && !string.IsNullOrWhiteSpace(p.Content))
                .Select(p => (p.Group is { } g ? $"{p.Author.DisplayName} in {g.Name}" : p.Author.DisplayName, p.Content)).ToList();
            var user = await db.Users.FindAsync([me], ct);
            return await svc.DigestAsync(user?.DisplayName.Split(' ')[0] ?? "friend", recent, ct);
        });

        ai.MapGet("/check-sources/{postId:guid}", async (Guid postId, ClaimsPrincipal cp, PostService posts, AiService svc, CancellationToken ct) =>
        {
            var visible = await posts.VisibleToAsync(cp.UserId(), cp.IsModerator());
            var content = await visible.Where(p => p.Id == postId).Select(p => p.Content).FirstOrDefaultAsync(ct);
            return content is null ? Results.NotFound() : Results.Ok(await svc.CheckSourcesAsync(content, ct));
        });

        // ---------- Reports & moderation ----------
        app.MapPost("/api/reports", async (ReportRequest req, ClaimsPrincipal cp, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Reason)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["Tell us what's wrong."] });
            var me = cp.UserId();
            if (!await db.Reports.AnyAsync(r => r.ReporterId == me && r.TargetId == req.TargetId && r.Status == ReportStatus.Open))
            {
                db.Reports.Add(new Report { ReporterId = me, TargetType = req.TargetType, TargetId = req.TargetId, Reason = req.Reason.Trim() });
                await db.SaveChangesAsync();
            }
            // Community safety net: content reported by 3 different people is hidden until a moderator decides.
            var reporters = await db.Reports.Where(r => r.TargetId == req.TargetId && r.Status == ReportStatus.Open).Select(r => r.ReporterId).Distinct().CountAsync();
            if (reporters >= AutoHideReports)
            {
                if (req.TargetType == ReportTarget.Post)
                    await db.Posts.Where(p => p.Id == req.TargetId && p.Moderation == ModerationStatus.Approved)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.Moderation, ModerationStatus.PendingReview));
                else if (req.TargetType == ReportTarget.Comment)
                    await db.Comments.Where(c => c.Id == req.TargetId && c.Moderation == ModerationStatus.Approved)
                        .ExecuteUpdateAsync(s => s.SetProperty(c => c.Moderation, ModerationStatus.PendingReview));
                else if (req.TargetType == ReportTarget.Story)
                    await db.Stories.Where(s => s.Id == req.TargetId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTime.UtcNow));
            }
            return Results.Ok();
        }).WithTags("Moderation").RequireAuthorization();

        var mod = app.MapGroup("/api/moderation").WithTags("Moderation").RequireAuthorization("moderator");

        mod.MapGet("/queue", async (ClaimsPrincipal cp, AppDbContext db, PostService svc) =>
            await svc.BuildAsync(db.Posts.Where(p => p.Moderation == ModerationStatus.PendingReview && !p.IsDeleted).OrderBy(p => p.CreatedAt).Take(50), cp.UserId()));

        mod.MapPost("/posts/{id:guid}/{decision}", async (Guid id, string decision, AppDbContext db, Notifier notifier, ClaimsPrincipal cp) =>
        {
            var post = await db.Posts.FindAsync(id);
            if (post is null) return Results.NotFound();
            post.Moderation = decision == "approve" ? ModerationStatus.Approved : ModerationStatus.Removed;
            await db.SaveChangesAsync();
            if (decision != "approve") await ApplyStrikeAsync(db, notifier, post.AuthorId);
            await notifier.SendAsync(post.AuthorId, null, NotificationType.Moderation,
                decision == "approve" ? "Your post was reviewed and is now visible." : "Your post was removed for breaking the community guidelines.",
                $"/post/{id}");
            return Results.Ok();
        });

        mod.MapGet("/reports", async (AppDbContext db) =>
        {
            var reports = await db.Reports.Include(r => r.Reporter).Where(r => r.Status == ReportStatus.Open)
                .OrderBy(r => r.CreatedAt).Take(100).ToListAsync();
            var result = new List<ReportDto>();
            foreach (var r in reports)
            {
                string? preview = r.TargetType switch
                {
                    ReportTarget.Post => await db.Posts.Where(p => p.Id == r.TargetId).Select(p => p.Content).FirstOrDefaultAsync(),
                    ReportTarget.Comment => await db.Comments.Where(c => c.Id == r.TargetId).Select(c => c.Content).FirstOrDefaultAsync(),
                    ReportTarget.User => await db.Users.Where(u => u.Id == r.TargetId).Select(u => u.DisplayName + " (@" + u.UserName + ")").FirstOrDefaultAsync(),
                    ReportTarget.Story => await db.Stories.Where(s => s.Id == r.TargetId).Select(s => s.Text ?? s.MediaUrl).FirstOrDefaultAsync(),
                    _ => null,
                };
                result.Add(new ReportDto(r.Id, r.Reporter.ToSummary(), r.TargetType, r.TargetId, r.Reason, r.Status, r.CreatedAt, preview));
            }
            return result;
        });

        // decision: "remove" takes the reported content down; "dismiss" closes the report.
        mod.MapPost("/reports/{id:guid}/{decision}", async (Guid id, string decision, ClaimsPrincipal cp, AppDbContext db, Notifier notifier) =>
        {
            var r = await db.Reports.FindAsync(id);
            if (r is null) return Results.NotFound();
            if (decision == "remove")
            {
                switch (r.TargetType)
                {
                    case ReportTarget.Post:
                        await db.Posts.Where(p => p.Id == r.TargetId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Moderation, ModerationStatus.Removed));
                        if (await db.Posts.Where(p => p.Id == r.TargetId).Select(p => (Guid?)p.AuthorId).FirstOrDefaultAsync() is Guid author)
                            await ApplyStrikeAsync(db, notifier, author);
                        break;
                    case ReportTarget.Comment:
                        await db.Comments.Where(c => c.Id == r.TargetId).ExecuteUpdateAsync(s => s.SetProperty(c => c.Moderation, ModerationStatus.Removed)); break;
                    case ReportTarget.Story:
                        await db.Stories.Where(s => s.Id == r.TargetId).ExecuteDeleteAsync(); break;
                    case ReportTarget.User:
                        await db.Users.Where(u => u.Id == r.TargetId && u.Role == UserRole.User).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsSuspended, true)); break;
                }
                r.Status = ReportStatus.Resolved;
            }
            else r.Status = ReportStatus.Dismissed;
            r.ResolvedById = cp.UserId();
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        mod.MapPost("/users/{id:guid}/role/{role}", async (Guid id, UserRole role, ClaimsPrincipal cp, AppDbContext db) =>
        {
            if (!cp.IsInRole(nameof(UserRole.Admin))) return Results.Forbid();
            var n = await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, role));
            return n == 0 ? Results.NotFound() : Results.Ok();
        });
    }
}

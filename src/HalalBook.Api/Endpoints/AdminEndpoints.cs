using System.Security.Claims;
using HalalBook.Api.Data;
using HalalBook.Api.Services;
using HalalBook.Shared;
using Microsoft.EntityFrameworkCore;

namespace HalalBook.Api.Endpoints;

/// <summary>Help &amp; feedback from members, the error log, and the admin console that manages both.</summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // ---------- Feedback (anyone can write; signed-in members see their own requests) ----------
        app.MapPost("/api/feedback", async (CreateFeedbackRequest req, ClaimsPrincipal cp, AppDbContext db, Notifier notifier) =>
        {
            var subject = (req.Subject ?? "").Trim();
            var message = (req.Message ?? "").Trim();
            if (subject.Length is 0 or > 200 || message.Length is 0 or > 5000)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["message"] = ["Add a subject (up to 200 characters) and a message (up to 5000)."] });
            Guid? me = cp.Identity?.IsAuthenticated == true ? cp.UserId() : null;
            if (me is null && string.IsNullOrWhiteSpace(req.Email))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["Add your email so we can reply."] });
            var f = new Feedback
            {
                UserId = me, Name = req.Name?.Trim(), Email = req.Email?.Trim(), Category = req.Category, Subject = subject, Message = message,
                Page = req.Page, ScreenshotUrl = Uri.TryCreate(req.ScreenshotUrl, UriKind.Absolute, out var u) && u.AbsolutePath.StartsWith("/uploads/") ? req.ScreenshotUrl : null,
            };
            db.Feedback.Add(f);
            await db.SaveChangesAsync();
            // Let the admins know.
            foreach (var admin in await db.Users.Where(x => x.Role == UserRole.Admin).Select(x => x.Id).ToListAsync())
                await notifier.SendAsync(admin, me, NotificationType.System, $"New {f.Category}: {subject}", "/admin");
            return Results.Ok(new { f.Id });
        }).WithTags("Feedback").RequireRateLimiting("auth");

        app.MapGet("/api/feedback/mine", async (ClaimsPrincipal cp, AppDbContext db) =>
            await Query(db.Feedback.Where(f => f.UserId == cp.UserId()).OrderByDescending(f => f.CreatedAt).Take(50)).ToListAsync())
            .WithTags("Feedback").RequireAuthorization();

        // ---------- Errors from the web app and browsers ----------
        app.MapPost("/api/errors/client", (ClientErrorRequest req, HttpContext ctx, ClaimsPrincipal cp, ErrorSink sink) =>
        {
            var source = req.Source is "Web" or "Browser" ? req.Source : "Client";
            Guid? user = cp.Identity?.IsAuthenticated == true ? cp.UserId() : null;
            sink.Report(new ErrorEntry(source, "Error", ErrorSink.Truncate(req.Message, 2000), null, ErrorSink.Truncate(req.StackTrace, 8000),
                ErrorSink.Truncate(req.Path, 500), null, null, user, ctx.TraceIdentifier, req.UserAgent ?? ctx.Request.Headers.UserAgent.ToString()));
            return Results.Accepted();
        }).WithTags("Errors").RequireRateLimiting("auth");

        // ---------- Admin console ----------
        var admin = app.MapGroup("/api/admin").WithTags("Admin").RequireAuthorization("admin");

        admin.MapGet("/stats", async (AppDbContext db) =>
        {
            var day = DateTime.UtcNow.AddDays(-1);
            var week = DateTime.UtcNow.AddDays(-7);
            return new AdminStatsDto(
                await db.Feedback.CountAsync(f => f.Status == FeedbackStatus.New),
                await db.Feedback.CountAsync(f => f.Status == FeedbackStatus.New || f.Status == FeedbackStatus.InProgress),
                await db.ErrorLogs.CountAsync(e => e.Status == ErrorStatus.Open),
                await db.ErrorLogs.Where(e => e.LastSeen > day).SumAsync(e => (int?)e.Count) ?? 0,
                await db.Users.CountAsync(), await db.Users.CountAsync(u => u.CreatedAt > week),
                await db.Posts.CountAsync(p => !p.IsDeleted), await db.Posts.CountAsync(p => p.CreatedAt > day),
                await db.Posts.CountAsync(p => p.Moderation == ModerationStatus.PendingReview && !p.IsDeleted),
                await db.Reports.CountAsync(r => r.Status == ReportStatus.Open));
        });

        admin.MapGet("/feedback", async (FeedbackStatus? status, FeedbackCategory? category, string? q, AppDbContext db) =>
        {
            var query = db.Feedback.AsQueryable();
            if (status is { } s) query = query.Where(f => f.Status == s);
            if (category is { } c) query = query.Where(f => f.Category == c);
            if (!string.IsNullOrWhiteSpace(q)) query = query.Where(f => EF.Functions.Like(f.Subject, $"%{q}%") || EF.Functions.Like(f.Message, $"%{q}%"));
            return await Query(query.OrderBy(f => f.Status).ThenByDescending(f => f.CreatedAt).Take(200)).ToListAsync();
        });

        admin.MapPut("/feedback/{id:guid}", async (Guid id, UpdateFeedbackRequest req, ClaimsPrincipal cp, AppDbContext db, Notifier notifier, Mailer mailer) =>
        {
            var f = await db.Feedback.Include(x => x.User).FirstOrDefaultAsync(x => x.Id == id);
            if (f is null) return Results.NotFound();
            var newReply = !string.IsNullOrWhiteSpace(req.Reply) && req.Reply != f.Reply;
            f.Status = req.Status;
            f.Reply = string.IsNullOrWhiteSpace(req.Reply) ? f.Reply : req.Reply.Trim();
            f.AdminNote = req.AdminNote;
            f.HandledById = cp.UserId();
            f.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            if (newReply)
            {
                if (f.UserId is Guid uid)
                    await notifier.SendAsync(uid, cp.UserId(), NotificationType.System, $"HalalBook replied to \"{f.Subject}\"", "/feedback");
                var to = f.User?.Email ?? f.Email;
                if (to is not null)
                    await mailer.SendAsync(t => t.FeedbackReply(to, f.User?.DisplayName ?? f.Name ?? "friend", f.Subject, f.Reply!));
            }
            return Results.Ok((await Query(db.Feedback.Where(x => x.Id == id)).FirstAsync()));
        });

        admin.MapGet("/errors", async (ErrorStatus? status, string? source, string? q, AppDbContext db) =>
        {
            var query = db.ErrorLogs.AsQueryable();
            query = query.Where(e => e.Status == (status ?? ErrorStatus.Open));
            if (!string.IsNullOrWhiteSpace(source)) query = query.Where(e => e.Source == source);
            if (!string.IsNullOrWhiteSpace(q)) query = query.Where(e => EF.Functions.Like(e.Message, $"%{q}%") || EF.Functions.Like(e.Path!, $"%{q}%"));
            var list = await query.OrderByDescending(e => e.LastSeen).Take(200).ToListAsync();
            var userIds = list.Where(e => e.UserId != null).Select(e => e.UserId!.Value).Distinct().ToList();
            var users = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id);
            return list.Select(e => new ErrorLogDto(e.Id, e.Source, e.Level, e.Message, e.ExceptionType, e.StackTrace, e.Path, e.Method, e.StatusCode,
                e.Count, e.FirstSeen, e.LastSeen, e.Status, e.Note, e.UserId is Guid g && users.TryGetValue(g, out var u) ? u.ToSummary() : null,
                e.TraceId, e.UserAgent)).ToList();
        });

        admin.MapPut("/errors/{id:guid}", async (Guid id, UpdateErrorRequest req, ClaimsPrincipal cp, AppDbContext db) =>
        {
            var e = await db.ErrorLogs.FindAsync(id);
            if (e is null) return Results.NotFound();
            e.Status = req.Status;
            e.Note = req.Note;
            e.ResolvedById = req.Status == ErrorStatus.Open ? null : cp.UserId();
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        // Admin-only check that error logging works end to end.
        admin.MapPost("/errors/test", (ErrorSink sink, ClaimsPrincipal cp) =>
        {
            sink.Report(new ErrorEntry("Api", "Error", "Test error from the admin console", "HalalBook.TestException", null, "/api/admin/errors/test", "POST", 500, cp.UserId(), null, null));
            return Results.Accepted();
        });
    }

    private static IQueryable<FeedbackDto> Query(IQueryable<Feedback> q) => q.Select(f => new FeedbackDto(
        f.Id, f.Category, f.Status, f.Subject, f.Message, f.Page, f.ScreenshotUrl,
        f.User == null ? null : new UserSummaryDto(f.User.Id, f.User.UserName, f.User.DisplayName, f.User.AvatarUrl, f.User.IsVerified),
        f.Name, f.Email, f.Reply, f.AdminNote, f.CreatedAt, f.UpdatedAt,
        f.HandledBy == null ? null : new UserSummaryDto(f.HandledBy.Id, f.HandledBy.UserName, f.HandledBy.DisplayName, f.HandledBy.AvatarUrl, f.HandledBy.IsVerified)));
}

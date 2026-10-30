using System.Security.Claims;
using System.Text.RegularExpressions;
using HalalBook.Api.Data;
using HalalBook.Api.Services;
using HalalBook.Shared;
using Microsoft.EntityFrameworkCore;

namespace HalalBook.Api.Endpoints;

/// <summary>
/// Privacy-friendly visitor counts for the admin console: each browser gets a random ID (no cookies, no IP stored),
/// counted once per day, with page views and where people came from.
/// </summary>
public static partial class VisitorEndpoints
{
    public static void MapVisitorEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/stats/visit", async (VisitRequest req, HttpContext http, ClaimsPrincipal cp, AppDbContext db) =>
        {
            var ua = http.Request.Headers.UserAgent.ToString();
            if (string.IsNullOrWhiteSpace(req.VisitorId) || req.VisitorId.Length > 40 || BotRegex().IsMatch(ua)) return Results.NoContent();
            var path = NormalizePath(req.Path);
            var day = DateOnly.FromDateTime(DateTime.UtcNow);
            var now = DateTime.UtcNow;

            var visit = await db.SiteVisits.FirstOrDefaultAsync(v => v.Day == day && v.VisitorId == req.VisitorId);
            if (visit is null)
            {
                db.SiteVisits.Add(visit = new SiteVisit
                {
                    Day = day, VisitorId = req.VisitorId, FirstPath = path, Source = Source(req.Referrer, http.Request.Host.Host),
                    Device = ua.Contains("iPad") || ua.Contains("Tablet") ? "Tablet" : ua.Contains("Mobi") ? "Phone" : "Computer",
                    FirstSeen = now,
                });
            }
            visit.PageViews++;
            visit.LastSeen = now;
            if (cp.UserId() is var uid && uid != Guid.Empty) visit.UserId = uid;

            var page = await db.PageStats.FirstOrDefaultAsync(p => p.Day == day && p.Path == path);
            if (page is null) db.PageStats.Add(page = new PageStat { Day = day, Path = path });
            page.Views++;
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException) { } // two tabs opening at the same moment; one count is enough
            return Results.NoContent();
        }).WithTags("Stats");

        app.MapGet("/api/admin/visitors", async (AppDbContext db, PresenceTracker presence) =>
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var visits = db.SiteVisits;
            Task<int> Unique(DateOnly from) => visits.Where(v => v.Day >= from).Select(v => v.VisitorId).Distinct().CountAsync();
            var activeSince = DateTime.UtcNow.AddMinutes(-5);
            var week = today.AddDays(-6);

            var daily = await visits.Where(v => v.Day >= today.AddDays(-13)).GroupBy(v => v.Day)
                .Select(g => new { Day = g.Key, Visitors = g.Count(), Views = g.Sum(v => v.PageViews) }).ToListAsync();
            var pages = await db.PageStats.Where(p => p.Day >= week).GroupBy(p => p.Path)
                .Select(g => new { Path = g.Key, Views = g.Sum(p => p.Views) }).OrderByDescending(x => x.Views).Take(8).ToListAsync();
            var sources = await visits.Where(v => v.Day >= week).GroupBy(v => v.Source)
                .Select(g => new { Source = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Take(8).ToListAsync();
            var devices = await visits.Where(v => v.Day >= week).GroupBy(v => v.Device)
                .Select(g => new { Device = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).ToListAsync();

            return new VisitorStatsDto(
                await visits.Where(v => v.LastSeen > activeSince).Select(v => v.VisitorId).Distinct().CountAsync(),
                presence.OnlineUserIds.Count,
                await visits.CountAsync(v => v.Day == today),
                await visits.CountAsync(v => v.Day == today.AddDays(-1)),
                await Unique(week), await Unique(today.AddDays(-29)),
                await visits.Select(v => v.VisitorId).Distinct().CountAsync(),
                await visits.Where(v => v.Day == today).SumAsync(v => (int?)v.PageViews) ?? 0,
                await visits.Where(v => v.Day >= week).SumAsync(v => (int?)v.PageViews) ?? 0,
                Enumerable.Range(0, 14).Select(i => today.AddDays(i - 13))
                    .Select(d => daily.FirstOrDefault(x => x.Day == d) is { } x ? new DailyVisitsDto(d, x.Visitors, x.Views) : new DailyVisitsDto(d, 0, 0)).ToList(),
                pages.Select(p => new CountDto(p.Path, p.Views)).ToList(),
                sources.Select(s => new CountDto(s.Source, s.Count)).ToList(),
                devices.Select(d => new CountDto(d.Device, d.Count)).ToList());
        }).WithTags("Admin").RequireAuthorization("admin");
    }

    /// <summary>Groups pages like /u/aisha and /post/{id} so the list shows sections, not one row per person or post.</summary>
    public static string NormalizePath(string? path)
    {
        var p = (path ?? "/").Split('?', '#')[0].ToLowerInvariant().TrimEnd('/');
        if (p.Length == 0) return "/";
        p = GuidRegex().Replace(p, "{id}");
        foreach (var prefix in new[] { "/u/", "/join/", "/hashtag/", "/page/" })
            if (p.StartsWith(prefix)) return prefix + "…";
        return p.Length > 120 ? p[..120] : p;
    }

    /// <summary>"Google", "WhatsApp", "Facebook"... from the referring address; visits without one are "Direct".</summary>
    public static string Source(string? referrer, string ownHost)
    {
        if (!Uri.TryCreate(referrer, UriKind.Absolute, out var uri)) return "Direct";
        var host = uri.Host.ToLowerInvariant();
        if (host.EndsWith("halalbook.app") || host == ownHost || host == "localhost") return "Direct";
        string[][] known =
        [
            ["google.", "Google"], ["bing.", "Bing"], ["yandex.", "Yandex"], ["duckduckgo.", "DuckDuckGo"], ["yahoo.", "Yahoo"],
            ["whatsapp", "WhatsApp"], ["wa.me", "WhatsApp"], ["facebook.", "Facebook"], ["fb.", "Facebook"], ["messenger.", "Messenger"],
            ["instagram.", "Instagram"], ["t.co", "X (Twitter)"], ["twitter.", "X (Twitter)"], ["x.com", "X (Twitter)"],
            ["t.me", "Telegram"], ["telegram.", "Telegram"], ["youtube.", "YouTube"], ["linkedin.", "LinkedIn"], ["reddit.", "Reddit"],
            ["chatgpt.", "ChatGPT"], ["naver.", "Naver"], ["baidu.", "Baidu"],
        ];
        var name = known.FirstOrDefault(k => host.Contains(k[0]))?[1] ?? host.Replace("www.", "");
        return name.Length > 100 ? name[..100] : name;
    }

    [GeneratedRegex("bot|crawl|spider|slurp|facebookexternalhit|whatsapp|preview|headless|lighthouse", RegexOptions.IgnoreCase)]
    private static partial Regex BotRegex();

    [GeneratedRegex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")]
    private static partial Regex GuidRegex();
}

using System.Text.Json.Nodes;
using HalalBook.Api.Data;
using HalalBook.Shared;
using Microsoft.EntityFrameworkCore;

namespace HalalBook.Api.Services;

/// <summary>What Noor understood from a plain-words search, as filters on posts.</summary>
public record SearchPlan(List<string> Keywords, List<string> Hashtags, bool FriendsOnly, int? Days, string Media, string? Topic, string? Person)
{
    /// <summary>Without AI: the meaningful words of the question.</summary>
    public static SearchPlan FromWords(string q) => new(
        q.Split([' ', ',', '.', '?', '!'], StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 3 && !StopWords.Contains(w.ToLowerInvariant()))
            .Select(w => w.TrimStart('#')).Take(6).ToList(),
        q.Split(' ').Where(w => w.StartsWith('#')).Select(w => w.TrimStart('#').ToLowerInvariant()).ToList(),
        q.Contains("friend", StringComparison.OrdinalIgnoreCase), null, "any", null, null);

    private static readonly HashSet<string> StopWords = ["show", "find", "posts", "post", "about", "that", "with", "from", "what", "which", "where", "when", "this", "last", "week", "month", "please", "friends", "my"];
}

public partial class AiService
{
    public async Task<SearchPlan?> PlanSearchAsync(string question, CancellationToken ct)
    {
        if (!IsEnabled) return null;
        var reply = await AskOnce(
            "Turn a social network search request into filters. Reply with only JSON: {\"keywords\": [up to 6 words to look for in post text, " +
            "including useful synonyms and the same words in the language the request is written in], \"hashtags\": [without #], " +
            "\"friendsOnly\": bool, \"days\": number or null (how far back), \"media\": \"photo\"|\"video\"|\"any\", " +
            "\"topic\": one of " + string.Join(", ", Topics.All) + " or null, \"person\": a name mentioned as the author, or null}.",
            question, 300, ct);
        try
        {
            var j = JsonNode.Parse(reply![reply.IndexOf('{')..(reply.LastIndexOf('}') + 1)])!;
            List<string> List(string k) => j[k]?.AsArray().Select(x => x?.GetValue<string>() ?? "").Where(x => x.Length > 1).Take(8).ToList() ?? [];
            var topic = j["topic"]?.GetValue<string>();
            return new SearchPlan(List("keywords"), List("hashtags").Select(h => h.TrimStart('#').ToLowerInvariant()).ToList(),
                j["friendsOnly"]?.GetValue<bool>() ?? false, j["days"]?.GetValue<int?>(), j["media"]?.GetValue<string>() ?? "any",
                Topics.All.Contains(topic) ? topic : null, j["person"]?.GetValue<string>());
        }
        catch (Exception) { return null; }
    }

    /// <summary>One warm paragraph about someone's week, in their language.</summary>
    public async Task<string?> WeekSummaryAsync(string name, string languageName, string facts, CancellationToken ct) =>
        !IsEnabled ? null : await AskOnce(Persona + " Write 2 or 3 warm, encouraging sentences in " + languageName + " looking back on " + name +
            "'s week on HalalBook, based only on these facts. No lists, no made-up details.", facts, 250, ct);
}

public static class AskSearch
{
    public static async Task<List<PostDto>> RunAsync(SearchPlan plan, Guid me, AppDbContext db, PostService svc)
    {
        var q = await svc.VisibleToAsync(me);
        if (plan.FriendsOnly)
        {
            var friends = await svc.FriendIdsAsync(me);
            q = q.Where(p => friends.Contains(p.AuthorId));
        }
        if (plan.Days is int d and > 0) q = q.Where(p => p.CreatedAt > DateTime.UtcNow.AddDays(-Math.Min(d, 3650)));
        if (plan.Media == "photo") q = q.Where(p => p.Media.Any(m => m.Type == MediaType.Image));
        if (plan.Media == "video") q = q.Where(p => p.Media.Any(m => m.Type == MediaType.Video));
        if (plan.Person is { Length: > 1 } person) q = q.Where(p => p.Author.DisplayName.Contains(person) || p.Author.UserName.Contains(person));
        var words = plan.Keywords;
        var tags = plan.Hashtags;
        if (words.Count > 0 || tags.Count > 0 || plan.Topic is not null)
        {
            // Any keyword, hashtag or the topic can match; posts matching more come first.
            var candidates = await q.Select(p => new { p.Id, p.Content, p.Topic, p.CreatedAt, Tags = p.Hashtags.Select(h => h.Hashtag.Tag).ToList() })
                .OrderByDescending(p => p.CreatedAt).Take(800).ToListAsync();
            var ranked = candidates.Select(p => new
            {
                p.Id, p.CreatedAt,
                Score = words.Count(w => p.Content.Contains(w, StringComparison.OrdinalIgnoreCase)) + 2 * tags.Count(t => p.Tags.Contains(t))
                    + (plan.Topic is not null && p.Topic == plan.Topic ? 1 : 0),
            }).Where(x => x.Score > 0).OrderByDescending(x => x.Score).ThenByDescending(x => x.CreatedAt).Take(20).Select(x => x.Id).ToList();
            var posts = await svc.BuildAsync(db.Posts.Where(p => ranked.Contains(p.Id)), me);
            return ranked.Select(id => posts.First(p => p.Id == id)).ToList();
        }
        return (await svc.PageAsync(q, me, 1, 20)).Items;
    }

    public static string Describe(SearchPlan p)
    {
        var parts = new List<string>();
        if (p.Keywords.Count > 0) parts.Add($"posts about {string.Join(", ", p.Keywords.Take(4))}");
        if (p.Hashtags.Count > 0) parts.Add(string.Join(" ", p.Hashtags.Select(h => "#" + h)));
        if (p.Topic is not null) parts.Add($"topic: {p.Topic}");
        if (p.Person is not null) parts.Add($"by {p.Person}");
        if (p.FriendsOnly) parts.Add("from your friends");
        if (p.Days is int d) parts.Add(d <= 1 ? "today" : $"in the last {d} days");
        if (p.Media is "photo" or "video") parts.Add($"with a {p.Media}");
        return parts.Count == 0 ? "recent posts" : string.Join(" · ", parts);
    }
}

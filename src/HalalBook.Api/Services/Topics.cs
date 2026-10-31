using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using HalalBook.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace HalalBook.Api.Services;

/// <summary>
/// What a post is about, and whether it's heated (angry, mocking or arguing), so members can calm their feed:
/// see less of chosen topics, and less outrage overall. Facebook optimises for outrage; HalalBook lets you turn it down.
/// </summary>
public static partial class Topics
{
    public static readonly string[] All =
        ["Faith", "Family", "Food", "Travel", "Learning", "Business", "Sports", "Tech", "Health", "Humor", "Charity", "Politics", "News", "Other"];

    /// <summary>A quick guess without AI, from common words.</summary>
    public static (string Topic, bool Heated) Guess(string text)
    {
        var t = text.ToLowerInvariant();
        string topic =
            PoliticsWords().IsMatch(t) ? "Politics" :
            NewsWords().IsMatch(t) ? "News" :
            FaithWords().IsMatch(t) ? "Faith" :
            FoodWords().IsMatch(t) ? "Food" :
            SportsWords().IsMatch(t) ? "Sports" :
            FamilyWords().IsMatch(t) ? "Family" : "Other";
        var heated = HeatedWords().IsMatch(t) || text.Count(c => c == '!') >= 4 || (text.Length > 20 && text.Count(char.IsUpper) > text.Length * 0.6);
        return (topic, heated);
    }

    [GeneratedRegex(@"\b(election|vote|government|minister|president|parliament|party|politic\w*|protest|sanction|war)\b")] private static partial Regex PoliticsWords();
    [GeneratedRegex(@"\b(breaking|news|reported|headline|announced)\b")] private static partial Regex NewsWords();
    [GeneratedRegex(@"\b(allah|quran|salah|dua|masjid|mosque|ramadan|jummah|hadith|prayer|alhamdulillah|inshaallah)\b")] private static partial Regex FaithWords();
    [GeneratedRegex(@"\b(recipe|cook\w*|biryani|iftar|dinner|lunch|breakfast|food|cake|dessert)\b")] private static partial Regex FoodWords();
    [GeneratedRegex(@"\b(football|cricket|match|goal|team|league|sports?)\b")] private static partial Regex SportsWords();
    [GeneratedRegex(@"\b(family|kids|children|mum|mom|dad|baby|wedding|nikah|wife|husband)\b")] private static partial Regex FamilyWords();
    [GeneratedRegex(@"\b(idiot|stupid|shame on|disgusting|hate|liars?|traitors?|pathetic|fools?)\b")] private static partial Regex HeatedWords();
}

public class TopicQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();
    public void Enqueue(Guid postId) => _channel.Writer.TryWrite(postId);
    public ChannelReader<Guid> Reader => _channel.Reader;
}

/// <summary>Labels new posts in the background (AI when available, otherwise the word-based guess).</summary>
public class TopicWorker(TopicQueue queue, IServiceScopeFactory scopes, ILogger<TopicWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var id in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var ai = scope.ServiceProvider.GetRequiredService<AiService>();
                var post = await db.Posts.FindAsync([id], stoppingToken);
                if (post is null || string.IsNullOrWhiteSpace(post.Content)) continue;
                var (topic, heated) = await ai.ClassifyTopicAsync(post.Content, stoppingToken) ?? Topics.Guess(post.Content);
                (post.Topic, post.Heated) = (topic, heated);
                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Could not label post {PostId}", id); }
        }
    }
}

public partial class AiService
{
    /// <summary>One of <see cref="Topics.All"/>, and whether the tone is heated. Null without AI.</summary>
    public async Task<(string Topic, bool Heated)?> ClassifyTopicAsync(string text, CancellationToken ct)
    {
        if (!IsEnabled) return null;
        var reply = await AskOnce("Label a social media post. Reply with only JSON: {\"topic\": one of " + string.Join(", ", Topics.All) +
            ", \"heated\": true if it is angry, mocking, insulting or picking a fight, otherwise false}.",
            text.Length > 1500 ? text[..1500] : text, 60, ct);
        try
        {
            var json = JsonNode.Parse(reply![reply.IndexOf('{')..(reply.LastIndexOf('}') + 1)])!;
            var topic = json["topic"]?.GetValue<string>();
            return (Topics.All.Contains(topic) ? topic! : "Other", json["heated"]?.GetValue<bool>() ?? false);
        }
        catch (Exception) { return null; }
    }
}

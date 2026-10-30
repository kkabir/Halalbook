using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HalalBook.Shared;

namespace HalalBook.Api.Services;

public class AiOptions
{
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "claude-sonnet-5-5";
    public string Endpoint { get; set; } = "https://api.anthropic.com/v1/messages";
    public bool ModerateWithAi { get; set; } = true;
}

/// <summary>
/// AI features backed by the Claude Messages API. Every feature has a local fallback so the
/// app keeps working when no API key is configured or the call fails.
/// </summary>
public partial class AiService(HttpClient http, AiOptions options, ContentModerator moderator, ILogger<AiService> log)
{
    public bool IsEnabled => !string.IsNullOrWhiteSpace(options.ApiKey);

    private const string Persona =
        "You are Noor, the assistant inside HalalBook, a social network for Muslims. " +
        "You are warm, respectful and concise, and you keep everything halal and family friendly.";

    public Task<string?> CompleteAsync(string system, IEnumerable<AiChatTurn> turns, int maxTokens = 800, CancellationToken ct = default) =>
        CompleteRawAsync(system, new JsonArray(turns.Select(t => (JsonNode)new JsonObject
        {
            ["role"] = t.Role == "assistant" ? "assistant" : "user",
            ["content"] = t.Content,
        }).ToArray()), maxTokens, ct);

    /// <summary>Sends a single user turn with an image (Claude vision) and a text prompt.</summary>
    public Task<string?> CompleteWithImageAsync(string system, byte[] image, string mediaType, string prompt, int maxTokens = 400, CancellationToken ct = default) =>
        CompleteRawAsync(system, new JsonArray(new JsonObject
        {
            ["role"] = "user",
            ["content"] = new JsonArray(
                new JsonObject
                {
                    ["type"] = "image",
                    ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = mediaType, ["data"] = Convert.ToBase64String(image) },
                },
                new JsonObject { ["type"] = "text", ["text"] = prompt }),
        }), maxTokens, ct);

    private async Task<string?> CompleteRawAsync(string system, JsonArray messages, int maxTokens, CancellationToken ct)
    {
        if (!IsEnabled) return null;
        try
        {
            var body = new JsonObject
            {
                ["model"] = options.Model,
                ["max_tokens"] = maxTokens,
                ["system"] = system,
                ["messages"] = messages,
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, options.Endpoint) { Content = JsonContent.Create(body) };
            req.Headers.Add("x-api-key", options.ApiKey);
            req.Headers.Add("anthropic-version", "2023-06-01");
            using var res = await http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
            {
                log.LogWarning("Claude API returned {Status}: {Body}", res.StatusCode, await res.Content.ReadAsStringAsync(ct));
                return null;
            }
            var json = await res.Content.ReadFromJsonAsync<JsonObject>(ct);
            var text = string.Concat(json?["content"]?.AsArray()
                .Where(c => c?["type"]?.GetValue<string>() == "text")
                .Select(c => c!["text"]!.GetValue<string>()) ?? []);
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Claude API call failed");
            return null;
        }
    }

    private Task<string?> AskOnce(string system, string prompt, int maxTokens = 800, CancellationToken ct = default) =>
        CompleteAsync(system, [new AiChatTurn("user", prompt)], maxTokens, ct);

    public async Task<AiComposeResponse> ComposeAsync(AiComposeRequest req, CancellationToken ct)
    {
        string instruction = req.Mode switch
        {
            "shorter" => "Make this post shorter and punchier while keeping its meaning.",
            "warmer" => "Rewrite this post to sound warmer and more heartfelt.",
            "arabic-greeting" => "Rewrite this post so it opens with a fitting Islamic greeting or dua, keeping the rest natural.",
            "caption" => "Write an engaging one or two sentence caption based on this idea.",
            _ => "Polish this post: fix grammar and spelling, improve flow, keep the author's voice and language.",
        };
        var ai = await AskOnce(Persona + " You help people write social posts. Reply with only the rewritten post text, no preamble.",
            $"{instruction}\n\nPost:\n{req.Text}", 600, ct);
        var text = ai ?? req.Text.Trim();
        var tags = await SuggestHashtagsAsync(text, ct);
        var moderation = await ModerateAsync(text, ct);
        return new AiComposeResponse(text, tags, moderation, ai is not null);
    }

    public async Task<List<string>> SuggestHashtagsAsync(string text, CancellationToken ct)
    {
        var ai = await AskOnce(Persona,
            "Suggest up to 5 relevant hashtags for this post. Reply with only the hashtags separated by spaces, each starting with #.\n\n" + text, 100, ct);
        var source = ai ?? string.Join(' ', KeywordFallback(text).Select(k => "#" + k));
        return HashtagRegex().Matches(source).Select(m => m.Groups[1].Value.ToLowerInvariant()).Distinct().Take(5).ToList();
    }

    public async Task<ModerationResult> ModerateAsync(string text, CancellationToken ct)
    {
        var local = moderator.Check(text);
        if (!local.Allowed || !IsEnabled || !options.ModerateWithAi || string.IsNullOrWhiteSpace(text)) return local;

        var ai = await AskOnce(
            "You are a content moderator for HalalBook, a family-friendly social network for Muslims. " +
            "Block content that is sexually explicit, hateful, harassing, violent extremism, or promotes haram activities such as gambling, alcohol or drugs. " +
            "Mark as needs_review content that is borderline. Respectful religious discussion and disagreement is allowed. " +
            "Reply with only JSON: {\"allowed\": bool, \"needs_review\": bool, \"reasons\": [string]}",
            text, 200, ct);
        if (ai is null) return local;
        try
        {
            var json = JsonNode.Parse(ai[ai.IndexOf('{')..(ai.LastIndexOf('}') + 1)])!;
            var reasons = json["reasons"]?.AsArray().Select(r => r!.GetValue<string>()).ToList() ?? [];
            return new ModerationResult(
                json["allowed"]?.GetValue<bool>() ?? true,
                local.NeedsReview || (json["needs_review"]?.GetValue<bool>() ?? false),
                [.. local.Reasons, .. reasons]);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            return local;
        }
    }

    public async Task<AiTextResponse> TranslateAsync(AiTranslateRequest req, CancellationToken ct)
    {
        var ai = await AskOnce(Persona + " Reply with only the translation.",
            $"Translate into {req.TargetLanguage}. Keep Islamic terms such as Allah, InshaAllah and Alhamdulillah as they are.\n\n{req.Text}", 800, ct);
        return new AiTextResponse(ai ?? req.Text, ai is not null);
    }

    public async Task<AiTextResponse> SummarizeAsync(string title, IEnumerable<string> lines, CancellationToken ct)
    {
        var all = lines.ToList();
        if (all.Count == 0) return new AiTextResponse("There is nothing to summarize yet.", false);
        var ai = await AskOnce(Persona,
            $"Summarize the main points of this discussion about \"{title}\" in 2 to 4 short bullet points.\n\n" + string.Join("\n", all.Take(200).Select(l => "- " + l)), 400, ct);
        return new AiTextResponse(ai ?? $"{all.Count} comments. Most recent: \"{Truncate(all[^1], 140)}\"", ai is not null);
    }

    public async Task<AiTextResponse> AskAsync(AiAskRequest req, CancellationToken ct)
    {
        var system = Persona +
            " You answer questions about Islam, Muslim life and using HalalBook. When you cite the Quran or hadith, give the reference " +
            "(surah:ayah or collection and number) and only cite what you are confident about. Where scholars differ, say so briefly and fairly. " +
            "You are not a mufti: for personal rulings, encourage the person to consult a qualified local scholar.";
        var turns = (req.History ?? []).TakeLast(10).Append(new AiChatTurn("user", req.Question));
        var ai = await CompleteAsync(system, turns, 1000, ct);
        return new AiTextResponse(ai ??
            "Assalamu alaikum! The AI assistant isn't configured on this server yet. Ask an admin to add a Claude API key under Ai:ApiKey.", ai is not null);
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the","a","an","and","or","but","to","of","in","on","for","with","is","are","was","were","be","this","that","it","my","our",
        "your","we","i","you","they","he","she","at","by","from","as","so","have","has","had","just","all","about","today","will","can"
    };

    private static IEnumerable<string> KeywordFallback(string text) =>
        WordRegex().Matches(text).Select(m => m.Value.ToLowerInvariant())
            .Where(w => w.Length > 3 && !StopWords.Contains(w))
            .GroupBy(w => w).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key.Length)
            .Select(g => g.Key).Take(4);

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    [GeneratedRegex(@"#(\w{2,50})")] private static partial Regex HashtagRegex();
    [GeneratedRegex(@"\p{L}+")] private static partial Regex WordRegex();
}

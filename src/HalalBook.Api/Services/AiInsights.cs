using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HalalBook.Shared;

namespace HalalBook.Api.Services;

public record ImageInsight(string? AltText, bool Allowed, bool NeedsReview, List<string> Reasons);

/// <summary>AI features that go beyond a typical social network: vision, smart replies, adab nudges, digests, source checks.</summary>
public partial class AiService
{
    /// <summary>Describes a photo for screen readers and checks it against HalalBook's modesty guidelines.</summary>
    public async Task<ImageInsight> AnalyzeImageAsync(byte[] image, string mediaType, CancellationToken ct)
    {
        // Claude accepts images up to 5 MB; larger ones are only size-checked.
        if (!IsEnabled || image.Length > 5_000_000) return new ImageInsight(null, true, false, []);
        var ai = await CompleteWithImageAsync(
            "You review photos for HalalBook, a family-friendly social network for Muslims. " +
            "Block nudity, sexual content, graphic violence or gore, and images promoting alcohol, drugs or gambling. " +
            "Mark as needs_review photos that are borderline (for example revealing clothing). Ordinary people, food, nature, " +
            "masjids, events and family photos are fine. Also write a short, factual alt text (max 20 words) for blind users. " +
            "Reply with only JSON: {\"alt\": string, \"allowed\": bool, \"needs_review\": bool, \"reasons\": [string]}",
            image, mediaType, "Review this photo.", 300, ct);
        if (ai is null) return new ImageInsight(null, true, false, []);
        try
        {
            var json = JsonNode.Parse(ai[ai.IndexOf('{')..(ai.LastIndexOf('}') + 1)])!;
            return new ImageInsight(
                json["alt"]?.GetValue<string>(),
                json["allowed"]?.GetValue<bool>() ?? true,
                json["needs_review"]?.GetValue<bool>() ?? false,
                json["reasons"]?.AsArray().Select(r => r!.GetValue<string>()).ToList() ?? []);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            return new ImageInsight(null, true, false, []);
        }
    }

    private static readonly string[] DefaultReplies = ["MashaAllah! 🌙", "JazakAllahu khayran", "Ameen 🤲", "Love this ❤️"];

    /// <summary>Three short, kind replies that fit the conversation.</summary>
    public async Task<SmartRepliesResponse> SmartRepliesAsync(SmartRepliesRequest req, CancellationToken ct)
    {
        var context = (req.Context ?? "").Trim();
        var ai = await AskOnce(Persona +
            " Suggest exactly 3 short replies (2 to 8 words each) the reader could send. Match the language and tone of the message, " +
            "use Islamic expressions only where natural, and keep them kind. Reply with one suggestion per line and nothing else.",
            $"{(req.Kind == "message" ? "Chat message" : "Post or comment")}:\n{context}", 120, ct);
        if (ai is not null)
        {
            var lines = ai.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(l => l.TrimStart('-', '*', '•', '1', '2', '3', '.', ')', ' ').Trim('"'))
                .Where(l => l.Length is > 0 and <= 80).Take(3).ToList();
            if (lines.Count > 0) return new SmartRepliesResponse(lines, true);
        }
        return new SmartRepliesResponse(FallbackReplies(context), false);
    }

    private static List<string> FallbackReplies(string text)
    {
        var t = text.ToLowerInvariant();
        if (Regex.IsMatch(t, @"\b(passed away|inna lillahi|died|death|funeral|janazah)\b"))
            return ["Inna lillahi wa inna ilayhi raji'un", "May Allah grant them Jannah 🤲", "Sending duas to the family"];
        if (Regex.IsMatch(t, @"\b(sick|ill|hospital|surgery|pray for)\b"))
            return ["May Allah grant shifa 🤲", "Making dua for you", "Get well soon, inshaAllah"];
        if (Regex.IsMatch(t, @"\b(married|nikah|baby|born|graduat|congrat|new job)\b"))
            return ["Mabrook! 🎉", "Barakallahu feek!", "MashaAllah, so happy for you"];
        if (t.Contains('?')) return ["Good question!", "InshaAllah, let me check", "I'd love to know too"];
        return [.. DefaultReplies.Take(3)];
    }

    private static readonly Regex HarshWords = new(
        @"\b(stupid|idiot|dumb|shut up|hate you|loser|ugly|liar|kafir|munafiq|go to hell|disgusting|pathetic|moron|fool)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The adab assistant: notices harsh or insulting comments and suggests a kinder way to say the same thing.</summary>
    public async Task<ToneResponse> ToneCheckAsync(string text, CancellationToken ct)
    {
        text = (text ?? "").Trim();
        if (text.Length < 4) return new ToneResponse(false, null, null, false);
        var obviouslyHarsh = HarshWords.IsMatch(text) || (text.Length > 12 && text.Count(char.IsUpper) > text.Count(char.IsLetter) * 0.7);
        if (!IsEnabled)
            return obviouslyHarsh
                ? new ToneResponse(true, null, "This might hurt someone. The Prophet ﷺ said: \"Whoever believes in Allah and the Last Day, let him speak good or remain silent.\"", false)
                : new ToneResponse(false, null, null, false);

        var ai = await AskOnce(
            "You are the adab (good manners) assistant of HalalBook. Decide if this comment is harsh, insulting, mocking, " +
            "takfiri, or likely to hurt someone. Disagreement stated respectfully is NOT harsh. If harsh, rewrite it to keep the " +
            "person's point but say it with kindness and adab, in the same language. " +
            "Reply with only JSON: {\"harsh\": bool, \"suggestion\": string or null, \"note\": short reason or null}",
            text, 300, ct);
        if (ai is null) return new ToneResponse(obviouslyHarsh, null, obviouslyHarsh ? "This might come across as hurtful." : null, false);
        try
        {
            var json = JsonNode.Parse(ai[ai.IndexOf('{')..(ai.LastIndexOf('}') + 1)])!;
            var harsh = json["harsh"]?.GetValue<bool>() ?? false;
            return new ToneResponse(harsh, harsh ? json["suggestion"]?.GetValue<string>() : null, harsh ? json["note"]?.GetValue<string>() : null, true);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            return new ToneResponse(obviouslyHarsh, null, null, false);
        }
    }

    /// <summary>"Catch me up": a short digest of recent posts from the people and groups you follow.</summary>
    public async Task<AiTextResponse> DigestAsync(string name, IReadOnlyList<(string Author, string Text)> posts, CancellationToken ct)
    {
        if (posts.Count == 0) return new AiTextResponse("You're all caught up. Nothing new from your friends yet.", false);
        var ai = await AskOnce(Persona +
            $" Write a warm, short catch-up for {name} (3 to 6 bullet points) about what their friends and groups shared. " +
            "Mention people by first name, group similar news, highlight anything needing duas or congratulations, and skip trivia.",
            string.Join("\n", posts.Take(40).Select(p => $"- {p.Author}: {Truncate(p.Text, 300)}")), 500, ct);
        return new AiTextResponse(ai ?? string.Join("\n", posts.Take(5).Select(p => $"• {p.Author}: {Truncate(p.Text, 90)}")), ai is not null);
    }

    /// <summary>Checks Quran and hadith claims in a post, citing references and flagging weak or fabricated attributions.</summary>
    public async Task<AiTextResponse> CheckSourcesAsync(string text, CancellationToken ct)
    {
        var ai = await AskOnce(Persona +
            " Check any Quran verses, hadith or Islamic rulings quoted or claimed in this post. For each: say whether it is accurate, " +
            "give the reference (surah:ayah, or hadith collection and number with its grading where known), and flag anything weak, " +
            "fabricated or misattributed. If you are not sure, say so plainly. If the post makes no such claims, say that briefly. " +
            "Be concise and respectful, and end with: \"For personal rulings, please ask a qualified scholar.\"",
            text, 700, ct);
        return new AiTextResponse(ai ?? "Source checking needs the AI assistant, which isn't configured on this server yet.", ai is not null);
    }
}

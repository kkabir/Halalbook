using System.Text.RegularExpressions;
using HalalBook.Shared;

namespace HalalBook.Api.Services;

public class ModerationOptions
{
    /// <summary>Terms that cause content to be rejected outright.</summary>
    public List<string> BlockedTerms { get; set; } = [];
    /// <summary>Terms that let content through but send it to the moderator queue.</summary>
    public List<string> ReviewTerms { get; set; } = [];
    /// <summary>When no AI key is set, photo and video posts wait for a moderator instead of going live unchecked.</summary>
    public bool ReviewMediaWithoutAi { get; set; } = true;
    /// <summary>Unambiguous adult words and site names, blocked even when hidden inside other text or links.</summary>
    public List<string> HiddenTerms { get; set; } = [];
}

/// <summary>Fast, local word-list moderation. The AI moderator runs on top of this when configured.</summary>
public class ContentModerator
{
    private readonly Regex? _blocked;
    private readonly Regex? _review;
    private readonly List<string> _hidden;

    public ContentModerator(ModerationOptions options)
    {
        _blocked = Build(options.BlockedTerms);
        _review = Build(options.ReviewTerms);
        _hidden = options.HiddenTerms.Select(t => t.ToLowerInvariant()).ToList();
    }

    private static Regex? Build(List<string> terms) => terms.Count == 0 ? null :
        new Regex(@"\b(" + string.Join("|", terms.Select(Regex.Escape)) + @")\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Dictionary<char, char> Leet = new()
    {
        ['0'] = 'o', ['1'] = 'i', ['3'] = 'e', ['4'] = 'a', ['5'] = 's', ['7'] = 't', ['@'] = 'a', ['$'] = 's', ['!'] = 'i',
    };

    /// <summary>"P.0.r-N" → "porn": undoes common tricks used to slip words past filters.</summary>
    public static string Normalize(string text)
    {
        var chars = text.ToLowerInvariant().Select(c => Leet.TryGetValue(c, out var r) ? r : c);
        // Join letters separated by dots, dashes, underscores, stars or single spaces ("s e x", "n.u.d.e").
        var joined = Regex.Replace(new string(chars.ToArray()), @"(?<=\b\p{L})[\s\.\-_\*]+(?=\p{L}\b)", "");
        return joined;
    }

    public ModerationResult Check(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new ModerationResult(true, false, []);
        var normalized = Normalize(text);
        var blocked = (_blocked?.Matches(text).Concat(_blocked.Matches(normalized)) ?? [])
            .Select(m => m.Value.ToLowerInvariant()).ToList();
        // Unambiguous adult words and sites, found even inside links or run-together text ("freepornhere", "xnxx.com").
        var squashed = Regex.Replace(normalized, @"[^\p{L}]", "");
        blocked.AddRange(_hidden.Where(squashed.Contains));
        blocked = blocked.Distinct().ToList();
        if (blocked.Count > 0)
            return new ModerationResult(false, false, [$"Contains content that isn't allowed on HalalBook ({string.Join(", ", blocked)})."]);
        var review = _review?.Matches(text).Select(m => m.Value.ToLowerInvariant()).Distinct().ToList() ?? [];
        return review.Count > 0
            ? new ModerationResult(true, true, [$"Sent for review: mentions {string.Join(", ", review)}."])
            : new ModerationResult(true, false, []);
    }
}

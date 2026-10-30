using System.Text.RegularExpressions;
using HalalBook.Shared;

namespace HalalBook.Api.Services;

public class ModerationOptions
{
    /// <summary>Terms that cause content to be rejected outright.</summary>
    public List<string> BlockedTerms { get; set; } = [];
    /// <summary>Terms that let content through but send it to the moderator queue.</summary>
    public List<string> ReviewTerms { get; set; } = [];
}

/// <summary>Fast, local word-list moderation. The AI moderator runs on top of this when configured.</summary>
public class ContentModerator
{
    private readonly Regex? _blocked;
    private readonly Regex? _review;

    public ContentModerator(ModerationOptions options)
    {
        _blocked = Build(options.BlockedTerms);
        _review = Build(options.ReviewTerms);
    }

    private static Regex? Build(List<string> terms) => terms.Count == 0 ? null :
        new Regex(@"\b(" + string.Join("|", terms.Select(Regex.Escape)) + @")\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public ModerationResult Check(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new ModerationResult(true, false, []);
        var blocked = _blocked?.Matches(text).Select(m => m.Value.ToLowerInvariant()).Distinct().ToList() ?? [];
        if (blocked.Count > 0)
            return new ModerationResult(false, false, [$"Contains content that isn't allowed on HalalBook ({string.Join(", ", blocked)})."]);
        var review = _review?.Matches(text).Select(m => m.Value.ToLowerInvariant()).Distinct().ToList() ?? [];
        return review.Count > 0
            ? new ModerationResult(true, true, [$"Sent for review: mentions {string.Join(", ", review)}."])
            : new ModerationResult(true, false, []);
    }
}

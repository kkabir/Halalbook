namespace HalalBook.Api.Services;

public record ScreeningResult(bool Allowed, bool NeedsReview, string? AltText, string? Reason);

/// <summary>
/// Decides whether an uploaded photo or video may be shown: AI vision checks photos and frames taken from videos,
/// and anything that can't be checked waits for a moderator instead of going live.
/// </summary>
public class MediaScreening(AiService ai, ModerationOptions moderation, ILogger<MediaScreening> log)
{
    public const int MaxFrames = 6;
    private const long MaxFrameBytes = 1_500_000;

    /// <summary>Places where a flagged file is refused outright, because there is no review queue for them.</summary>
    private static bool IsStrict(UploadPurpose p) => p is UploadPurpose.Profile or UploadPurpose.Cover or UploadPurpose.Story or UploadPurpose.Message;

    public async Task<ScreeningResult> ScreenAsync(string path, HalalBook.Shared.MediaType type, string ext, UploadPurpose purpose,
        IReadOnlyList<IFormFile> frames, CancellationToken ct)
    {
        ScreeningResult result;
        // Voice notes have no picture to check; what people say in chats is covered by reports and blocking.
        if (purpose == UploadPurpose.Voice) return new ScreeningResult(true, false, null, null);
        if (!ai.IsEnabled)
        {
            // No AI: posts, reels and comment photos wait for a moderator (when configured); other places rely on reports.
            var review = moderation.ReviewMediaWithoutAi && purpose is UploadPurpose.Post or UploadPurpose.Reel or UploadPurpose.Comment;
            return new ScreeningResult(true, review, null, review ? "Waiting for a moderator to check this media." : null);
        }

        if (type == HalalBook.Shared.MediaType.Image)
        {
            var insight = await ai.AnalyzeImageAsync(await File.ReadAllBytesAsync(path, ct), MimeFor(ext), ct);
            result = new ScreeningResult(insight.Allowed, insight.NeedsReview, insight.AltText, string.Join(" ", insight.Reasons));
        }
        else
        {
            // Videos: the browser sends still frames from across the video; every one must pass.
            var usable = frames.Where(f => f.Length is > 0 and <= MaxFrameBytes).Take(MaxFrames).ToList();
            if (usable.Count < 3)
                return Strict(purpose, new ScreeningResult(true, true, null, "This video couldn't be checked automatically, so a moderator will review it."));
            string? alt = null;
            var review = false;
            var reasons = new List<string>();
            foreach (var frame in usable)
            {
                await using var s = frame.OpenReadStream();
                using var ms = new MemoryStream();
                await s.CopyToAsync(ms, ct);
                var bytes = ms.ToArray();
                if (bytes.Length < 3 || bytes[0] != 0xFF || bytes[1] != 0xD8) { review = true; continue; } // frames must be JPEG
                var insight = await ai.AnalyzeImageAsync(bytes, "image/jpeg", ct);
                alt ??= insight.AltText is { } a ? "Video: " + a : null;
                reasons.AddRange(insight.Reasons);
                if (!insight.Allowed) return new ScreeningResult(false, false, null, string.Join(" ", insight.Reasons.Distinct()));
                review |= insight.NeedsReview;
            }
            result = new ScreeningResult(true, review, alt, string.Join(" ", reasons.Distinct()));
        }
        if (!result.Allowed) log.LogInformation("Blocked {Type} upload for {Purpose}: {Reason}", type, purpose, result.Reason);
        return Strict(purpose, result);
    }

    /// <summary>Where there's no review queue, "needs review" means "not allowed".</summary>
    private static ScreeningResult Strict(UploadPurpose purpose, ScreeningResult r) =>
        r.NeedsReview && IsStrict(purpose) ? new ScreeningResult(false, false, null, r.Reason ?? "This couldn't be approved automatically.") : r;

    private static string MimeFor(string ext) => ext.ToLowerInvariant() switch
    {
        ".png" => "image/png", ".gif" => "image/gif", ".webp" => "image/webp", _ => "image/jpeg",
    };
}

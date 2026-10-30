using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace HalalBook.Web.Services;

/// <summary>Server upload limits, mirrored from the API so the browser can refuse oversized files before uploading them.</summary>
public record MediaLimitsDto(double MaxImageMB, double MaxCommentImageMB, double MaxVideoMB, int MaxPostVideoSeconds,
    int MaxReelSeconds, int MaxStoryVideoSeconds, int UploadsPerMinute);

/// <summary>Checks size and video length in the browser, then uploads with the right purpose.</summary>
public class MediaGuard(ApiClient api, IJSRuntime js, Toasts toasts)
{
    private MediaLimitsDto? _limits;

    public async Task<MediaLimitsDto> LimitsAsync() =>
        _limits ??= await api.GetAsync<MediaLimitsDto>("api/media/limits");

    /// <summary>
    /// Uploads the files that pass the limits and returns them; tells the person about any that don't.
    /// <paramref name="input"/> is the InputFile element, used to read video lengths in the browser.
    /// </summary>
    public async Task<List<HalalBook.Shared.UploadResult>> UploadAsync(IReadOnlyList<IBrowserFile> files, string purpose,
        ElementReference? input = null, Action<int, int>? progress = null)
    {
        var l = await LimitsAsync();
        double[] durations = [];
        if (input is { } el && files.Any(f => f.ContentType.StartsWith("video")))
        {
            try { durations = await js.InvokeAsync<double[]>("halalbook.videoDurations", el); } catch (JSException) { }
        }
        var maxSeconds = purpose switch { "reel" => l.MaxReelSeconds, "story" => l.MaxStoryVideoSeconds, _ => l.MaxPostVideoSeconds };
        var videoAllowed = purpose is "post" or "reel" or "story";

        var results = new List<HalalBook.Shared.UploadResult>();
        for (int i = 0; i < files.Count; i++)
        {
            progress?.Invoke(i + 1, files.Count);
            var f = files[i];
            var isVideo = f.ContentType.StartsWith("video");
            if (isVideo && !videoAllowed) { toasts.Show("Only photos can be added here.", true); continue; }
            if (!isVideo) f = await ShrinkAsync(f, purpose);
            var maxMb = isVideo ? l.MaxVideoMB : purpose == "comment" ? l.MaxCommentImageMB : l.MaxImageMB;
            if (f.Size > maxMb * 1_000_000) { toasts.Show($"{f.Name} is too large. The limit is {maxMb:0.#} MB.", true); continue; }
            if (isVideo && i < durations.Length && durations[i] > maxSeconds + 1)
            {
                toasts.Show($"{f.Name} is {Format((int)durations[i])} long. Videos here can be up to {Format(maxSeconds)}.", true);
                continue;
            }
            List<byte[]>? frames = null;
            if (isVideo && input is { } el2)
            {
                try { frames = (await js.InvokeAsync<string[]>("halalbook.videoFrames", el2, i, 5)).Select(Convert.FromBase64String).ToList(); }
                catch (JSException) { }
            }
            try { results.Add(await api.UploadAsync(f, (long)(maxMb * 1_000_000) + 1, purpose, frames)); }
            catch (Exception ex) { toasts.Error(ex); }
        }
        return results;
    }

    /// <summary>
    /// Resizes large photos in the browser before they are sent, so a 12 MB phone photo uploads as a few hundred KB
    /// and feeds load fast on slow connections. Animated GIFs and small images are left alone.
    /// </summary>
    private static async Task<IBrowserFile> ShrinkAsync(IBrowserFile f, string purpose)
    {
        var isHeic = f.ContentType is "image/heic" or "image/heif" || f.Name.EndsWith(".heic", StringComparison.OrdinalIgnoreCase);
        // iPhone HEIC photos are always converted, since browsers and the server can't show them as they are.
        if (f.ContentType is "image/gif" or "image/svg+xml" || (f.Size < 400_000 && !isHeic)) return f;
        var max = purpose switch { "profile" => 1080, "comment" or "message" => 1280, _ => 2048 };
        var format = f.ContentType == "image/png" ? "image/png" : "image/jpeg";
        try
        {
            var small = await f.RequestImageFileAsync(format, max, max);
            return small.Size > 0 && (isHeic || small.Size < f.Size) ? small : f;
        }
        catch (Exception) { return f; } // Formats the browser can't draw (such as some HEIC photos) upload as they are.
    }

    private static string Format(int s) => s >= 60 ? $"{s / 60}:{s % 60:00} min" : $"{s} seconds";
}

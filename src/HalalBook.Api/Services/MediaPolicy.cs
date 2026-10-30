using System.Buffers.Binary;
using HalalBook.Shared;

namespace HalalBook.Api.Services;

/// <summary>
/// Upload limits per use, so nobody can (on purpose or by accident) tie up the server with huge or very long media.
/// All values are configurable under "Media" in appsettings.
/// </summary>
public class MediaOptions
{
    public double MaxImageMB { get; set; } = 8;
    public double MaxCommentImageMB { get; set; } = 2;
    public double MaxVideoMB { get; set; } = 60;
    public int MaxPostVideoSeconds { get; set; } = 180;
    public int MaxReelSeconds { get; set; } = 90;
    public int MaxStoryVideoSeconds { get; set; } = 60;
    public int UploadsPerMinute { get; set; } = 20;
}

public enum UploadPurpose { Post, Reel, Story, Comment, Message, Profile, Cover }

public record MediaLimits(bool AllowsVideo, long MaxImageBytes, long MaxVideoBytes, int MaxVideoSeconds);

public class MediaPolicy(MediaOptions o)
{
    private static long Mb(double mb) => (long)(mb * 1_000_000);

    public MediaLimits For(UploadPurpose purpose) => purpose switch
    {
        UploadPurpose.Comment => new(false, Mb(o.MaxCommentImageMB), 0, 0),
        UploadPurpose.Message => new(false, Mb(o.MaxImageMB), 0, 0),
        UploadPurpose.Profile or UploadPurpose.Cover => new(false, Mb(o.MaxImageMB), 0, 0),
        UploadPurpose.Reel => new(true, Mb(o.MaxImageMB), Mb(o.MaxVideoMB), o.MaxReelSeconds),
        UploadPurpose.Story => new(true, Mb(o.MaxImageMB), Mb(o.MaxVideoMB), o.MaxStoryVideoSeconds),
        _ => new(true, Mb(o.MaxImageMB), Mb(o.MaxVideoMB), o.MaxPostVideoSeconds),
    };

    /// <summary>Returns an error message, or null when the file is within limits.</summary>
    public async Task<string?> CheckAsync(IFormFile file, MediaType type, UploadPurpose purpose)
    {
        var limits = For(purpose);
        if (type == MediaType.Video && !limits.AllowsVideo) return "Only photos can be added here.";
        var max = type == MediaType.Video ? limits.MaxVideoBytes : limits.MaxImageBytes;
        if (file.Length == 0) return "That file is empty.";
        if (file.Length > max) return $"{(type == MediaType.Video ? "Videos" : "Photos")} here can be up to {max / 1_000_000.0:0.#} MB.";
        if (type == MediaType.Video)
        {
            var seconds = await VideoDurationAsync(file);
            if (seconds is double s && s > limits.MaxVideoSeconds + 1)
                return $"Videos here can be up to {FormatSeconds(limits.MaxVideoSeconds)} long. This one is {FormatSeconds((int)s)}.";
        }
        return null;
    }

    public static string FormatSeconds(int s) => s >= 60 ? $"{s / 60}:{s % 60:00} min" : $"{s} seconds";

    /// <summary>Reads the duration from MP4/MOV ("mvhd") or WebM (EBML Duration) headers without decoding the video.</summary>
    public static async Task<double?> VideoDurationAsync(IFormFile file)
    {
        await using var s = file.OpenReadStream();
        var buffer = new byte[Math.Min(file.Length, 8 * 1024 * 1024)];
        int read = await s.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false);
        var data = buffer.AsSpan(0, read);
        return Mp4Duration(data) ?? WebmDuration(data);
    }

    public static double? Mp4Duration(ReadOnlySpan<byte> data)
    {
        // The movie header may sit at the end of the file for some encoders; scan what we have for "mvhd".
        var idx = data.IndexOf("mvhd"u8);
        if (idx < 4 || idx + 32 > data.Length) return null;
        var p = data[(idx + 4)..];
        int version = p[0];
        if (version == 1 && p.Length >= 32)
        {
            var scale = BinaryPrimitives.ReadUInt32BigEndian(p[20..]);
            var duration = BinaryPrimitives.ReadUInt64BigEndian(p[24..]);
            return scale == 0 ? null : (double)duration / scale;
        }
        if (p.Length >= 20)
        {
            var scale = BinaryPrimitives.ReadUInt32BigEndian(p[12..]);
            var duration = BinaryPrimitives.ReadUInt32BigEndian(p[16..]);
            return scale == 0 ? null : (double)duration / scale;
        }
        return null;
    }

    public static double? WebmDuration(ReadOnlySpan<byte> data)
    {
        // TimecodeScale (2A D7 B1, default 1,000,000 ns) and Duration (44 89, float) inside Segment Info.
        double scale = 1_000_000;
        var ts = data.IndexOf(new byte[] { 0x2A, 0xD7, 0xB1 });
        if (ts >= 0 && ts + 4 < data.Length)
        {
            int len = data[ts + 3] & 0x7F;
            if (len is > 0 and <= 8 && ts + 4 + len <= data.Length)
            {
                ulong v = 0;
                foreach (var b in data.Slice(ts + 4, len)) v = (v << 8) | b;
                scale = v;
            }
        }
        var d = data.IndexOf(new byte[] { 0x44, 0x89 });
        if (d < 0 || d + 3 >= data.Length) return null;
        int size = data[d + 2] & 0x7F;
        var start = d + 3;
        if (size == 4 && start + 4 <= data.Length) return BinaryPrimitives.ReadSingleBigEndian(data[start..]) * scale / 1e9;
        if (size == 8 && start + 8 <= data.Length) return BinaryPrimitives.ReadDoubleBigEndian(data[start..]) * scale / 1e9;
        return null; // live-recorded WebM often has no duration; the size cap still applies
    }
}

using HalalBook.Shared;

namespace HalalBook.Web.Services;

/// <summary>Toast messages shown at the bottom of the screen.</summary>
public class Toasts
{
    public record Toast(Guid Id, string Text, bool IsError);
    public List<Toast> Items { get; } = [];
    public event Action? Changed;

    public void Show(string text, bool isError = false)
    {
        var t = new Toast(Guid.NewGuid(), text, isError);
        Items.Add(t);
        Changed?.Invoke();
        _ = Task.Delay(4000).ContinueWith(_ => { Items.Remove(t); Changed?.Invoke(); });
    }

    public void Error(Exception ex) => Show(ex is ApiException ? ex.Message : "Something went wrong. Please try again.", true);
}

public static class Ui
{
    public static string TimeAgo(DateTime utc)
    {
        var span = DateTime.UtcNow - DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        if (span.TotalSeconds < 60) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h";
        if (span.TotalDays < 7) return $"{(int)span.TotalDays}d";
        return utc.ToString(utc.Year == DateTime.UtcNow.Year ? "MMM d" : "MMM d, yyyy");
    }

    public static string Emoji(ReactionType t) => t switch
    {
        ReactionType.Like => "👍",
        ReactionType.Love => "❤️",
        ReactionType.MashaAllah => "✨",
        ReactionType.SubhanAllah => "🌙",
        ReactionType.Ameen => "🤲",
        ReactionType.Sad => "😢",
        _ => "👍",
    };

    public static string Label(ReactionType t) => t switch
    {
        ReactionType.MashaAllah => "MashaAllah",
        ReactionType.SubhanAllah => "SubhanAllah",
        _ => t.ToString(),
    };

    public static string Initials(string name) =>
        string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpperInvariant(p[0])));

    public static string StoryBackground(string? key) => key switch
    {
        "sunset" => "linear-gradient(135deg,#f6a35c,#c0396b)",
        "night" => "linear-gradient(135deg,#0b1d3a,#36246b)",
        "gold" => "linear-gradient(135deg,#c9a227,#8a6d12)",
        "sky" => "linear-gradient(135deg,#4fb3d9,#1d5f8c)",
        _ => "linear-gradient(135deg,#13866a,#0b3d2e)",
    };

    public static string LastSeen(bool online, DateTime? lastSeen) =>
        online ? "Active now" : lastSeen is { } t ? $"Active {TimeAgo(t)}{(TimeAgo(t) == "just now" ? "" : " ago")}" : "";

    public static string Time(TimeOnly t) => t.ToString("h:mm tt");
}

using Microsoft.AspNetCore.SignalR;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using HalalBook.Api.Data;
using HalalBook.Shared;
using Microsoft.EntityFrameworkCore;

namespace HalalBook.Api.Services;

public partial class AiService
{
    private const string TranslatorRules =
        "You translate for HalalBook, a social network for Muslims. Keep Islamic terms in their usual form for that language " +
        "(Allah, InshaAllah, MashaAllah, Alhamdulillah, SubhanAllah, salah, wudu, Jumu'ah, ﷺ). Keep Quran verses and duas in Arabic exactly as written " +
        "and translate around them. Keep @mentions, #hashtags, emojis, links and line breaks unchanged.";

    /// <summary>Translates a batch of short interface strings; returns them in the same order, or null without AI.</summary>
    public async Task<List<string>?> TranslateBatchAsync(string languageName, IReadOnlyList<string> texts, CancellationToken ct)
    {
        if (!IsEnabled || texts.Count == 0) return null;
        var ai = await AskOnce(TranslatorRules + " These are labels and messages from the app's interface: keep them short and natural, like a native app. " +
            "Reply with only a JSON array of strings in the same order.",
            $"Translate into {languageName}:\n" + JsonSerializer.Serialize(texts), 4000, ct);
        if (ai is null) return null;
        try
        {
            var arr = JsonNode.Parse(ai[ai.IndexOf('[')..(ai.LastIndexOf(']') + 1)])!.AsArray().Select(n => n?.GetValue<string>() ?? "").ToList();
            return arr.Count == texts.Count ? arr : null;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentOutOfRangeException or InvalidOperationException) { return null; }
    }

    /// <summary>A short recap of a chat for someone catching up.</summary>
    public async Task<ChatSummaryDto> SummarizeChatAsync(List<(string Name, string Text)> lines, string languageName, CancellationToken ct)
    {
        if (lines.Count == 0) return new ChatSummaryDto("There's nothing to summarize yet.", false);
        if (!IsEnabled)
            return new ChatSummaryDto($"{lines.Count} recent messages, most recently from {lines[^1].Name}. Noor needs the AI assistant to summarize them.", false);
        var transcript = string.Join('\n', lines.Select(l => $"{l.Name}: {(l.Text.Length > 500 ? l.Text[..500] : l.Text)}"));
        var ai = await AskOnce("You are Noor, HalalBook's assistant. Summarize this chat for someone catching up, in " + languageName +
            ". Use 2 to 4 short bullet points (•): the gist, decisions or plans with dates and times, and anything waiting for their reply. " +
            "Be warm and brief. Don't invent anything.", transcript, 600, ct);
        return ai is null ? new ChatSummaryDto("Noor couldn't summarize this chat right now.", false) : new ChatSummaryDto(ai.Trim(), true);
    }

    /// <summary>Detects a post's language and translates it into several languages at once.</summary>
    public async Task<(string? Language, Dictionary<string, string> Translations)> TranslateToManyAsync(string text, IEnumerable<string> codes, CancellationToken ct)
    {
        var targets = codes.Select(Languages.Get).ToList();
        if (!IsEnabled || string.IsNullOrWhiteSpace(text)) return (Languages.GuessFromScript(text), []);
        var ai = await AskOnce(TranslatorRules +
            " Detect the language of the post, then translate it into each requested language. Reply with only JSON: " +
            "{\"source\": \"<ISO 639-1 code>\", \"translations\": {\"<code>\": \"<text>\", ...}}. Skip the source language.",
            $"Languages: {string.Join(", ", targets.Select(t => $"{t.Code} ({t.EnglishName})"))}\n\nPost:\n{text}", 6000, ct);
        if (ai is null) return (Languages.GuessFromScript(text), []);
        try
        {
            var json = JsonNode.Parse(ai[ai.IndexOf('{')..(ai.LastIndexOf('}') + 1)])!;
            var source = json["source"]?.GetValue<string>();
            var dict = json["translations"]?.AsObject().Where(kv => kv.Value is not null)
                .ToDictionary(kv => Languages.Normalize(kv.Key), kv => kv.Value!.GetValue<string>()) ?? [];
            return (source is null ? null : Languages.Normalize(source), dict);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            return (Languages.GuessFromScript(text), []);
        }
    }
}

/// <summary>Posts waiting to be translated for the whole Ummah, processed in the background so posting stays instant.</summary>
public class PostTranslationQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();
    public void Enqueue(Guid postId) => _channel.Writer.TryWrite(postId);
    public ChannelReader<Guid> Reader => _channel.Reader;
}

public class PostTranslationWorker(PostTranslationQueue queue, IServiceScopeFactory scopes, ILogger<PostTranslationWorker> log) : BackgroundService
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
                var (lang, translations) = await ai.TranslateToManyAsync(post.Content, Languages.Ummah, stoppingToken);
                post.Language = lang ?? post.Language;
                PostTranslations.Merge(post, translations);
                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Could not translate post {PostId}", id);
            }
        }
    }
}

/// <summary>Chat messages waiting to be translated into the languages of the people in the chat.</summary>
public class MessageTranslationQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();
    public void Enqueue(Guid messageId) => _channel.Writer.TryWrite(messageId);
    public ChannelReader<Guid> Reader => _channel.Reader;
}

public class MessageTranslationWorker(MessageTranslationQueue queue, IServiceScopeFactory scopes,
    Microsoft.AspNetCore.SignalR.IHubContext<HalalBook.Api.Hubs.RealtimeHub> hub, ILogger<MessageTranslationWorker> log) : BackgroundService
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
                var msg = await db.Messages.Include(m => m.Sender).FirstOrDefaultAsync(m => m.Id == id, stoppingToken);
                if (msg is null || string.IsNullOrWhiteSpace(msg.Content)) continue;
                var readers = await db.ConversationParticipants.Where(p => p.ConversationId == msg.ConversationId)
                    .Select(p => new { p.UserId, p.User.Language, p.User.AutoTranslate }).ToListAsync(stoppingToken);
                var wanted = readers.Where(r => r.AutoTranslate && r.UserId != msg.SenderId).Select(r => r.Language).Distinct().ToList();
                var guess = Languages.GuessFromScript(msg.Content);
                // Everyone reads the sender's language, and nothing suggests otherwise: no translation needed.
                if (wanted.Count == 0 || (wanted.All(l => l == msg.Sender.Language) && (guess is null || guess == msg.Sender.Language))) continue;

                var (lang, translations) = await ai.TranslateToManyAsync(msg.Content, wanted, stoppingToken);
                msg.Language = lang ?? guess;
                if (msg.Language is { } source) translations.Remove(source);
                if (translations.Count == 0) { await db.SaveChangesAsync(stoppingToken); continue; }
                msg.Translations = JsonSerializer.Serialize(translations);
                await db.SaveChangesAsync(stoppingToken);
                await hub.Clients.Users(readers.Select(r => r.UserId.ToString()).ToList()).SendAsync("messageTranslated",
                    new MessageTranslatedDto(msg.Id, msg.ConversationId, msg.Language, translations), stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Could not translate message {MessageId}", id);
            }
        }
    }
}

public static class PostTranslations
{
    public static Dictionary<string, string> Read(Post p) =>
        string.IsNullOrEmpty(p.Translations) ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(p.Translations) ?? [];

    public static void Merge(Post p, Dictionary<string, string> add)
    {
        if (add.Count == 0) return;
        var all = Read(p);
        foreach (var (k, v) in add) all[k] = v;
        p.Translations = JsonSerializer.Serialize(all);
    }
}

/// <summary>Interface text in any language: built-in translations first, then the database cache, then AI (saved for everyone).</summary>
public class UiTranslator(AppDbContext db, AiService ai)
{
    public static string Key(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32];

    public async Task<Dictionary<string, string>> TranslateAsync(string language, IReadOnlyList<string> texts, CancellationToken ct)
    {
        var lang = Languages.Normalize(language);
        var result = new Dictionary<string, string>();
        var wanted = texts.Where(t => !string.IsNullOrWhiteSpace(t) && t.Length <= 400).Distinct().Take(300).ToList();
        if (lang == "en") { foreach (var t in wanted) result[t] = t; return result; }

        var keys = wanted.ToDictionary(t => Key(t));
        var cached = await db.UiTranslations.Where(x => x.Language == lang && keys.Keys.Contains(x.Key)).ToListAsync(ct);
        foreach (var c in cached) result[keys[c.Key]] = c.Text;

        var missing = wanted.Where(t => !result.ContainsKey(t)).ToList();
        foreach (var chunk in missing.Chunk(60))
        {
            var translated = await ai.TranslateBatchAsync(Languages.Get(lang).EnglishName, chunk, ct);
            if (translated is null) break;
            for (int i = 0; i < chunk.Length; i++)
            {
                result[chunk[i]] = translated[i];
                db.UiTranslations.Add(new UiTranslation { Language = lang, Key = Key(chunk[i]), Source = chunk[i], Text = translated[i] });
            }
            await db.SaveChangesAsync(ct);
        }
        return result;
    }
}

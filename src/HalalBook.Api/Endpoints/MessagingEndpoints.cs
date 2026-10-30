using System.Text.Json;
using System.Security.Claims;
using HalalBook.Api.Data;
using HalalBook.Api.Hubs;
using HalalBook.Api.Services;
using HalalBook.Shared;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HalalBook.Api.Endpoints;

public static class MessagingEndpoints
{
    public static void MapMessagingEndpoints(this IEndpointRouteBuilder app)
    {
        var chats = app.MapGroup("/api/conversations").WithTags("Messaging").RequireAuthorization();

        chats.MapGet("/", async (ClaimsPrincipal cp, AppDbContext db) =>
        {
            var me = cp.UserId();
            var convs = await db.Conversations.Include(c => c.Participants).ThenInclude(p => p.User)
                .Where(c => c.Participants.Any(p => p.UserId == me))
                .OrderByDescending(c => c.LastActivityAt).Take(50).AsSplitQuery().ToListAsync();
            var result = new List<ConversationDto>();
            foreach (var c in convs)
            {
                var lastRead = c.Participants.First(p => p.UserId == me).LastReadAt;
                var last = await db.Messages.Include(m => m.Sender).Where(m => m.ConversationId == c.Id)
                    .OrderByDescending(m => m.SentAt).FirstOrDefaultAsync();
                var unread = await db.Messages.CountAsync(m => m.ConversationId == c.Id && m.SenderId != me && m.SentAt > lastRead);
                result.Add(ToDto(c, me, last, unread));
            }
            return result;
        });

        // Opens the existing 1:1 conversation, or starts a new one (group when more than one other person).
        chats.MapPost("/", async (StartConversationRequest req, ClaimsPrincipal cp, AppDbContext db, PostService svc, PrivacyService privacy) =>
        {
            var me = cp.UserId();
            var others = req.ParticipantIds.Where(id => id != me).Distinct().ToList();
            if (others.Count == 0) return Results.BadRequest();
            var blocked = await svc.BlockedIdsAsync(me);
            if (others.Any(blocked.Contains)) return Results.Forbid();
            if (await db.Users.CountAsync(u => others.Contains(u.Id)) != others.Count) return Results.BadRequest();

            if (others.Count == 1)
            {
                var other = others[0];
                var existing = await db.Conversations.Include(c => c.Participants).ThenInclude(p => p.User)
                    .Where(c => !c.IsGroup && c.Participants.Any(p => p.UserId == me) && c.Participants.Any(p => p.UserId == other))
                    .FirstOrDefaultAsync();
                if (existing is not null) return Results.Ok(ToDto(existing, me, null, 0));
            }
            // New conversations respect each person's "who can message me" setting.
            foreach (var other in await db.Users.Where(u => others.Contains(u.Id)).ToListAsync())
                if (!PrivacyService.Allows(other.WhoCanMessage, await privacy.RelationAsync(me, other.Id)))
                    return Results.Problem($"{other.DisplayName} only accepts messages from {PrivacyService.Describe(other.WhoCanMessage)}.", statusCode: 403);
            var conv = new Conversation { IsGroup = others.Count > 1, Title = req.Title };
            conv.Participants.AddRange(others.Append(me).Select(id => new ConversationParticipant { UserId = id }));
            db.Conversations.Add(conv);
            await db.SaveChangesAsync();
            var full = await db.Conversations.Include(c => c.Participants).ThenInclude(p => p.User).FirstAsync(c => c.Id == conv.Id);
            return Results.Ok(ToDto(full, me, null, 0));
        });

        chats.MapGet("/{id:guid}/messages", async (Guid id, DateTime? before, ClaimsPrincipal cp, AppDbContext db, IHubContext<RealtimeHub> hub) =>
        {
            var me = cp.UserId();
            var participant = await db.ConversationParticipants.FirstOrDefaultAsync(p => p.ConversationId == id && p.UserId == me);
            if (participant is null) return Results.NotFound();
            var q = db.Messages.Include(m => m.Sender).Include(m => m.ReplyTo).ThenInclude(r => r!.Sender).Include(m => m.Reactions)
                .Where(m => m.ConversationId == id);
            if (before is DateTime b) q = q.Where(m => m.SentAt < b);
            var msgs = await q.OrderByDescending(m => m.SentAt).Take(50).ToListAsync();
            participant.LastReadAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await AnnounceReadAsync(db, hub, id, me, participant.LastReadAt);
            return Results.Ok(msgs.OrderBy(m => m.SentAt).Select(ToDto).ToList());
        });

        chats.MapPost("/{id:guid}/messages", async (Guid id, SendMessageRequest req, ClaimsPrincipal cp, AppDbContext db,
            IHubContext<RealtimeHub> hub, ContentModerator moderator, PostService svc, PushQueue push, MessageTranslationQueue translations) =>
        {
            var me = cp.UserId();
            var conv = await db.Conversations.Include(c => c.Participants).FirstOrDefaultAsync(c => c.Id == id);
            if (conv is null || conv.Participants.All(p => p.UserId != me)) return Results.NotFound();
            // A block on either side stops the conversation, even an old one.
            var blocked = await svc.BlockedIdsAsync(me);
            if (!conv.IsGroup && conv.Participants.Any(p => p.UserId != me && blocked.Contains(p.UserId)))
                return Results.Problem("You can't message this person.", statusCode: 403);
            var content = (req.Content ?? "").Trim();
            if (content.Length == 0 && string.IsNullOrEmpty(req.MediaUrl) && req.SharedPostId is null) return Results.BadRequest();
            var mod = moderator.Check(content);
            if (!mod.Allowed) return Results.Problem(string.Join(" ", mod.Reasons), statusCode: 422);

            var msg = new Message { ConversationId = id, SenderId = me, Content = content, MediaUrl = req.MediaUrl };
            if (req.ReplyToId is Guid rid)
            {
                msg.ReplyTo = await db.Messages.Include(m => m.Sender).FirstOrDefaultAsync(m => m.Id == rid && m.ConversationId == id);
                msg.ReplyToId = msg.ReplyTo?.Id;
            }
            if (req.SharedPostId is Guid shared && !await AttachPostAsync(msg, shared, me, svc)) return Results.NotFound();
            db.Messages.Add(msg);
            conv.LastActivityAt = msg.SentAt;
            conv.Participants.First(p => p.UserId == me).LastReadAt = msg.SentAt;
            await db.SaveChangesAsync();
            msg.Sender = (await db.Users.FindAsync(me))!;
            var dto = ToDto(msg);
            await hub.Clients.Users(conv.Participants.Select(p => p.UserId.ToString())).SendAsync("message", dto);
            var preview = content.Length > 0 ? content : msg.MediaUrl is not null ? "📷 Photo" : "Shared a post";
            foreach (var p in conv.Participants.Where(p => p.UserId != me && !p.Muted))
                push.Enqueue(p.UserId, msg.Sender.DisplayName, preview, $"/messages/{conv.Id}", $"chat-{conv.Id}");
            if (content.Length > 0) translations.Enqueue(msg.Id);
            return Results.Ok(dto);
        });

        chats.MapPost("/send-post", async (SendPostRequest req, ClaimsPrincipal cp, AppDbContext db, PostService svc, PrivacyService privacy,
            IHubContext<RealtimeHub> hub, ContentModerator moderator) =>
        {
            var me = cp.UserId();
            var note = (req.Note ?? "").Trim();
            if (!moderator.Check(note).Allowed) return Results.Problem("That message isn't allowed.", statusCode: 422);
            var blocked = await svc.BlockedIdsAsync(me);
            var sent = 0;
            foreach (var uid in req.UserIds.Distinct().Where(u => u != me && !blocked.Contains(u)).Take(50))
            {
                var conv = await db.Conversations.Include(c => c.Participants)
                    .FirstOrDefaultAsync(c => !c.IsGroup && c.Participants.Any(p => p.UserId == me) && c.Participants.Any(p => p.UserId == uid));
                if (conv is null)
                {
                    var other = await db.Users.FindAsync(uid);
                    if (other is null || !PrivacyService.Allows(other.WhoCanMessage, await privacy.RelationAsync(me, uid))) continue;
                    conv = new Conversation();
                    conv.Participants.AddRange([new ConversationParticipant { UserId = me }, new ConversationParticipant { UserId = uid }]);
                    db.Conversations.Add(conv);
                }
                var msg = new Message { ConversationId = conv.Id, SenderId = me, Content = note };
                if (!await AttachPostAsync(msg, req.PostId, me, svc)) return Results.NotFound();
                db.Messages.Add(msg);
                conv.LastActivityAt = msg.SentAt;
                await db.SaveChangesAsync();
                msg.Sender = (await db.Users.FindAsync(me))!;
                await hub.Clients.Users([me.ToString(), uid.ToString()]).SendAsync("message", ToDto(msg));
                sent++;
            }
            return Results.Ok(sent);
        });

        // Mute: keep the chat, but no notifications or sounds.
        chats.MapPut("/{id:guid}/mute", async (Guid id, MuteRequest req, ClaimsPrincipal cp, AppDbContext db) =>
        {
            var p = await db.ConversationParticipants.FirstOrDefaultAsync(p => p.ConversationId == id && p.UserId == cp.UserId());
            if (p is null) return Results.NotFound();
            p.Muted = req.Muted;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // Noor's recap of the latest messages, in the reader's language.
        chats.MapGet("/{id:guid}/summary", async (Guid id, ClaimsPrincipal cp, AppDbContext db, AiService ai, CancellationToken ct) =>
        {
            var me = cp.UserId();
            if (!await db.ConversationParticipants.AnyAsync(p => p.ConversationId == id && p.UserId == me, ct)) return Results.NotFound();
            var user = await db.Users.FindAsync([me], ct);
            var msgs = await db.Messages.Include(m => m.Sender).Where(m => m.ConversationId == id && m.Content != "")
                .OrderByDescending(m => m.SentAt).Take(60).ToListAsync(ct);
            msgs.Reverse();
            var lines = msgs.Select(m => (m.Sender.DisplayName, m.Content)).ToList();
            return Results.Ok(await ai.SummarizeChatAsync(lines, Languages.Get(user?.Language).EnglishName, ct));
        }).RequireRateLimiting("ai");

        chats.MapPost("/{id:guid}/read", async (Guid id, ClaimsPrincipal cp, AppDbContext db, IHubContext<RealtimeHub> hub) =>
        {
            var p = await db.ConversationParticipants.FirstOrDefaultAsync(x => x.ConversationId == id && x.UserId == cp.UserId());
            if (p is null) return Results.NotFound();
            p.LastReadAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await AnnounceReadAsync(db, hub, id, p.UserId, p.LastReadAt);
            return Results.Ok();
        });

        // One emoji per person per message; the same emoji again (or none) takes it back.
        chats.MapPost("/{id:guid}/messages/{messageId:guid}/react", async (Guid id, Guid messageId, ReactToMessageRequest req, ClaimsPrincipal cp,
            AppDbContext db, IHubContext<RealtimeHub> hub) =>
        {
            var me = cp.UserId();
            var conv = await db.Conversations.Include(c => c.Participants).FirstOrDefaultAsync(c => c.Id == id);
            if (conv is null || conv.Participants.All(p => p.UserId != me)) return Results.NotFound();
            if (!await db.Messages.AnyAsync(m => m.Id == messageId && m.ConversationId == id)) return Results.NotFound();
            var emoji = req.Emoji?.Trim();
            if (emoji is { Length: > 16 }) return Results.BadRequest();
            var existing = await db.MessageReactions.FindAsync(messageId, me);
            if (existing is not null && (string.IsNullOrEmpty(emoji) || existing.Emoji == emoji)) db.MessageReactions.Remove(existing);
            else if (existing is not null) existing.Emoji = emoji!;
            else if (!string.IsNullOrEmpty(emoji)) db.MessageReactions.Add(new MessageReaction { MessageId = messageId, UserId = me, Emoji = emoji });
            await db.SaveChangesAsync();
            var all = await db.MessageReactions.Where(r => r.MessageId == messageId).Select(r => new MessageReactionDto(r.UserId, r.Emoji)).ToListAsync();
            var dto = new MessageReactionsDto(messageId, id, all);
            await hub.Clients.Users(conv.Participants.Select(p => p.UserId.ToString()).ToList()).SendAsync("messageReactions", dto);
            return Results.Ok(dto);
        });

        app.MapGet("/api/conversations/unread-count", async (ClaimsPrincipal cp, AppDbContext db) =>
        {
            var me = cp.UserId();
            return await db.ConversationParticipants.Where(p => p.UserId == me)
                .CountAsync(p => db.Messages.Any(m => m.ConversationId == p.ConversationId && m.SenderId != me && m.SentAt > p.LastReadAt));
        }).WithTags("Messaging").RequireAuthorization();

        // ---------- Notifications ----------
        var notes = app.MapGroup("/api/notifications").WithTags("Notifications").RequireAuthorization();

        notes.MapGet("/", async (ClaimsPrincipal cp, AppDbContext db) =>
            await db.Notifications.Where(n => n.UserId == cp.UserId()).OrderByDescending(n => n.CreatedAt).Take(50)
                .Select(n => new NotificationDto(n.Id, n.Type,
                    n.Actor == null ? null : new UserSummaryDto(n.Actor.Id, n.Actor.UserName, n.Actor.DisplayName, n.Actor.AvatarUrl, n.Actor.IsVerified),
                    n.Message, n.Link, n.IsRead, n.CreatedAt)).ToListAsync());

        notes.MapGet("/unread-count", async (ClaimsPrincipal cp, AppDbContext db) =>
            await db.Notifications.CountAsync(n => n.UserId == cp.UserId() && !n.IsRead));

        notes.MapPost("/read-all", async (ClaimsPrincipal cp, AppDbContext db) =>
        {
            await db.Notifications.Where(n => n.UserId == cp.UserId() && !n.IsRead).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
            return Results.Ok();
        });

        notes.MapPost("/{id:guid}/read", async (Guid id, ClaimsPrincipal cp, AppDbContext db) =>
        {
            await db.Notifications.Where(n => n.Id == id && n.UserId == cp.UserId()).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
            return Results.Ok();
        });
    }

    private static ConversationDto ToDto(Conversation c, Guid me, Message? last, int unread)
    {
        var others = c.Participants.Where(p => p.UserId != me).Select(p => p.User.ToSummary()).ToList();
        var title = c.Title ?? (others.Count == 0 ? "Notes to self" : string.Join(", ", others.Select(o => o.DisplayName)));
        var othersRead = c.Participants.Where(p => p.UserId != me).Select(p => (DateTime?)p.LastReadAt).DefaultIfEmpty().Min();
        return new ConversationDto(c.Id, title, c.IsGroup, others, last is null ? null : ToDto(last), unread,
            c.Participants.FirstOrDefault(p => p.UserId == me)?.Muted ?? false, othersRead);
    }

    private static MessageDto ToDto(Message m) => new(m.Id, m.ConversationId, m.Sender.ToSummary(), m.Content, m.MediaUrl, m.SentAt,
        m.SharedPostId is Guid pid ? new SharedPostPreview(pid, m.SharedAuthorName ?? "", m.SharedPreview, m.SharedImageUrl) : null,
        m.Language, string.IsNullOrEmpty(m.Translations) ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(m.Translations),
        m.ReplyTo is { } r ? new ReplyPreviewDto(r.Id, r.Sender?.DisplayName ?? "",
            r.Content.Length > 0 ? (r.Content.Length > 120 ? r.Content[..120] + "…" : r.Content) : r.MediaUrl is not null ? "📎" : "") : null,
        m.Reactions.Count == 0 ? null : m.Reactions.Select(x => new MessageReactionDto(x.UserId, x.Emoji)).ToList());

    /// <summary>Tells the others in the chat how far this person has read, for "Seen".</summary>
    private static async Task AnnounceReadAsync(AppDbContext db, IHubContext<RealtimeHub> hub, Guid conversationId, Guid userId, DateTime at)
    {
        var others = await db.ConversationParticipants.Where(p => p.ConversationId == conversationId && p.UserId != userId)
            .Select(p => p.UserId.ToString()).ToListAsync();
        await hub.Clients.Users(others).SendAsync("conversationRead", new ConversationReadDto(conversationId, userId, at));
    }

    /// <summary>Attaches a preview of a post the sender is allowed to see.</summary>
    private static async Task<bool> AttachPostAsync(Message msg, Guid postId, Guid me, PostService svc)
    {
        var visible = await svc.VisibleToAsync(me);
        var post = await visible.Include(p => p.Author).Include(p => p.Media).FirstOrDefaultAsync(p => p.Id == postId);
        if (post is null) return false;
        msg.SharedPostId = post.Id;
        msg.SharedAuthorName = post.Author.DisplayName;
        msg.SharedPreview = post.Content.Length > 200 ? post.Content[..200] + "…" : post.Content;
        msg.SharedImageUrl = post.Media.OrderBy(m => m.SortOrder).FirstOrDefault(m => m.Type == MediaType.Image)?.Url;
        return true;
    }
}

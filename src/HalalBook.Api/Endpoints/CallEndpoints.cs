using System.Security.Claims;
using HalalBook.Api.Data;
using HalalBook.Api.Hubs;
using HalalBook.Api.Services;
using HalalBook.Shared;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HalalBook.Api.Endpoints;

/// <summary>WhatsApp-style calls: ring, answer or decline, end; a summary line is posted in the chat.</summary>
public static class CallEndpoints
{
    public static void MapCallEndpoints(this IEndpointRouteBuilder app)
    {
        var calls = app.MapGroup("/api/calls").WithTags("Calls").RequireAuthorization();

        calls.MapPost("/", async (StartCallRequest req, ClaimsPrincipal cp, AppDbContext db, IHubContext<RealtimeHub> hub, PostService svc, PushQueue push) =>
        {
            var me = cp.UserId();
            var conv = await db.Conversations.Include(c => c.Participants).ThenInclude(p => p.User).FirstOrDefaultAsync(c => c.Id == req.ConversationId);
            if (conv is null || conv.Participants.All(p => p.UserId != me)) return Results.NotFound();
            var blocked = await svc.BlockedIdsAsync(me);
            if (conv.Participants.Any(p => p.UserId != me && blocked.Contains(p.UserId))) return Results.Forbid();
            // One active call per conversation: join it instead of ringing again.
            var since = DateTime.UtcNow.AddHours(-6);
            var active = await db.Calls.Include(c => c.Caller)
                .FirstOrDefaultAsync(c => c.ConversationId == conv.Id && (c.Status == CallStatus.Ringing || c.Status == CallStatus.Active) && c.CreatedAt > since);
            if (active is not null) return Results.Ok(ToDto(active, conv, me));

            var call = new Call { ConversationId = conv.Id, CallerId = me, IsVideo = req.Video, Status = CallStatus.Ringing };
            db.Calls.Add(call);
            await db.SaveChangesAsync();
            call.Caller = (await db.Users.FindAsync(me))!;
            var others = conv.Participants.Where(p => p.UserId != me).Select(p => p.UserId.ToString()).ToList();
            foreach (var p in conv.Participants.Where(p => p.UserId != me))
            {
                await hub.Clients.User(p.UserId.ToString()).SendAsync("incomingCall", ToDto(call, conv, p.UserId));
                // Ring locked phones too, even when the app is open in a background tab.
                push.Enqueue(p.UserId, call.IsVideo ? "🎥 Incoming video call" : "📞 Incoming voice call",
                    $"{call.Caller.DisplayName} is calling you", $"/call/{call.Id}", $"call-{call.Id}", onlyWhenAway: false);
            }
            return Results.Ok(ToDto(call, conv, me));
        });

        calls.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal cp, AppDbContext db) =>
        {
            var me = cp.UserId();
            var call = await db.Calls.Include(c => c.Caller).FirstOrDefaultAsync(c => c.Id == id);
            if (call is null) return Results.NotFound();
            var conv = await db.Conversations.Include(c => c.Participants).ThenInclude(p => p.User).FirstAsync(c => c.Id == call.ConversationId);
            return conv.Participants.Any(p => p.UserId == me) ? Results.Ok(ToDto(call, conv, me)) : Results.NotFound();
        });

        calls.MapPost("/{id:guid}/accept", async (Guid id, ClaimsPrincipal cp, AppDbContext db, IHubContext<RealtimeHub> hub) =>
        {
            var (call, conv, error) = await Load(db, id, cp.UserId());
            if (error is not null) return error;
            if (call!.Status is CallStatus.Ended or CallStatus.Missed or CallStatus.Declined) return Results.Problem("This call has ended.", statusCode: 400);
            if (call.Status == CallStatus.Ringing) { call.Status = CallStatus.Active; call.AnsweredAt = DateTime.UtcNow; await db.SaveChangesAsync(); }
            await hub.Clients.Users(conv!.Participants.Select(p => p.UserId.ToString())).SendAsync("callUpdated", call.Id, call.Status.ToString(), cp.UserId());
            return Results.Ok(ToDto(call, conv, cp.UserId()));
        });

        calls.MapPost("/{id:guid}/decline", async (Guid id, ClaimsPrincipal cp, AppDbContext db, IHubContext<RealtimeHub> hub) =>
        {
            var me = cp.UserId();
            var (call, conv, error) = await Load(db, id, me);
            if (error is not null) return error;
            // In a group call, one person declining doesn't end it for everyone.
            if (call!.Status == CallStatus.Ringing && !conv!.IsGroup)
                await FinishAsync(db, hub, call, conv, CallStatus.Declined);
            else
                await hub.Clients.User(call.CallerId.ToString()).SendAsync("callUpdated", call.Id, "Declined", me);
            return Results.Ok();
        });

        calls.MapPost("/{id:guid}/end", async (Guid id, ClaimsPrincipal cp, AppDbContext db, IHubContext<RealtimeHub> hub, CallRoomTracker rooms) =>
        {
            var (call, conv, error) = await Load(db, id, cp.UserId());
            if (error is not null) return error;
            if (call!.Status is CallStatus.Ringing or CallStatus.Active)
            {
                // Group calls continue while others are still connected; 1:1 calls end for both.
                if (conv!.IsGroup && call.Status == CallStatus.Active && rooms.Count(call.Id) > 1) return Results.Ok();
                await FinishAsync(db, hub, call, conv!, call.Status == CallStatus.Ringing ? CallStatus.Missed : CallStatus.Ended);
            }
            return Results.Ok();
        });

        calls.MapGet("/history", async (ClaimsPrincipal cp, AppDbContext db) =>
        {
            var me = cp.UserId();
            var convIds = db.ConversationParticipants.Where(p => p.UserId == me).Select(p => p.ConversationId);
            var list = await db.Calls.Include(c => c.Caller).Where(c => convIds.Contains(c.ConversationId)).OrderByDescending(c => c.CreatedAt).Take(50).ToListAsync();
            var convs = await db.Conversations.Include(c => c.Participants).ThenInclude(p => p.User)
                .Where(c => list.Select(x => x.ConversationId).Contains(c.Id)).ToDictionaryAsync(c => c.Id);
            return list.Select(c => ToDto(c, convs[c.ConversationId], me)).ToList();
        });
    }

    private static async Task<(Call?, Conversation?, IResult?)> Load(AppDbContext db, Guid id, Guid me)
    {
        var call = await db.Calls.Include(c => c.Caller).FirstOrDefaultAsync(c => c.Id == id);
        if (call is null) return (null, null, Results.NotFound());
        var conv = await db.Conversations.Include(c => c.Participants).ThenInclude(p => p.User).FirstAsync(c => c.Id == call.ConversationId);
        return conv.Participants.Any(p => p.UserId == me) ? (call, conv, null) : (null, null, Results.NotFound());
    }

    /// <summary>Ends the call, tells everyone, and adds a WhatsApp-style line to the chat ("Missed video call", "Call · 4:12").</summary>
    private static async Task FinishAsync(AppDbContext db, IHubContext<RealtimeHub> hub, Call call, Conversation conv, CallStatus status)
    {
        call.Status = status;
        call.EndedAt = DateTime.UtcNow;
        var kind = call.IsVideo ? "video call" : "voice call";
        var text = status switch
        {
            CallStatus.Missed => $"📵 Missed {kind}",
            CallStatus.Declined => $"📵 {char.ToUpper(kind[0])}{kind[1..]} declined",
            _ when call.AnsweredAt is { } a => $"{(call.IsVideo ? "🎥" : "📞")} {char.ToUpper(kind[0])}{kind[1..]} · {Duration(call.EndedAt.Value - a)}",
            _ => $"📵 {char.ToUpper(kind[0])}{kind[1..]} cancelled",
        };
        var msg = new Message { ConversationId = conv.Id, SenderId = call.CallerId, Content = text };
        db.Messages.Add(msg);
        conv.LastActivityAt = msg.SentAt;
        await db.SaveChangesAsync();
        var users = conv.Participants.Select(p => p.UserId.ToString()).ToList();
        await hub.Clients.Users(users).SendAsync("callUpdated", call.Id, status.ToString(), call.CallerId);
        msg.Sender = call.Caller;
        await hub.Clients.Users(users).SendAsync("message", new MessageDto(msg.Id, msg.ConversationId, msg.Sender.ToSummary(), msg.Content, null, msg.SentAt));
    }

    private static string Duration(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    private static CallDto ToDto(Call c, Conversation conv, Guid viewer)
    {
        var others = conv.Participants.Where(p => p.UserId != viewer).Select(p => p.User.ToSummary()).ToList();
        var title = conv.Title ?? string.Join(", ", others.Select(o => o.DisplayName));
        return new CallDto(c.Id, conv.Id, title, c.IsVideo, conv.IsGroup, c.Status, c.Caller.ToSummary(),
            conv.Participants.Select(p => p.User.ToSummary()).ToList(), c.CreatedAt, c.AnsweredAt, c.EndedAt);
    }
}

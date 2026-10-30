using HalalBook.Api.Data;
using HalalBook.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HalalBook.Api.Hubs;

/// <summary>
/// Pushes live events to clients: "message" (new chat message), "notification", "typing" and "presence".
/// Users are addressed by their user id (the JWT NameIdentifier claim).
/// </summary>
[Authorize]
public class RealtimeHub(AppDbContext db, PresenceTracker presence) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var me = Context.User!.UserId();
        if (presence.Connect(me)) await BroadcastAsync(me, online: true);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var me = Context.User!.UserId();
        if (presence.Disconnect(me))
        {
            await db.Users.Where(u => u.Id == me).ExecuteUpdateAsync(s => s.SetProperty(u => u.LastSeenAt, DateTime.UtcNow));
            await BroadcastAsync(me, online: false);
        }
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>Tells everyone this person came online or went offline, unless they hide their active status.</summary>
    private async Task BroadcastAsync(Guid userId, bool online)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId && u.ShowOnlineStatus)) return;
        await Clients.Others.SendAsync("presence", userId, online);
    }

    public async Task Typing(Guid conversationId)
    {
        var me = Context.User!.UserId();
        if (!await db.ConversationParticipants.AnyAsync(p => p.ConversationId == conversationId && p.UserId == me)) return;
        var others = await db.ConversationParticipants
            .Where(p => p.ConversationId == conversationId && p.UserId != me)
            .Select(p => p.UserId.ToString()).ToListAsync();
        if (others.Count == 0) return;
        await Clients.Users(others).SendAsync("typing", conversationId, me);
    }
}

using HalalBook.Api.Data;
using HalalBook.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HalalBook.Api.Hubs;

/// <summary>
/// Pushes live events to clients: "message" (new chat message), "notification", and "typing".
/// Users are addressed by their user id (the JWT NameIdentifier claim).
/// </summary>
[Authorize]
public class RealtimeHub(AppDbContext db) : Hub
{
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

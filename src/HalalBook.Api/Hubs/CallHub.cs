using HalalBook.Api.Data;
using HalalBook.Api.Services;
using HalalBook.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HalalBook.Api.Hubs;

/// <summary>
/// WebRTC signaling for audio and video calls. Same protocol as live rooms (Join, Signal, Leave),
/// but only members of the call's conversation can join, and everyone sends audio (and video for video calls).
/// </summary>
[Authorize]
public class CallHub(AppDbContext db, CallRoomTracker rooms) : Hub
{
    private static string Group(Guid id) => $"call-{id}";

    public async Task<List<LiveParticipantDto>> Join(Guid callId)
    {
        var me = Context.User!.UserId();
        var call = await db.Calls.FirstOrDefaultAsync(c => c.Id == callId) ?? throw new HubException("This call doesn't exist.");
        if (call.Status is CallStatus.Ended or CallStatus.Missed or CallStatus.Declined) throw new HubException("This call has ended.");
        if (!await db.ConversationParticipants.AnyAsync(p => p.ConversationId == call.ConversationId && p.UserId == me))
            throw new HubException("You're not part of this call.");
        var user = (await db.Users.FindAsync(me))!;
        var existing = rooms.In(callId);
        var self = new LiveParticipantDto(Context.ConnectionId, user.ToSummary(), call.CallerId == me, true);
        rooms.Add(callId, self);
        await Groups.AddToGroupAsync(Context.ConnectionId, Group(callId));
        await Clients.OthersInGroup(Group(callId)).SendAsync("peerJoined", self);
        await Clients.Group(Group(callId)).SendAsync("viewers", rooms.Count(callId));
        return existing;
    }

    public async Task Leave(Guid callId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(callId));
        await LeftAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await LeftAsync();
        await base.OnDisconnectedAsync(exception);
    }

    private async Task LeftAsync()
    {
        if (rooms.Remove(Context.ConnectionId) is not { } left) return;
        await Clients.Group(Group(left.SessionId)).SendAsync("peerLeft", left.Participant.ConnectionId);
        var remaining = rooms.Count(left.SessionId);
        await Clients.Group(Group(left.SessionId)).SendAsync("viewers", remaining);
    }

    public async Task Signal(Guid callId, string toConnectionId, string type, string payload)
    {
        if (!rooms.Contains(callId, Context.ConnectionId) || !rooms.Contains(callId, toConnectionId)) return;
        await Clients.Client(toConnectionId).SendAsync("signal", Context.ConnectionId, type, payload);
    }
}

/// <summary>Who is connected to each call (separate from live rooms).</summary>
public class CallRoomTracker : LiveRoomTracker;

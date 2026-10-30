using System.Collections.Concurrent;
using HalalBook.Api.Data;
using HalalBook.Api.Services;
using HalalBook.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HalalBook.Api.Hubs;

/// <summary>Who is in each live room, keyed by SignalR connection.</summary>
public class LiveRoomTracker
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, LiveParticipantDto>> _rooms = new();
    private readonly ConcurrentDictionary<string, Guid> _roomOf = new();

    public void Add(Guid sessionId, LiveParticipantDto p)
    {
        _rooms.GetOrAdd(sessionId, _ => new())[p.ConnectionId] = p;
        _roomOf[p.ConnectionId] = sessionId;
    }

    public (Guid SessionId, LiveParticipantDto Participant)? Remove(string connectionId)
    {
        if (!_roomOf.TryRemove(connectionId, out var sessionId)) return null;
        if (_rooms.TryGetValue(sessionId, out var room) && room.TryRemove(connectionId, out var p)) return (sessionId, p);
        return null;
    }

    public List<LiveParticipantDto> In(Guid sessionId) => _rooms.TryGetValue(sessionId, out var r) ? r.Values.ToList() : [];
    public int Count(Guid sessionId) => _rooms.TryGetValue(sessionId, out var r) ? r.Count : 0;
    public bool Contains(Guid sessionId, string connectionId) => _rooms.TryGetValue(sessionId, out var r) && r.ContainsKey(connectionId);
}

/// <summary>
/// Live rooms: WebRTC signaling (offers, answers, ICE candidates relayed peer to peer), live chat,
/// reactions and viewer counts. Media flows browser to browser; this hub only introduces peers.
/// </summary>
[Authorize]
public class LiveHub(AppDbContext db, LiveRoomTracker rooms, ContentModerator moderator) : Hub
{
    private static string Group(Guid id) => $"live-{id}";

    /// <summary>Joins a live room and returns everyone already in it.</summary>
    public async Task<List<LiveParticipantDto>> Join(Guid sessionId)
    {
        var me = Context.User!.UserId();
        var session = await db.LiveSessions.FirstOrDefaultAsync(s => s.Id == sessionId)
                      ?? throw new HubException("This live session doesn't exist.");
        if (session.Status == LiveStatus.Ended) throw new HubException("This live session has ended.");
        var user = await db.Users.FindAsync(me) ?? throw new HubException("Sign in again.");

        var isHost = session.HostId == me;
        // In a conference everyone shares camera and mic; in shows, events and podcasts only the host does.
        var sends = isHost || session.Kind == LiveKind.Conference;
        var existing = rooms.In(sessionId);
        var self = new LiveParticipantDto(Context.ConnectionId, user.ToSummary(), isHost, sends);
        rooms.Add(sessionId, self);
        await Groups.AddToGroupAsync(Context.ConnectionId, Group(sessionId));
        await Clients.OthersInGroup(Group(sessionId)).SendAsync("peerJoined", self);
        await BroadcastCountAsync(session);
        return existing;
    }

    public async Task Leave(Guid sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(sessionId));
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
        if (await db.LiveSessions.FindAsync(left.SessionId) is { } session) await BroadcastCountAsync(session);
    }

    /// <summary>Relays an SDP offer/answer or ICE candidate to one peer in the same room.</summary>
    public async Task Signal(Guid sessionId, string toConnectionId, string type, string payload)
    {
        if (!rooms.Contains(sessionId, Context.ConnectionId) || !rooms.Contains(sessionId, toConnectionId)) return;
        await Clients.Client(toConnectionId).SendAsync("signal", Context.ConnectionId, type, payload);
    }

    public async Task Chat(Guid sessionId, string text)
    {
        if (!rooms.Contains(sessionId, Context.ConnectionId)) return;
        text = (text ?? "").Trim();
        if (text.Length is 0 or > 500) return;
        var check = moderator.Check(text);
        if (!check.Allowed || check.NeedsReview) throw new HubException("That message isn't allowed here.");
        var user = (await db.Users.FindAsync(Context.User!.UserId()))!;
        await Clients.Group(Group(sessionId)).SendAsync("chat", new LiveChatDto(user.ToSummary(), text, DateTime.UtcNow));
    }

    public async Task React(Guid sessionId, string emoji)
    {
        if (!rooms.Contains(sessionId, Context.ConnectionId) || emoji is not ("❤️" or "👍" or "✨" or "🤲" or "🌙" or "😂")) return;
        await Clients.Group(Group(sessionId)).SendAsync("reaction", emoji);
    }

    private async Task BroadcastCountAsync(LiveSession session)
    {
        var count = rooms.Count(session.Id);
        if (count > session.PeakViewers)
        {
            session.PeakViewers = count;
            await db.SaveChangesAsync();
        }
        await Clients.Group(Group(session.Id)).SendAsync("viewers", count);
    }
}

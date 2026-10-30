using System.Text.Json.Serialization;
using HalalBook.Shared;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace HalalBook.Web.Services;

/// <summary>Live connection to the API hub for chat messages, notifications and unread badges.</summary>
public sealed class Realtime(AuthSession session, ApiClient api, ILogger<Realtime> log) : IAsyncDisposable
{
    private HubConnection? _hub;

    public int UnreadNotifications { get; private set; }
    public int UnreadMessages { get; private set; }

    /// <summary>People currently online whose status this user may see (friends first).</summary>
    public List<OnlineUserDto> OnlineUsers { get; private set; } = [];
    private readonly HashSet<Guid> _online = [];
    public bool IsOnline(Guid userId) => _online.Contains(userId);
    public event Action? PresenceChanged;

    public event Action<MessageDto>? MessageReceived;
    public event Action<NotificationDto>? NotificationReceived;
    public event Action<Guid, Guid>? Typing;
    public event Action? CountsChanged;
    public event Action<CallDto>? IncomingCall;
    public event Action<Guid, string, Guid>? CallUpdated;

    public async Task StartAsync()
    {
        if (_hub is not null || !session.IsAuthenticated) return;
        await RefreshCountsAsync();
        await RefreshOnlineAsync();
        _hub = new HubConnectionBuilder()
            .WithUrl(new Uri(api.BaseAddress, "hubs/realtime"), o => o.AccessTokenProvider = () => Task.FromResult(session.Token))
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .WithAutomaticReconnect()
            .Build();

        _hub.On<MessageDto>("message", m =>
        {
            if (m.Sender.Id != session.User?.Id) { UnreadMessages++; CountsChanged?.Invoke(); }
            MessageReceived?.Invoke(m);
        });
        _hub.On<NotificationDto>("notification", n =>
        {
            UnreadNotifications++;
            CountsChanged?.Invoke();
            NotificationReceived?.Invoke(n);
        });
        _hub.On<Guid, Guid>("typing", (c, u) => Typing?.Invoke(c, u));
        _hub.On<CallDto>("incomingCall", c => IncomingCall?.Invoke(c));
        _hub.On<Guid, string, Guid>("callUpdated", (id, status, by) => CallUpdated?.Invoke(id, status, by));
        _hub.On<Guid, bool>("presence", async (userId, online) =>
        {
            if (online && !_online.Contains(userId)) await RefreshOnlineAsync(); // fetch name/avatar, respecting privacy
            else if (!online && _online.Remove(userId))
            {
                OnlineUsers = OnlineUsers.Where(o => o.User.Id != userId).ToList();
                PresenceChanged?.Invoke();
            }
        });
        _hub.Reconnected += async _ => await RefreshOnlineAsync();

        try { await _hub.StartAsync(); }
        catch (Exception ex) { log.LogWarning(ex, "Realtime connection failed; live updates are off."); }
    }

    public async Task RefreshCountsAsync()
    {
        try
        {
            UnreadNotifications = await api.GetAsync<int>("api/notifications/unread-count");
            UnreadMessages = await api.GetAsync<int>("api/conversations/unread-count");
            CountsChanged?.Invoke();
        }
        catch (Exception ex) { log.LogDebug(ex, "Could not refresh counts"); }
    }

    public async Task RefreshOnlineAsync()
    {
        try
        {
            OnlineUsers = await api.GetAsync<List<OnlineUserDto>>("api/presence/online");
            _online.Clear();
            foreach (var o in OnlineUsers) _online.Add(o.User.Id);
            PresenceChanged?.Invoke();
        }
        catch (Exception ex) { log.LogDebug(ex, "Could not refresh online users"); }
    }

    public void ClearNotifications() { UnreadNotifications = 0; CountsChanged?.Invoke(); }

    public Task SendTypingAsync(Guid conversationId) =>
        _hub?.State == HubConnectionState.Connected ? _hub.InvokeAsync("Typing", conversationId) : Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null) await _hub.DisposeAsync();
    }
}

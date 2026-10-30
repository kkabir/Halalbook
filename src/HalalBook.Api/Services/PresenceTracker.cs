using System.Collections.Concurrent;

namespace HalalBook.Api.Services;

/// <summary>
/// Who is connected right now, counted per user so several tabs or devices count once.
/// In memory: fine for a single server; use a SignalR backplane (e.g. Redis) when scaling out.
/// </summary>
public class PresenceTracker
{
    private readonly ConcurrentDictionary<Guid, int> _connections = new();

    /// <summary>Returns true when this is the user's first connection (they just came online).</summary>
    public bool Connect(Guid userId) => _connections.AddOrUpdate(userId, 1, (_, n) => n + 1) == 1;

    /// <summary>Returns true when the user's last connection closed (they just went offline).</summary>
    public bool Disconnect(Guid userId)
    {
        while (_connections.TryGetValue(userId, out var n))
        {
            if (n <= 1)
            {
                if (_connections.TryRemove(new KeyValuePair<Guid, int>(userId, n))) return true;
            }
            else if (_connections.TryUpdate(userId, n - 1, n)) return false;
        }
        return false;
    }

    public bool IsOnline(Guid userId) => _connections.ContainsKey(userId);

    public IReadOnlyCollection<Guid> OnlineUserIds => _connections.Keys.ToList();
}

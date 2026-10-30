using HalalBook.Api.Data;
using HalalBook.Shared;
using Microsoft.EntityFrameworkCore;

namespace HalalBook.Api.Services;

/// <summary>Applies Facebook-style privacy audiences (everyone, friends of friends, friends, only me).</summary>
public class PrivacyService(AppDbContext db, PostService posts)
{
    public enum Relation { Self, Friend, FriendOfFriend, None }

    public async Task<Relation> RelationAsync(Guid me, Guid other)
    {
        if (me == other) return Relation.Self;
        var myFriends = await posts.FriendIdsAsync(me);
        if (myFriends.Contains(other)) return Relation.Friend;
        var theirFriends = await posts.FriendIdsAsync(other);
        return myFriends.Intersect(theirFriends).Any() ? Relation.FriendOfFriend : Relation.None;
    }

    public static bool Allows(PrivacyAudience audience, Relation r) => r == Relation.Self || audience switch
    {
        PrivacyAudience.Everyone => true,
        PrivacyAudience.FriendsOfFriends => r is Relation.Friend or Relation.FriendOfFriend,
        PrivacyAudience.Friends => r == Relation.Friend,
        _ => false,
    };

    public static string Describe(PrivacyAudience a) => a switch
    {
        PrivacyAudience.FriendsOfFriends => "friends of friends",
        PrivacyAudience.Friends => "friends",
        PrivacyAudience.OnlyMe => "no one",
        _ => "everyone",
    };

    public async Task<int> MutualFriendsAsync(Guid me, Guid other)
    {
        if (me == other) return 0;
        var mine = await posts.FriendIdsAsync(me);
        var theirs = await posts.FriendIdsAsync(other);
        return mine.Intersect(theirs).Count();
    }

    /// <summary>Ids of people two steps away (friends of my friends who aren't my friends).</summary>
    public async Task<HashSet<Guid>> FriendsOfFriendsAsync(Guid me)
    {
        var friends = await posts.FriendIdsAsync(me);
        var fof = await db.Friendships.Where(f => f.Accepted && (friends.Contains(f.RequesterId) || friends.Contains(f.AddresseeId)))
            .Select(f => friends.Contains(f.RequesterId) ? f.AddresseeId : f.RequesterId).ToListAsync();
        return fof.Where(id => id != me && !friends.Contains(id)).ToHashSet();
    }
}

using System.Text.RegularExpressions;
using HalalBook.Api.Data;
using HalalBook.Shared;
using Microsoft.EntityFrameworkCore;

namespace HalalBook.Api.Services;

/// <summary>Visibility rules, feed ranking and DTO building for posts.</summary>
public partial class PostService(AppDbContext db)
{
    public async Task<List<Guid>> FriendIdsAsync(Guid me) =>
        await db.Friendships.Where(f => f.Accepted && (f.RequesterId == me || f.AddresseeId == me))
            .Select(f => f.RequesterId == me ? f.AddresseeId : f.RequesterId).ToListAsync();

    public async Task<List<Guid>> BlockedIdsAsync(Guid me) =>
        await db.Blocks.Where(b => b.BlockerId == me || b.BlockedId == me)
            .Select(b => b.BlockerId == me ? b.BlockedId : b.BlockerId).ToListAsync();

    /// <summary>Posts that <paramref name="me"/> is allowed to see.</summary>
    public async Task<IQueryable<Post>> VisibleToAsync(Guid me, bool isModerator = false)
    {
        var friends = await FriendIdsAsync(me);
        var blocked = await BlockedIdsAsync(me);
        var myGroups = await db.GroupMembers.Where(m => m.UserId == me && m.IsApproved).Select(m => m.GroupId).ToListAsync();
        var privateGroups = await db.Groups.Where(g => g.Privacy == GroupPrivacy.Private).Select(g => g.Id).ToListAsync();

        return db.Posts.Where(p => !p.IsDeleted
            && !blocked.Contains(p.AuthorId)
            && (p.Moderation == ModerationStatus.Approved || p.AuthorId == me || (isModerator && p.Moderation != ModerationStatus.Removed))
            && (p.AuthorId == me || p.Visibility == Visibility.Public || (p.Visibility == Visibility.Friends && friends.Contains(p.AuthorId)))
            && (p.GroupId == null || !privateGroups.Contains(p.GroupId.Value) || myGroups.Contains(p.GroupId.Value)));
    }

    /// <summary>
    /// Smart feed: candidates from people, pages and groups you're connected to plus trending public posts,
    /// ranked by affinity, engagement and freshness.
    /// </summary>
    public async Task<PagedResult<PostDto>> FeedAsync(Guid me, int page, int pageSize, bool isModerator)
    {
        var visible = await VisibleToAsync(me, isModerator);
        var friends = await FriendIdsAsync(me);
        var following = await db.Follows.Where(f => f.FollowerId == me).Select(f => f.FolloweeId).ToListAsync();
        var pages = await db.PageFollowers.Where(f => f.UserId == me).Select(f => f.PageId).ToListAsync();
        var groups = await db.GroupMembers.Where(m => m.UserId == me && m.IsApproved).Select(m => m.GroupId).ToListAsync();
        var since = DateTime.UtcNow.AddDays(-30);

        var candidates = await visible
            .Where(p => p.Kind == PostKind.Standard && p.CreatedAt > since)
            .Select(p => new
            {
                p.Id, p.AuthorId, p.CreatedAt, p.GroupId, p.PageId,
                Reactions = db.Reactions.Count(r => r.PostId == p.Id),
                Comments = db.Comments.Count(c => c.PostId == p.Id && !c.IsDeleted),
            })
            .OrderByDescending(p => p.CreatedAt).Take(500).ToListAsync();

        var now = DateTime.UtcNow;
        var ranked = candidates.Select(c =>
        {
            double affinity = c.AuthorId == me ? 3
                : friends.Contains(c.AuthorId) ? 4
                : following.Contains(c.AuthorId) ? 2.5
                : c.PageId is Guid pid && pages.Contains(pid) ? 2
                : c.GroupId is Guid gid && groups.Contains(gid) ? 2.5
                : 0.5;
            double engagement = Math.Log(1 + c.Reactions + 2 * c.Comments);
            double hours = (now - c.CreatedAt).TotalHours;
            double freshness = 1 / Math.Pow(hours + 2, 1.2);
            return (c.Id, Score: (affinity + engagement) * freshness);
        }).OrderByDescending(x => x.Score).ToList();

        var pageIds = ranked.Skip((page - 1) * pageSize).Take(pageSize).Select(x => x.Id).ToList();
        var dtos = await BuildAsync(db.Posts.Where(p => pageIds.Contains(p.Id)), me);
        var ordered = pageIds.Select(id => dtos.FirstOrDefault(d => d.Id == id)).OfType<PostDto>().ToList();
        return new PagedResult<PostDto>(ordered, page, pageSize, ranked.Count > page * pageSize);
    }

    public async Task<PagedResult<PostDto>> PageAsync(IQueryable<Post> query, Guid me, int page, int pageSize)
    {
        var ids = await query.OrderByDescending(p => p.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize + 1).Select(p => p.Id).ToListAsync();
        var hasMore = ids.Count > pageSize;
        ids = ids.Take(pageSize).ToList();
        var dtos = await BuildAsync(db.Posts.Where(p => ids.Contains(p.Id)), me);
        return new PagedResult<PostDto>(ids.Select(id => dtos.First(d => d.Id == id)).ToList(), page, pageSize, hasMore);
    }

    public async Task<List<PostDto>> BuildAsync(IQueryable<Post> query, Guid me)
    {
        var posts = await query
            .Include(p => p.Author).Include(p => p.Media).Include(p => p.Hashtags).ThenInclude(h => h.Hashtag)
            .Include(p => p.Group).Include(p => p.Page)
            .Include(p => p.SharedPost).ThenInclude(s => s!.Author)
            .Include(p => p.SharedPost).ThenInclude(s => s!.Media)
            .AsSplitQuery().AsNoTracking().ToListAsync();
        if (posts.Count == 0) return [];

        var ids = posts.Select(p => p.Id).ToList();
        var reactions = await db.Reactions.Where(r => r.PostId != null && ids.Contains(r.PostId.Value))
            .GroupBy(r => new { r.PostId, r.Type }).Select(g => new { g.Key.PostId, g.Key.Type, Count = g.Count() }).ToListAsync();
        var mine = await db.Reactions.Where(r => r.UserId == me && r.PostId != null && ids.Contains(r.PostId.Value))
            .Select(r => new { r.PostId, r.Type }).ToListAsync();
        var comments = await db.Comments.Where(c => ids.Contains(c.PostId) && !c.IsDeleted)
            .GroupBy(c => c.PostId).Select(g => new { PostId = g.Key, Count = g.Count() }).ToListAsync();
        var shares = await db.Posts.Where(p => p.SharedPostId != null && ids.Contains(p.SharedPostId.Value) && !p.IsDeleted)
            .GroupBy(p => p.SharedPostId).Select(g => new { PostId = g.Key, Count = g.Count() }).ToListAsync();
        var bookmarks = await db.Bookmarks.Where(b => b.UserId == me && ids.Contains(b.PostId)).Select(b => b.PostId).ToListAsync();

        return posts.Select(p => new PostDto(
            p.Id, p.Author.ToSummary(), p.Content, p.Visibility, p.Kind, p.CreatedAt, p.EditedAt,
            p.Media.OrderBy(m => m.SortOrder).Select(m => new MediaDto(m.Url, m.Type)).ToList(),
            p.Hashtags.Select(h => h.Hashtag.Tag).ToList(),
            comments.FirstOrDefault(c => c.PostId == p.Id)?.Count ?? 0,
            shares.FirstOrDefault(s => s.PostId == p.Id)?.Count ?? 0,
            reactions.Where(r => r.PostId == p.Id).ToDictionary(r => r.Type, r => r.Count),
            mine.FirstOrDefault(r => r.PostId == p.Id)?.Type,
            bookmarks.Contains(p.Id), p.Moderation, p.Feeling, p.Location,
            p.Group?.ToSummary(), p.Page?.ToSummary(),
            p.SharedPost is { IsDeleted: false } s
                ? new PostDto(s.Id, s.Author.ToSummary(), s.Content, s.Visibility, s.Kind, s.CreatedAt, s.EditedAt,
                    s.Media.OrderBy(m => m.SortOrder).Select(m => new MediaDto(m.Url, m.Type)).ToList(), [], 0, 0, [], null, false,
                    s.Moderation, s.Feeling, s.Location, null, null, null)
                : null)).ToList();
    }

    /// <summary>Replaces a post's hashtags with the ones found in its text.</summary>
    public async Task SyncHashtagsAsync(Post post)
    {
        var tags = ExtractHashtags(post.Content);
        post.Hashtags.Clear();
        foreach (var tag in tags)
        {
            var h = await db.Hashtags.FirstOrDefaultAsync(x => x.Tag == tag)
                    ?? db.Hashtags.Local.FirstOrDefault(x => x.Tag == tag)
                    ?? db.Hashtags.Add(new Hashtag { Tag = tag }).Entity;
            post.Hashtags.Add(new PostHashtag { PostId = post.Id, HashtagId = h.Id, Hashtag = h });
        }
    }

    public static List<string> ExtractHashtags(string text) =>
        HashtagRegex().Matches(text ?? "").Select(m => m.Groups[1].Value.ToLowerInvariant()).Distinct().Take(30).ToList();

    public static List<string> ExtractMentions(string text) =>
        MentionRegex().Matches(text ?? "").Select(m => m.Groups[1].Value).Distinct().Take(20).ToList();

    [GeneratedRegex(@"(?<!\w)#(\w{2,50})")] private static partial Regex HashtagRegex();
    [GeneratedRegex(@"(?<!\w)@([A-Za-z0-9_\.]{3,32})")] private static partial Regex MentionRegex();
}

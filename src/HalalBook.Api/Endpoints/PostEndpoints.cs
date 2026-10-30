using System.Security.Claims;
using HalalBook.Api.Data;
using HalalBook.Api.Services;
using HalalBook.Shared;
using Microsoft.EntityFrameworkCore;

namespace HalalBook.Api.Endpoints;

public static class PostEndpoints
{
    public static void MapPostEndpoints(this IEndpointRouteBuilder app)
    {
        var posts = app.MapGroup("/api/posts").WithTags("Posts").RequireAuthorization();

        app.MapGet("/api/feed", async (int? page, ClaimsPrincipal cp, PostService svc) =>
            await svc.FeedAsync(cp.UserId(), Math.Max(1, page ?? 1), 10, cp.IsModerator()))
            .WithTags("Posts").RequireAuthorization();

        // Short vertical videos.
        app.MapGet("/api/reels", async (int? page, ClaimsPrincipal cp, PostService svc) =>
        {
            var visible = await svc.VisibleToAsync(cp.UserId());
            return await svc.PageAsync(visible.Where(p => p.Kind == PostKind.Reel), cp.UserId(), Math.Max(1, page ?? 1), 5);
        }).WithTags("Posts").RequireAuthorization();

        posts.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal cp, PostService svc) =>
        {
            var visible = await svc.VisibleToAsync(cp.UserId(), cp.IsModerator());
            var list = await svc.BuildAsync(visible.Where(p => p.Id == id), cp.UserId());
            return list.Count == 1 ? Results.Ok(list[0]) : Results.NotFound();
        });

        posts.MapPost("/", async (CreatePostRequest req, ClaimsPrincipal cp, AppDbContext db, PostService svc, AiService ai, Notifier notifier, IWebHostEnvironment env, CancellationToken ct) =>
        {
            var me = cp.UserId();
            var content = (req.Content ?? "").Trim();
            if (content.Length == 0 && (req.Media?.Count ?? 0) == 0 && req.SharedPostId is null)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["content"] = ["Write something or add a photo or video."] });
            if (content.Length > 10000) return Results.ValidationProblem(new Dictionary<string, string[]> { ["content"] = ["Posts are limited to 10,000 characters."] });

            // Destinations: one or more groups, optionally also your profile (like Facebook's "Post to").
            var groupIds = (req.GroupIds ?? []).Concat(req.GroupId is Guid g0 ? [g0] : []).Distinct().Take(20).ToList();
            foreach (var gid in groupIds)
                if (!await db.GroupMembers.AnyAsync(m => m.GroupId == gid && m.UserId == me && m.IsApproved))
                    return Results.Problem("Join the group to post in it.", statusCode: 403);
            var onProfile = groupIds.Count == 0 || req.AlsoOnProfile;
            if (groupIds.Count > 0 && (req.PageId is not null || req.TargetUserId is not null)) return Results.BadRequest();

            // Custom audiences can only name your friends.
            var audience = new List<Guid>();
            if (req.Visibility is Visibility.SpecificPeople or Visibility.FriendsExcept)
            {
                var friendIds = await svc.FriendIdsAsync(me);
                audience = (req.AudienceUserIds ?? []).Distinct().Where(friendIds.Contains).Take(500).ToList();
                if (req.Visibility == Visibility.SpecificPeople && audience.Count == 0)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["audience"] = ["Choose at least one friend to share with."] });
            }
            Guid? target = req.TargetUserId == me ? null : req.TargetUserId;
            if (target is Guid tid)
            {
                if (req.GroupId is not null || req.PageId is not null) return Results.BadRequest();
                if (await UserEndpoints.CanPostOnProfileAsync(db, me, tid) is { } why) return Results.Problem(why, statusCode: 403);
            }
            if (req.PageId is Guid pid && !await db.Pages.AnyAsync(p => p.Id == pid && p.OwnerId == me))
                return Results.Problem("Only the page owner can post as the page.", statusCode: 403);
            if (req.Kind == PostKind.Reel && req.Media?.FirstOrDefault()?.Type != MediaType.Video)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["media"] = ["A reel needs a video."] });

            var moderation = await ai.ModerateAsync(content, ct);
            if (!moderation.Allowed) return Results.Problem(string.Join(" ", moderation.Reasons), statusCode: 422, title: "Content not allowed");
            if ((req.Media ?? []).Any(m => MediaNeedsReview(env, m.Url)))
                moderation = moderation with { NeedsReview = true, Reasons = [.. moderation.Reasons, "A photo was flagged for review."] };

            Post NewPost(Guid? groupId) => new()
            {
                AuthorId = me, Content = content, Kind = req.Kind,
                // Group posts follow the group's privacy; custom audiences apply to your profile.
                Visibility = groupId is null ? req.Visibility : Visibility.Public,
                GroupId = groupId, PageId = groupId is null ? req.PageId : null, SharedPostId = req.SharedPostId,
                TargetUserId = groupId is null ? target : null,
                Feeling = req.Feeling, Location = req.Location,
                Moderation = moderation.NeedsReview ? ModerationStatus.PendingReview : ModerationStatus.Approved,
                ModerationNote = moderation.NeedsReview ? string.Join(" ", moderation.Reasons) : null,
                Media = (req.Media ?? []).Take(10).Select((m, i) => new PostMedia
                {
                    Url = m.Url, Type = m.Type, SortOrder = i,
                    AltText = m.AltText is { Length: > 0 } a ? a[..Math.Min(a.Length, 300)] : null,
                }).ToList(),
            };
            var created = new List<Post>();
            if (onProfile) created.Add(NewPost(null));
            created.AddRange(groupIds.Select(g => NewPost(g)));
            foreach (var p in created)
            {
                db.Posts.Add(p);
                await svc.SyncHashtagsAsync(p);
            }
            var post = created[0];
            if (post.GroupId is null && audience.Count > 0)
                db.PostAudiences.AddRange(audience.Select(uid => new PostAudience { PostId = post.Id, UserId = uid }));
            await db.SaveChangesAsync();

            var meUser = (await db.Users.FindAsync(me))!;
            foreach (var name in PostService.ExtractMentions(content))
            {
                var key = name.ToUpperInvariant();
                if (await db.Users.FirstOrDefaultAsync(u => u.NormalizedUserName == key) is { } mentioned)
                    await notifier.SendAsync(mentioned.Id, me, NotificationType.Mention, $"{meUser.DisplayName} mentioned you in a post.", $"/post/{post.Id}");
            }
            if (target is Guid wallOwner)
                await notifier.SendAsync(wallOwner, me, NotificationType.Mention, $"{meUser.DisplayName} posted on your profile.", $"/post/{post.Id}");
            if (req.SharedPostId is Guid sid && await db.Posts.FindAsync(sid) is { } original)
                await notifier.SendAsync(original.AuthorId, me, NotificationType.Share, $"{meUser.DisplayName} shared your post.", $"/post/{post.Id}");

            var dto = (await svc.BuildAsync(db.Posts.Where(p => p.Id == post.Id), me))[0];
            return Results.Created($"/api/posts/{post.Id}", dto);
        });

        posts.MapPut("/{id:guid}", async (Guid id, UpdatePostRequest req, ClaimsPrincipal cp, AppDbContext db, PostService svc, AiService ai, CancellationToken ct) =>
        {
            var post = await db.Posts.Include(p => p.Hashtags).FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);
            if (post is null) return Results.NotFound();
            if (post.AuthorId != cp.UserId()) return Results.Forbid();
            var moderation = await ai.ModerateAsync(req.Content, ct);
            if (!moderation.Allowed) return Results.Problem(string.Join(" ", moderation.Reasons), statusCode: 422, title: "Content not allowed");
            post.Content = req.Content.Trim();
            if (req.Visibility is Visibility.SpecificPeople or Visibility.FriendsExcept && req.AudienceUserIds is not null)
            {
                var friendIds = await svc.FriendIdsAsync(cp.UserId());
                await db.PostAudiences.Where(a => a.PostId == id).ExecuteDeleteAsync(ct);
                db.PostAudiences.AddRange(req.AudienceUserIds.Distinct().Where(friendIds.Contains).Select(uid => new PostAudience { PostId = id, UserId = uid }));
            }
            post.Visibility = post.GroupId is null ? req.Visibility : post.Visibility;
            post.EditedAt = DateTime.UtcNow;
            if (moderation.NeedsReview) post.Moderation = ModerationStatus.PendingReview;
            await svc.SyncHashtagsAsync(post);
            await db.SaveChangesAsync();
            return Results.Ok((await svc.BuildAsync(db.Posts.Where(p => p.Id == id), cp.UserId()))[0]);
        });

        posts.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal cp, AppDbContext db) =>
        {
            var post = await db.Posts.FindAsync(id);
            if (post is null) return Results.NotFound();
            // Authors, the owner of the profile it was posted on, and moderators can remove a post.
            if (post.AuthorId != cp.UserId() && post.TargetUserId != cp.UserId() && !cp.IsModerator()) return Results.Forbid();
            post.IsDeleted = true;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // ---- Reactions ----
        posts.MapPut("/{id:guid}/reaction", async (Guid id, ReactRequest req, ClaimsPrincipal cp, AppDbContext db, Notifier notifier) =>
        {
            var me = cp.UserId();
            var post = await db.Posts.FindAsync(id);
            if (post is null || post.IsDeleted) return Results.NotFound();
            var existing = await db.Reactions.FirstOrDefaultAsync(r => r.PostId == id && r.UserId == me);
            if (existing is null)
            {
                db.Reactions.Add(new Reaction { PostId = id, UserId = me, Type = req.Type });
                await db.SaveChangesAsync();
                var meUser = (await db.Users.FindAsync(me))!;
                await notifier.SendAsync(post.AuthorId, me, NotificationType.Reaction, $"{meUser.DisplayName} reacted {ReactionLabel(req.Type)} to your post.", $"/post/{id}");
            }
            else
            {
                existing.Type = req.Type;
                await db.SaveChangesAsync();
            }
            return Results.Ok();
        });

        posts.MapDelete("/{id:guid}/reaction", async (Guid id, ClaimsPrincipal cp, AppDbContext db) =>
        {
            await db.Reactions.Where(r => r.PostId == id && r.UserId == cp.UserId()).ExecuteDeleteAsync();
            return Results.NoContent();
        });

        posts.MapGet("/{id:guid}/reactions", async (Guid id, AppDbContext db) =>
            await db.Reactions.Where(r => r.PostId == id)
                .Join(db.Users, r => r.UserId, u => u.Id, (r, u) => new { r.Type, User = u })
                .Select(x => new { x.Type, User = x.User.ToSummary() }).Take(200).ToListAsync());

        // ---- Comments ----
        posts.MapGet("/{id:guid}/comments", async (Guid id, ClaimsPrincipal cp, AppDbContext db) =>
        {
            var me = cp.UserId();
            var comments = await db.Comments.Include(c => c.Author)
                .Where(c => c.PostId == id && !c.IsDeleted && (c.Moderation == ModerationStatus.Approved || c.AuthorId == me))
                .OrderBy(c => c.CreatedAt).Take(500).ToListAsync();
            var ids = comments.Select(c => c.Id).ToList();
            var likes = await db.Reactions.Where(r => r.CommentId != null && ids.Contains(r.CommentId.Value))
                .Select(r => new { r.CommentId, r.UserId }).ToListAsync();

            CommentDto Map(Comment c) => new(c.Id, c.PostId, c.ParentId, c.Author.ToSummary(), c.Content, c.CreatedAt,
                likes.Count(l => l.CommentId == c.Id), likes.Any(l => l.CommentId == c.Id && l.UserId == me),
                comments.Where(r => r.ParentId == c.Id).Select(Map).ToList(), c.MediaUrl);

            return comments.Where(c => c.ParentId == null).Select(Map).ToList();
        });

        posts.MapPost("/{id:guid}/comments", async (Guid id, CreateCommentRequest req, ClaimsPrincipal cp, AppDbContext db, AiService ai, Notifier notifier, IWebHostEnvironment env, CancellationToken ct) =>
        {
            var me = cp.UserId();
            var post = await db.Posts.FindAsync(id);
            if (post is null || post.IsDeleted) return Results.NotFound();
            var content = (req.Content ?? "").Trim();
            var media = IsUploadedImage(req.MediaUrl) ? req.MediaUrl : null;
            if ((content.Length == 0 && media is null) || content.Length > 3000)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["content"] = ["Write a comment (up to 3000 characters) or add a photo."] });
            var moderation = await ai.ModerateAsync(content, ct);
            if (!moderation.Allowed) return Results.Problem(string.Join(" ", moderation.Reasons), statusCode: 422, title: "Content not allowed");

            var comment = new Comment
            {
                PostId = id, AuthorId = me, ParentId = req.ParentId, Content = content, MediaUrl = media,
                Moderation = moderation.NeedsReview || (media is not null && MediaNeedsReview(env, media)) ? ModerationStatus.PendingReview : ModerationStatus.Approved,
            };
            db.Comments.Add(comment);
            await db.SaveChangesAsync();
            var meUser = (await db.Users.FindAsync(me))!;
            await notifier.SendAsync(post.AuthorId, me, NotificationType.Comment, $"{meUser.DisplayName} commented: \"{Short(content)}\"", $"/post/{id}");
            if (req.ParentId is Guid pid && await db.Comments.FindAsync(pid) is { } parent && parent.AuthorId != post.AuthorId)
                await notifier.SendAsync(parent.AuthorId, me, NotificationType.Comment, $"{meUser.DisplayName} replied to your comment.", $"/post/{id}");
            return Results.Ok(new CommentDto(comment.Id, id, comment.ParentId, meUser.ToSummary(), content, comment.CreatedAt, 0, false, [], media));
        });

        app.MapDelete("/api/comments/{id:guid}", async (Guid id, ClaimsPrincipal cp, AppDbContext db) =>
        {
            var c = await db.Comments.Include(x => x.Post).FirstOrDefaultAsync(x => x.Id == id);
            if (c is null) return Results.NotFound();
            if (c.AuthorId != cp.UserId() && c.Post.AuthorId != cp.UserId() && !cp.IsModerator()) return Results.Forbid();
            c.IsDeleted = true;
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).WithTags("Posts").RequireAuthorization();

        app.MapPost("/api/comments/{id:guid}/like", async (Guid id, ClaimsPrincipal cp, AppDbContext db) =>
        {
            var me = cp.UserId();
            var existing = await db.Reactions.FirstOrDefaultAsync(r => r.CommentId == id && r.UserId == me);
            if (existing is null) db.Reactions.Add(new Reaction { CommentId = id, UserId = me, Type = ReactionType.Like });
            else db.Reactions.Remove(existing);
            await db.SaveChangesAsync();
            return Results.Ok(existing is null);
        }).WithTags("Posts").RequireAuthorization();

        // ---- AI: summarize a long discussion ----
        posts.MapGet("/{id:guid}/summary", async (Guid id, AppDbContext db, AiService ai, CancellationToken ct) =>
        {
            var post = await db.Posts.FindAsync(id);
            if (post is null) return Results.NotFound();
            var lines = await db.Comments.Where(c => c.PostId == id && !c.IsDeleted && c.Moderation == ModerationStatus.Approved)
                .OrderBy(c => c.CreatedAt).Select(c => c.Content).ToListAsync(ct);
            return Results.Ok(await ai.SummarizeAsync(Short(post.Content, 80), lines, ct));
        });

        // ---- Bookmarks ----
        posts.MapPost("/{id:guid}/bookmark", async (Guid id, ClaimsPrincipal cp, AppDbContext db) =>
        {
            var me = cp.UserId();
            if (!await db.Bookmarks.AnyAsync(b => b.UserId == me && b.PostId == id))
            {
                db.Bookmarks.Add(new Bookmark { UserId = me, PostId = id });
                await db.SaveChangesAsync();
            }
            return Results.Ok();
        });

        posts.MapDelete("/{id:guid}/bookmark", async (Guid id, ClaimsPrincipal cp, AppDbContext db) =>
        {
            await db.Bookmarks.Where(b => b.UserId == cp.UserId() && b.PostId == id).ExecuteDeleteAsync();
            return Results.NoContent();
        });

        app.MapGet("/api/bookmarks", async (int? page, ClaimsPrincipal cp, AppDbContext db, PostService svc) =>
        {
            var me = cp.UserId();
            var saved = db.Bookmarks.Where(b => b.UserId == me).Select(b => b.PostId);
            var visible = await svc.VisibleToAsync(me);
            return await svc.PageAsync(visible.Where(p => saved.Contains(p.Id)), me, Math.Max(1, page ?? 1), 15);
        }).WithTags("Posts").RequireAuthorization();

        // ---- Hashtags ----
        app.MapGet("/api/hashtags/trending", async (AppDbContext db) =>
        {
            var since = DateTime.UtcNow.AddDays(-7);
            var counts = await (from ph in db.PostHashtags
                                join p in db.Posts on ph.PostId equals p.Id
                                join h in db.Hashtags on ph.HashtagId equals h.Id
                                where p.CreatedAt > since && !p.IsDeleted && p.Moderation == ModerationStatus.Approved
                                group h by h.Tag into g
                                select new { Tag = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count).Take(10).ToListAsync();
            return counts.Select(c => new HashtagDto(c.Tag, c.Count)).ToList();
        }).WithTags("Posts").RequireAuthorization();

        app.MapGet("/api/hashtags/{tag}", async (string tag, int? page, ClaimsPrincipal cp, PostService svc) =>
        {
            var t = tag.TrimStart('#').ToLowerInvariant();
            var visible = await svc.VisibleToAsync(cp.UserId());
            return await svc.PageAsync(visible.Where(p => p.Hashtags.Any(h => h.Hashtag.Tag == t)), cp.UserId(), Math.Max(1, page ?? 1), 15);
        }).WithTags("Posts").RequireAuthorization();

        // ---- Stories (24h) ----
        var stories = app.MapGroup("/api/stories").WithTags("Stories").RequireAuthorization();

        stories.MapGet("/", async (ClaimsPrincipal cp, AppDbContext db, PostService svc) =>
        {
            var me = cp.UserId();
            var friends = await svc.FriendIdsAsync(me);
            var following = await db.Follows.Where(f => f.FollowerId == me).Select(f => f.FolloweeId).ToListAsync();
            var authors = friends.Concat(following).Append(me).Distinct().ToList();
            var now = DateTime.UtcNow;
            var list = await db.Stories.Include(s => s.Author)
                .Where(s => s.ExpiresAt > now && authors.Contains(s.AuthorId)).OrderBy(s => s.CreatedAt).ToListAsync();
            var ids = list.Select(s => s.Id).ToList();
            var views = await db.StoryViews.Where(v => ids.Contains(v.StoryId)).Select(v => new { v.StoryId, v.ViewerId }).ToListAsync();
            return list.GroupBy(s => s.AuthorId).Select(g =>
            {
                var items = g.Select(s => new StoryDto(s.Id, s.MediaUrl, s.MediaType, s.Text, s.Background, s.CreatedAt, s.ExpiresAt,
                    views.Count(v => v.StoryId == s.Id), views.Any(v => v.StoryId == s.Id && v.ViewerId == me))).ToList();
                return new StoryReelDto(g.First().Author.ToSummary(), items, items.All(i => i.Seen));
            })
            .OrderByDescending(r => r.Author.Id == me).ThenBy(r => r.AllSeen).ThenByDescending(r => r.Stories.Max(s => s.CreatedAt)).ToList();
        });

        stories.MapPost("/", async (CreateStoryRequest req, ClaimsPrincipal cp, AppDbContext db, AiService ai, IWebHostEnvironment env, CancellationToken ct) =>
        {
            if (req.MediaUrl is { } storyMedia && MediaNeedsReview(env, storyMedia))
                return Results.Problem("This photo or video can't be used in a story.", statusCode: 422, title: "Content not allowed");
            if (string.IsNullOrWhiteSpace(req.MediaUrl) && string.IsNullOrWhiteSpace(req.Text))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["text"] = ["Add a photo, video or text."] });
            var moderation = await ai.ModerateAsync(req.Text ?? "", ct);
            if (!moderation.Allowed || moderation.NeedsReview) return Results.Problem(string.Join(" ", moderation.Reasons), statusCode: 422, title: "Content not allowed");
            var s = new Story { AuthorId = cp.UserId(), MediaUrl = req.MediaUrl, MediaType = req.MediaType, Text = req.Text, Background = req.Background };
            db.Stories.Add(s);
            await db.SaveChangesAsync();
            return Results.Ok(new StoryDto(s.Id, s.MediaUrl, s.MediaType, s.Text, s.Background, s.CreatedAt, s.ExpiresAt, 0, true));
        });

        stories.MapPost("/{id:guid}/view", async (Guid id, ClaimsPrincipal cp, AppDbContext db) =>
        {
            var me = cp.UserId();
            if (!await db.StoryViews.AnyAsync(v => v.StoryId == id && v.ViewerId == me))
            {
                db.StoryViews.Add(new StoryView { StoryId = id, ViewerId = me });
                await db.SaveChangesAsync();
            }
            return Results.Ok();
        });

        stories.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal cp, AppDbContext db) =>
        {
            var n = await db.Stories.Where(s => s.Id == id && s.AuthorId == cp.UserId()).ExecuteDeleteAsync();
            return n == 0 ? Results.NotFound() : Results.NoContent();
        });
    }

    /// <summary>True when AI vision marked this uploaded photo as borderline (a ".review" file sits next to it).</summary>
    private static bool MediaNeedsReview(IWebHostEnvironment env, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || !u.AbsolutePath.StartsWith("/uploads/")) return false;
        var root = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
        var path = Path.GetFullPath(Path.Combine(root, u.AbsolutePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
        return path.StartsWith(Path.GetFullPath(root)) && File.Exists(path + ".review");
    }

    /// <summary>Comment photos must be images uploaded to HalalBook, not arbitrary links.</summary>
    private static bool IsUploadedImage(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "https" or "http" && u.AbsolutePath.StartsWith("/uploads/")
        && Path.GetExtension(u.AbsolutePath).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp";

    private static string Short(string s, int n = 60) => s.Length <= n ? s : s[..n] + "…";

    private static string ReactionLabel(ReactionType t) => t switch
    {
        ReactionType.MashaAllah => "MashaAllah",
        ReactionType.SubhanAllah => "SubhanAllah",
        ReactionType.Ameen => "Ameen",
        _ => t.ToString().ToLowerInvariant(),
    };
}

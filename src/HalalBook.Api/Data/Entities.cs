using HalalBook.Shared;

namespace HalalBook.Api.Data;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserName { get; set; } = "";
    public string NormalizedUserName { get; set; } = "";
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Bio { get; set; }
    public string? AvatarUrl { get; set; }
    public string? CoverUrl { get; set; }
    public string? Location { get; set; }
    public string? Website { get; set; }
    public UserRole Role { get; set; }
    public bool IsVerified { get; set; }
    public bool IsSuspended { get; set; }
    public bool EmailConfirmed { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public bool ShowOnlineStatus { get; set; } = true;
    public DateTime? LastSeenAt { get; set; }
    public ProfilePostPermission WhoCanPost { get; set; }
    public PrivacyAudience WhoCanMessage { get; set; } = PrivacyAudience.Everyone;
    /// <summary>Interface language (see <see cref="Languages"/>) and whether posts are shown translated into it.</summary>
    public string Language { get; set; } = "en";
    public bool AutoTranslate { get; set; } = true;
    /// <summary>HalalBook is open to everyone: members who aren't Muslim can hide the Islamic tools.</summary>
    public bool FaithFeatures { get; set; } = true;
    public DateOnly? Birthday { get; set; }
    public bool ShowBirthday { get; set; } = true;
    /// <summary>Phone notification settings (see NotificationSettingsDto).</summary>
    public bool NotifyShowPreview { get; set; } = true;
    public bool NotifyDisappearing { get; set; }
    public bool DoNotDisturb { get; set; }
    public bool VipsBreakThrough { get; set; } = true;
    /// <summary>The post shown first on this person's profile.</summary>
    public Guid? PinnedPostId { get; set; }
    /// <summary>Code in this person's invite link (halalbook.app/join/{code}), and who invited them.</summary>
    public string? InviteCode { get; set; }
    public Guid? InvitedById { get; set; }
    public PrivacyAudience WhoCanSendFriendRequests { get; set; } = PrivacyAudience.Everyone;
    public PrivacyAudience WhoCanSeeFriendsList { get; set; } = PrivacyAudience.Everyone;
    public PrivacyAudience WhoCanFindMe { get; set; } = PrivacyAudience.Everyone;
    public PrivacyAudience WhoCanFollow { get; set; } = PrivacyAudience.Everyone;
    /// <summary>Which version of the Community Agreement the person last read and accepted, and when.</summary>
    public string? AcceptedTermsVersion { get; set; }
    public DateTime? AcceptedTermsAt { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string PrayerMethod { get; set; } = "MWL";
    public string AsrSchool { get; set; } = "Shafi";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Friendship
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequesterId { get; set; }
    public User Requester { get; set; } = null!;
    public Guid AddresseeId { get; set; }
    public User Addressee { get; set; } = null!;
    public bool Accepted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Follow
{
    public Guid FollowerId { get; set; }
    public Guid FolloweeId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Silenced, not blocked: their messages and posts still arrive, but without notifications.</summary>
public class MutedUser
{
    public Guid OwnerId { get; set; }
    public Guid MutedId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Block
{
    public Guid BlockerId { get; set; }
    public Guid BlockedId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>One complete recitation of the Quran shared by a group: 30 juz, each claimed and read by someone.</summary>
public class Khatm
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    /// <summary>Who it's dedicated to or why, such as "For my late father, Abdul Karim".</summary>
    public string? Intention { get; set; }
    public DateOnly? DueDate { get; set; }
    public Guid CreatorId { get; set; }
    public User Creator { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public List<KhatmPart> Parts { get; set; } = [];
}

public class KhatmPart
{
    public Guid KhatmId { get; set; }
    public int Juz { get; set; }
    public Guid? UserId { get; set; }
    public DateTime? ClaimedAt { get; set; }
    public DateTime? DoneAt { get; set; }
}

/// <summary>One browser's visit on one day (anonymous random ID; no IP address is kept).</summary>
public class SiteVisit
{
    public long Id { get; set; }
    public DateOnly Day { get; set; }
    public string VisitorId { get; set; } = "";
    public Guid? UserId { get; set; }
    public string FirstPath { get; set; } = "/";
    public string Source { get; set; } = "Direct";
    public string Device { get; set; } = "Computer";
    public int PageViews { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
}

/// <summary>Page views per page section per day.</summary>
public class PageStat
{
    public long Id { get; set; }
    public DateOnly Day { get; set; }
    public string Path { get; set; } = "/";
    public int Views { get; set; }
}

/// <summary>Something for sale (or free) in the Marketplace.</summary>
public class Listing
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SellerId { get; set; }
    public User Seller { get; set; } = null!;
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public string Category { get; set; } = "Other";
    public string? Condition { get; set; }
    public string? Location { get; set; }
    /// <summary>Photo URLs (JSON array).</summary>
    public string Photos { get; set; } = "[]";
    public ListingStatus Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class PollOption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PostId { get; set; }
    public string Text { get; set; } = "";
    public int SortOrder { get; set; }
}

/// <summary>One vote per person per poll; voting again moves it.</summary>
public class PollVote
{
    public Guid PostId { get; set; }
    public Guid UserId { get; set; }
    public Guid OptionId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A browser or phone that asked for HalalBook notifications (Web Push).</summary>
public class PushSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Endpoint { get; set; } = "";
    public string P256dh { get; set; } = "";
    public string Auth { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Post
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AuthorId { get; set; }
    public User Author { get; set; } = null!;
    public string Content { get; set; } = "";
    public Visibility Visibility { get; set; }
    public PostKind Kind { get; set; }
    public string? Feeling { get; set; }
    public string? Location { get; set; }
    public Guid? GroupId { get; set; }
    public Group? Group { get; set; }
    public Guid? PageId { get; set; }
    public Page? Page { get; set; }
    public Guid? SharedPostId { get; set; }
    public Post? SharedPost { get; set; }
    /// <summary>Detected language of the post, and translations by language code (JSON).</summary>
    public string? Language { get; set; }
    public string? Translations { get; set; }
    /// <summary>A life event (see <see cref="LifeEvents"/>), shown as a special card on the timeline.</summary>
    public string? LifeEvent { get; set; }
    public DateOnly? LifeEventDate { get; set; }
    /// <summary>Set when the audience is one of the author's friend lists (Family, Close friends...).</summary>
    public Guid? FriendListId { get; set; }
    /// <summary>Set when someone posts on another person's profile.</summary>
    public Guid? TargetUserId { get; set; }
    public User? TargetUser { get; set; }
    public ModerationStatus Moderation { get; set; }
    public string? ModerationNote { get; set; }
    /// <summary>How many times the video has been watched (reels and short videos).</summary>
    public int ViewCount { get; set; }
    /// <summary>Scheduled posts stay hidden (except from their author) until this time (UTC), then appear as new.</summary>
    public DateTime? PublishAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EditedAt { get; set; }
    public bool IsDeleted { get; set; }
    public List<PostMedia> Media { get; set; } = [];
    public List<PostHashtag> Hashtags { get; set; } = [];
}

public class PostMedia
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PostId { get; set; }
    public string Url { get; set; } = "";
    public MediaType Type { get; set; }
    public int SortOrder { get; set; }
    /// <summary>AI-written description for screen readers.</summary>
    public string? AltText { get; set; }
}

public class Hashtag
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Tag { get; set; } = "";
}

public class PostHashtag
{
    public Guid PostId { get; set; }
    public Guid HashtagId { get; set; }
    public Hashtag Hashtag { get; set; } = null!;
}

public class Comment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PostId { get; set; }
    public Post Post { get; set; } = null!;
    public Guid AuthorId { get; set; }
    public User Author { get; set; } = null!;
    public Guid? ParentId { get; set; }
    public string Content { get; set; } = "";
    public string? MediaUrl { get; set; }
    /// <summary>Language guessed from the script, and translations by language code (JSON), shared by every reader.</summary>
    public string? Language { get; set; }
    public string? Translations { get; set; }
    public ModerationStatus Moderation { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Reaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? PostId { get; set; }
    public Guid? CommentId { get; set; }
    public ReactionType Type { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Bookmark
{
    public Guid UserId { get; set; }
    public Guid PostId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Story
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AuthorId { get; set; }
    public User Author { get; set; } = null!;
    public string? MediaUrl { get; set; }
    public MediaType MediaType { get; set; }
    public string? Text { get; set; }
    public string? Background { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(24);
}

public class StoryView
{
    public Guid StoryId { get; set; }
    public Guid ViewerId { get; set; }
    public DateTime ViewedAt { get; set; } = DateTime.UtcNow;
}

public class Group
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? CoverUrl { get; set; }
    public string? Category { get; set; }
    public GroupPrivacy Privacy { get; set; }
    public Guid OwnerId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class GroupMember
{
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
    public MemberRole Role { get; set; }
    public bool IsApproved { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}

public class Page
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Handle { get; set; } = "";
    public string? Category { get; set; }
    public string? Description { get; set; }
    public string? AvatarUrl { get; set; }
    public string? CoverUrl { get; set; }
    public string? Website { get; set; }
    public bool IsVerified { get; set; }
    public Guid OwnerId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class PageFollower
{
    public Guid PageId { get; set; }
    public Guid UserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Event
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public string? Location { get; set; }
    public string? OnlineUrl { get; set; }
    public string? CoverUrl { get; set; }
    public string? Category { get; set; }
    public Guid HostId { get; set; }
    public User Host { get; set; } = null!;
    public Guid? GroupId { get; set; }
    public Group? Group { get; set; }
    public Guid? PageId { get; set; }
    public Page? Page { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class EventRsvp
{
    public Guid EventId { get; set; }
    public Guid UserId { get; set; }
    public RsvpStatus Status { get; set; }
}

public class Conversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? Title { get; set; }
    public bool IsGroup { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
    public List<ConversationParticipant> Participants { get; set; } = [];
}

public class ConversationParticipant
{
    public Guid ConversationId { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTime LastReadAt { get; set; } = DateTime.UtcNow;
    /// <summary>Muted chats still receive messages, but without notifications or sounds.</summary>
    public bool Muted { get; set; }
}

public class Message
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public Guid SenderId { get; set; }
    public User Sender { get; set; } = null!;
    public string Content { get; set; } = "";
    public string? MediaUrl { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    /// <summary>A post shared into the chat; the preview is stored so the card still shows if the post changes.</summary>
    public Guid? SharedPostId { get; set; }
    public string? SharedPreview { get; set; }
    public string? SharedImageUrl { get; set; }
    /// <summary>Detected language and translations by language code (JSON), so everyone reads the chat in their own language.</summary>
    public string? Language { get; set; }
    public string? Translations { get; set; }
    public string? SharedAuthorName { get; set; }
    /// <summary>The message this one answers, shown as a quote above it.</summary>
    public Guid? ReplyToId { get; set; }
    public Message? ReplyTo { get; set; }
    /// <summary>Marked urgent by the sender; it breaks through Do Not Disturb when the sender is one of the reader's VIPs.</summary>
    public bool Urgent { get; set; }
    public List<MessageReaction> Reactions { get; set; } = [];
}

/// <summary>One person's emoji on a chat message (one each; tapping another replaces it).</summary>
public class MessageReaction
{
    public Guid MessageId { get; set; }
    public Guid UserId { get; set; }
    public string Emoji { get; set; } = "❤️";
}

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? ActorId { get; set; }
    public User? Actor { get; set; }
    public NotificationType Type { get; set; }
    public string Message { get; set; } = "";
    public string? Link { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Report
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReporterId { get; set; }
    public User Reporter { get; set; } = null!;
    public ReportTarget TargetType { get; set; }
    public Guid TargetId { get; set; }
    public string Reason { get; set; } = "";
    public ReportStatus Status { get; set; }
    public Guid? ResolvedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum EmailCodePurpose { VerifyEmail, ResetPassword, Login }

/// <summary>One-time code sent by email. Only a hash of the code is stored.</summary>
public class EmailCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public EmailCodePurpose Purpose { get; set; }
    public string CodeHash { get; set; } = "";
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
}

public class LiveSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HostId { get; set; }
    public User Host { get; set; } = null!;
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public LiveKind Kind { get; set; }
    public LiveStatus Status { get; set; }
    public string? CoverUrl { get; set; }
    public Guid? GroupId { get; set; }
    public Group? Group { get; set; }
    public Guid? EventId { get; set; }
    public DateTime? ScheduledFor { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public int PeakViewers { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>People a post is shared with (SpecificPeople) or hidden from (FriendsExcept).</summary>
public class PostAudience
{
    public Guid PostId { get; set; }
    public Guid UserId { get; set; }
}

public class Feedback
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserId { get; set; }
    public User? User { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public FeedbackCategory Category { get; set; }
    public FeedbackStatus Status { get; set; }
    public string Subject { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Page { get; set; }
    public string? ScreenshotUrl { get; set; }
    public string? Reply { get; set; }
    public string? AdminNote { get; set; }
    public Guid? HandledById { get; set; }
    public User? HandledBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>One row per distinct error; repeats increase Count and LastSeen.</summary>
public class ErrorLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Fingerprint { get; set; } = "";
    public string Source { get; set; } = "";
    public string Level { get; set; } = "Error";
    public string Message { get; set; } = "";
    public string? ExceptionType { get; set; }
    public string? StackTrace { get; set; }
    public string? Path { get; set; }
    public string? Method { get; set; }
    public int? StatusCode { get; set; }
    public Guid? UserId { get; set; }
    public string? TraceId { get; set; }
    public string? UserAgent { get; set; }
    public int Count { get; set; } = 1;
    public DateTime FirstSeen { get; set; } = DateTime.UtcNow;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public ErrorStatus Status { get; set; }
    public string? Note { get; set; }
    public Guid? ResolvedById { get; set; }
}

/// <summary>A personal list of friends (Family, Close friends, Best friends or custom), usable as a post audience and feed.</summary>
public class FriendList
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = "";
    public FriendListKind Kind { get; set; }
    public string Icon { get; set; } = "⭐";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<FriendListMember> Members { get; set; } = [];
}

public class FriendListMember
{
    public Guid ListId { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
}

/// <summary>An audio or video call in a conversation (1:1 or group).</summary>
public class Call
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public Guid CallerId { get; set; }
    public User Caller { get; set; } = null!;
    public bool IsVideo { get; set; }
    public CallStatus Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AnsweredAt { get; set; }
    public DateTime? EndedAt { get; set; }
}

/// <summary>A cached translation of an interface string, shared by everyone using that language.</summary>
public class UiTranslation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Language { get; set; } = "";
    public string Key { get; set; } = "";
    public string Source { get; set; } = "";
    public string Text { get; set; } = "";
}

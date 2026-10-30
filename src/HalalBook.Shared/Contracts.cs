namespace HalalBook.Shared;

// ---------- Enums ----------
public enum UserRole { User, Moderator, Admin }
public enum Visibility { Public, Friends, OnlyMe }
public enum PostKind { Standard, Reel }
public enum MediaType { Image, Video }
public enum ReactionType { Like, Love, MashaAllah, SubhanAllah, Ameen, Sad }
public enum ModerationStatus { Approved, PendingReview, Removed }
public enum FriendshipStatus { None, Pending, Accepted, IncomingRequest, OutgoingRequest, Self }
public enum GroupPrivacy { Public, Private }
public enum MemberRole { Member, Admin }
public enum RsvpStatus { Going, Interested, NotGoing }
public enum ReportStatus { Open, Resolved, Dismissed }
public enum ReportTarget { Post, Comment, User, Story, Message }
public enum NotificationType { FriendRequest, FriendAccepted, Follow, Reaction, Comment, Mention, Share, GroupInvite, EventInvite, Message, Moderation, System }

// ---------- Auth ----------
public record RegisterRequest(string UserName, string Email, string Password, string DisplayName);
public record LoginRequest(string UserNameOrEmail, string Password);
public record AuthResponse(string Token, DateTime ExpiresAt, UserDto User);

/// <summary>Where sign-in or sign-up stands: done (signed in), or waiting for an emailed code.</summary>
public enum AuthStep { Done, VerifyEmail, LoginCode }
public record AuthResult(AuthStep Step, AuthResponse? Auth, string? Email, string? Message);
public record VerifyCodeRequest(string Email, string Code);
public record EmailRequest(string Email);
public record ResendCodeRequest(string Email, AuthStep Step);
public record ResetPasswordRequest(string Email, string Code, string NewPassword);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record TwoFactorRequest(bool Enabled);
public record OnlineStatusRequest(bool Visible);
public record OnlineUserDto(UserSummaryDto User, bool IsFriend);

// ---------- Users ----------
public record UserSummaryDto(Guid Id, string UserName, string DisplayName, string? AvatarUrl, bool IsVerified);

public record UserDto(
    Guid Id, string UserName, string Email, string DisplayName, string? Bio, string? AvatarUrl, string? CoverUrl,
    string? Location, string? Website, UserRole Role, bool IsVerified, DateTime JoinedAt,
    double? Latitude, double? Longitude, string PrayerMethod, string AsrSchool, bool EmailConfirmed = false, bool TwoFactorEnabled = false, bool ShowOnlineStatus = true);

public record ProfileDto(
    UserSummaryDto User, string? Bio, string? CoverUrl, string? Location, string? Website, DateTime JoinedAt,
    int FriendCount, int FollowerCount, int FollowingCount, int PostCount,
    FriendshipStatus Friendship, bool IsFollowing, bool IsBlocked, bool IsOnline = false, DateTime? LastSeenAt = null);

public record UpdateProfileRequest(
    string? DisplayName, string? Bio, string? AvatarUrl, string? CoverUrl, string? Location, string? Website,
    double? Latitude, double? Longitude, string? PrayerMethod, string? AsrSchool);

// ---------- Media ----------
public record MediaDto(string Url, MediaType Type);
public record UploadResult(string Url, MediaType Type);

// ---------- Posts ----------
public record CreatePostRequest(
    string Content, Visibility Visibility = Visibility.Public, List<MediaDto>? Media = null,
    Guid? GroupId = null, Guid? PageId = null, Guid? SharedPostId = null, PostKind Kind = PostKind.Standard,
    string? Feeling = null, string? Location = null);

public record UpdatePostRequest(string Content, Visibility Visibility);

public record PostDto(
    Guid Id, UserSummaryDto Author, string Content, Visibility Visibility, PostKind Kind, DateTime CreatedAt, DateTime? EditedAt,
    List<MediaDto> Media, List<string> Hashtags, int CommentCount, int ShareCount,
    Dictionary<ReactionType, int> Reactions, ReactionType? MyReaction, bool IsBookmarked,
    ModerationStatus Moderation, string? Feeling, string? Location,
    GroupSummaryDto? Group, PageSummaryDto? Page, PostDto? SharedPost)
{
    public int TotalReactions => Reactions.Values.Sum();
}

public record CommentDto(
    Guid Id, Guid PostId, Guid? ParentId, UserSummaryDto Author, string Content, DateTime CreatedAt,
    int LikeCount, bool LikedByMe, List<CommentDto> Replies);

public record CreateCommentRequest(string Content, Guid? ParentId = null);
public record ReactRequest(ReactionType Type);
public record PagedResult<T>(List<T> Items, int Page, int PageSize, bool HasMore);

// ---------- Stories ----------
public record CreateStoryRequest(string? MediaUrl, MediaType MediaType, string? Text, string? Background);
public record StoryDto(Guid Id, string? MediaUrl, MediaType MediaType, string? Text, string? Background, DateTime CreatedAt, DateTime ExpiresAt, int ViewCount, bool Seen);
public record StoryReelDto(UserSummaryDto Author, List<StoryDto> Stories, bool AllSeen);

// ---------- Friends / follows ----------
public record FriendRequestDto(Guid Id, UserSummaryDto From, UserSummaryDto To, DateTime CreatedAt);

// ---------- Groups ----------
public record GroupSummaryDto(Guid Id, string Name, string? CoverUrl);
public record GroupDto(Guid Id, string Name, string? Description, string? CoverUrl, GroupPrivacy Privacy, string? Category,
    int MemberCount, bool IsMember, bool IsAdmin, bool IsPending, DateTime CreatedAt);
public record CreateGroupRequest(string Name, string? Description, string? CoverUrl, GroupPrivacy Privacy, string? Category);

// ---------- Pages ----------
public record PageSummaryDto(Guid Id, string Name, string Handle, string? AvatarUrl);
public record PageDto(Guid Id, string Name, string Handle, string? Category, string? Description, string? AvatarUrl, string? CoverUrl,
    string? Website, int FollowerCount, bool IsFollowing, bool IsOwner, bool IsVerified, DateTime CreatedAt);
public record CreatePageRequest(string Name, string Handle, string? Category, string? Description, string? AvatarUrl, string? CoverUrl, string? Website);

// ---------- Events ----------
public record EventDto(Guid Id, string Title, string? Description, DateTime StartsAt, DateTime? EndsAt, string? Location, string? OnlineUrl,
    string? CoverUrl, string? Category, UserSummaryDto Host, GroupSummaryDto? Group, PageSummaryDto? Page,
    int GoingCount, int InterestedCount, RsvpStatus? MyRsvp);
public record CreateEventRequest(string Title, string? Description, DateTime StartsAt, DateTime? EndsAt, string? Location, string? OnlineUrl,
    string? CoverUrl, string? Category, Guid? GroupId, Guid? PageId);
public record RsvpRequest(RsvpStatus Status);

// ---------- Messaging ----------
public record ConversationDto(Guid Id, string Title, bool IsGroup, List<UserSummaryDto> Participants, MessageDto? LastMessage, int UnreadCount);
public record MessageDto(Guid Id, Guid ConversationId, UserSummaryDto Sender, string Content, string? MediaUrl, DateTime SentAt);
public record StartConversationRequest(List<Guid> ParticipantIds, string? Title);
public record SendMessageRequest(string Content, string? MediaUrl);

// ---------- Notifications ----------
public record NotificationDto(Guid Id, NotificationType Type, UserSummaryDto? Actor, string Message, string? Link, bool IsRead, DateTime CreatedAt);

// ---------- Search / discovery ----------
public record SearchResultDto(List<UserSummaryDto> People, List<PostDto> Posts, List<GroupDto> Groups, List<PageDto> Pages, List<EventDto> Events, List<HashtagDto> Hashtags);
public record HashtagDto(string Tag, int PostCount);

// ---------- Moderation ----------
public record ReportRequest(ReportTarget TargetType, Guid TargetId, string Reason);
public record ReportDto(Guid Id, UserSummaryDto Reporter, ReportTarget TargetType, Guid TargetId, string Reason, ReportStatus Status, DateTime CreatedAt, string? TargetPreview);
public record ModerationResult(bool Allowed, bool NeedsReview, List<string> Reasons);

// ---------- Islamic ----------
public record PrayerTimesDto(DateOnly Date, string Method, string AsrSchool, double Latitude, double Longitude, int UtcOffsetMinutes,
    TimeOnly Fajr, TimeOnly Sunrise, TimeOnly Dhuhr, TimeOnly Asr, TimeOnly Maghrib, TimeOnly Isha,
    string NextPrayer, TimeOnly NextPrayerTime, HijriDateDto Hijri);
public record QiblaDto(double Latitude, double Longitude, double Bearing, double DistanceKm);
public record HijriDateDto(int Year, int Month, int Day, string MonthName, string MonthNameArabic, string Formatted);
public record CalendarDayDto(DateOnly Date, HijriDateDto Hijri, List<string> Occasions);
public record IslamicOccasionDto(string Name, HijriDateDto Hijri, DateOnly GregorianDate);
public record PrayerMethodDto(string Key, string Name);

// ---------- AI ----------
public record AiComposeRequest(string Text, string Mode = "polish"); // polish | shorter | warmer | arabic-greeting | caption
public record AiComposeResponse(string Text, List<string> SuggestedHashtags, ModerationResult Moderation, bool UsedAi);
public record AiTranslateRequest(string Text, string TargetLanguage);
public record AiTextResponse(string Text, bool UsedAi);
public record AiAskRequest(string Question, List<AiChatTurn>? History = null);
public record AiChatTurn(string Role, string Content);

namespace HalalBook.Shared;

// ---------- Enums ----------
public enum UserRole { User, Moderator, Admin }
/// <summary>Who can see a post. SpecificPeople and FriendsExcept use the post's audience list.</summary>
public enum Visibility { Public, Friends, OnlyMe, SpecificPeople, FriendsExcept, FriendList }
/// <summary>Who may post on someone's profile (their timeline).</summary>
public enum ProfilePostPermission { Friends, Everyone, OnlyMe }
/// <summary>Facebook-style privacy audiences for contact and discovery settings.</summary>
public enum PrivacyAudience { Everyone, FriendsOfFriends, Friends, OnlyMe }
public record PrivacySettingsDto(PrivacyAudience WhoCanMessage, PrivacyAudience WhoCanSendFriendRequests,
    PrivacyAudience WhoCanSeeFriendsList, PrivacyAudience WhoCanFindMe, PrivacyAudience WhoCanFollow);
/// <summary>Dua: a request for the community's prayers; people answer with "I made dua" (the Ameen reaction).</summary>
public enum PostKind { Standard, Reel, Dua }
public enum MediaType { Image, Video }
public enum ReactionType { Like, Love, MashaAllah, SubhanAllah, Ameen, Sad }
public enum ModerationStatus { Approved, PendingReview, Removed }
public enum FriendshipStatus { None, Pending, Accepted, IncomingRequest, OutgoingRequest, Self }
public enum GroupPrivacy { Public, Private }
public enum MemberRole { Member, Admin }
public enum RsvpStatus { Going, Interested, NotGoing }
public enum ReportStatus { Open, Resolved, Dismissed }
public enum ReportTarget { Post, Comment, User, Story, Message }
public enum NotificationType { FriendRequest, FriendAccepted, Follow, Reaction, Comment, Mention, Share, GroupInvite, EventInvite, Message, Moderation, System, Live }

// ---------- Auth ----------
public record RegisterRequest(string UserName, string Email, string Password, string DisplayName, string? AcceptedTermsVersion = null,
    string? InviteCode = null);
/// <summary>A member's personal invite link and the people who joined through it.</summary>
/// <summary>A browser's Web Push subscription (from PushSubscription.toJSON()).</summary>
public record PushSubscribeRequest(string Endpoint, string P256dh, string Auth);
public record InviteInfoDto(string Code, string Link, int JoinedCount, List<UserSummaryDto> Joined);
public record AcceptTermsRequest(string Version);
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
    double? Latitude, double? Longitude, string PrayerMethod, string AsrSchool, bool EmailConfirmed = false, bool TwoFactorEnabled = false, bool ShowOnlineStatus = true,
    ProfilePostPermission WhoCanPost = ProfilePostPermission.Friends, string? AcceptedTermsVersion = null,
    PrivacySettingsDto? Privacy = null, string Language = "en", bool AutoTranslate = true);

public record ProfileDto(
    UserSummaryDto User, string? Bio, string? CoverUrl, string? Location, string? Website, DateTime JoinedAt,
    int FriendCount, int FollowerCount, int FollowingCount, int PostCount,
    FriendshipStatus Friendship, bool IsFollowing, bool IsBlocked, bool IsOnline = false, DateTime? LastSeenAt = null, bool CanPost = false,
    bool CanMessage = true, bool CanAddFriend = true, bool CanSeeFriends = true, bool CanFollow = true, int MutualFriends = 0,
    Guid? PinnedPostId = null);

public record UpdateProfileRequest(
    string? DisplayName, string? Bio, string? AvatarUrl, string? CoverUrl, string? Location, string? Website,
    double? Latitude, double? Longitude, string? PrayerMethod, string? AsrSchool, ProfilePostPermission? WhoCanPost = null);

// ---------- Media ----------
public record MediaDto(string Url, MediaType Type, string? AltText = null);
public record UploadResult(string Url, MediaType Type, string? AltText = null, bool NeedsReview = false);

// ---------- Posts ----------
public record CreatePostRequest(
    string Content, Visibility Visibility = Visibility.Public, List<MediaDto>? Media = null,
    Guid? GroupId = null, Guid? PageId = null, Guid? SharedPostId = null, PostKind Kind = PostKind.Standard,
    string? Feeling = null, string? Location = null, Guid? TargetUserId = null,
    List<Guid>? AudienceUserIds = null, List<Guid>? GroupIds = null, bool AlsoOnProfile = false, Guid? FriendListId = null,
    string? LifeEvent = null, DateOnly? LifeEventDate = null, bool TranslateForUmmah = false);

public record UpdatePostRequest(string Content, Visibility Visibility, List<Guid>? AudienceUserIds = null);

public record PostDto(
    Guid Id, UserSummaryDto Author, string Content, Visibility Visibility, PostKind Kind, DateTime CreatedAt, DateTime? EditedAt,
    List<MediaDto> Media, List<string> Hashtags, int CommentCount, int ShareCount,
    Dictionary<ReactionType, int> Reactions, ReactionType? MyReaction, bool IsBookmarked,
    ModerationStatus Moderation, string? Feeling, string? Location,
    GroupSummaryDto? Group, PageSummaryDto? Page, PostDto? SharedPost, UserSummaryDto? TargetUser = null, List<UserSummaryDto>? Audience = null, string? FriendListName = null,
    string? LifeEvent = null, DateOnly? LifeEventDate = null, bool IsPinned = false,
    string? OriginalLanguage = null, string? TranslatedContent = null, string? TranslatedTo = null, int ViewCount = 0)
{
    public int TotalReactions => Reactions.Values.Sum();
}

public record CommentDto(
    Guid Id, Guid PostId, Guid? ParentId, UserSummaryDto Author, string Content, DateTime CreatedAt,
    int LikeCount, bool LikedByMe, List<CommentDto> Replies, string? MediaUrl = null, string? TranslatedContent = null);

public record CreateCommentRequest(string Content, Guid? ParentId = null, string? MediaUrl = null);
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
public record ConversationDto(Guid Id, string Title, bool IsGroup, List<UserSummaryDto> Participants, MessageDto? LastMessage, int UnreadCount,
    bool Muted = false, DateTime? OthersReadAt = null);
public record MuteRequest(bool Muted);
/// <summary>A short AI recap of a chat: the gist, and anything that needs a reply or an action.</summary>
public record ChatSummaryDto(string Summary, bool UsedAi);
public record MessageDto(Guid Id, Guid ConversationId, UserSummaryDto Sender, string Content, string? MediaUrl, DateTime SentAt,
    SharedPostPreview? SharedPost = null, string? OriginalLanguage = null, Dictionary<string, string>? Translations = null,
    ReplyPreviewDto? ReplyTo = null, List<MessageReactionDto>? Reactions = null);
public record ReplyPreviewDto(Guid Id, string SenderName, string Text);
public record MessageReactionDto(Guid UserId, string Emoji);
public record ReactToMessageRequest(string? Emoji);
/// <summary>Sent live when someone reacts to a message, or reads a chat.</summary>
public record MessageReactionsDto(Guid MessageId, Guid ConversationId, List<MessageReactionDto> Reactions);
public record ConversationReadDto(Guid ConversationId, Guid UserId, DateTime ReadAt);
/// <summary>Sent live when a message's translations are ready.</summary>
public record MessageTranslatedDto(Guid MessageId, Guid ConversationId, string? OriginalLanguage, Dictionary<string, string> Translations);
/// <summary>A post shared into a chat, shown as a small card.</summary>
public record SharedPostPreview(Guid PostId, string AuthorName, string? Text, string? ImageUrl);
public record StartConversationRequest(List<Guid> ParticipantIds, string? Title);
public record SendMessageRequest(string Content, string? MediaUrl, Guid? SharedPostId = null, Guid? ReplyToId = null);
/// <summary>Send a post to several people at once (each gets it in their chat with you).</summary>
public record SendPostRequest(Guid PostId, List<Guid> UserIds, string? Note);

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
/// <summary>The verse of the day: reference like "2:286", Arabic text, and a translation in the reader's language.</summary>
public record AyahDto(string Reference, string SurahName, string SurahNameArabic, string Arabic, string? Translation, string Language);

// ---------- AI ----------
public record AiComposeRequest(string Text, string Mode = "polish"); // polish | shorter | warmer | arabic-greeting | caption
public record AiComposeResponse(string Text, List<string> SuggestedHashtags, ModerationResult Moderation, bool UsedAi);
public record AiTranslateRequest(string Text, string TargetLanguage);
public record AiTextResponse(string Text, bool UsedAi);
public record AiAskRequest(string Question, List<AiChatTurn>? History = null);
public record AiChatTurn(string Role, string Content);

// ---------- Live ----------
public enum LiveKind { Show, Event, Podcast, Conference }
public enum LiveStatus { Scheduled, Live, Ended }
public record LiveSessionDto(Guid Id, string Title, string? Description, LiveKind Kind, LiveStatus Status, UserSummaryDto Host,
    DateTime? ScheduledFor, DateTime? StartedAt, DateTime? EndedAt, int ViewerCount, int PeakViewers,
    GroupSummaryDto? Group, Guid? EventId, string? CoverUrl, bool IsHost);
public record CreateLiveRequest(string Title, string? Description, LiveKind Kind, DateTime? ScheduledFor = null,
    Guid? GroupId = null, Guid? EventId = null, string? CoverUrl = null, bool StartNow = true);
public record LiveParticipantDto(string ConnectionId, UserSummaryDto User, bool IsHost, bool Sends);
public record LiveChatDto(UserSummaryDto User, string Text, DateTime At);
public record IceServerDto(string[] Urls, string? Username, string? Credential);

// ---------- AI insights ----------
public record SmartRepliesRequest(string Context, string Kind = "comment");
public record SmartRepliesResponse(List<string> Replies, bool UsedAi);
public record ToneRequest(string Text);
public record ToneResponse(bool IsHarsh, string? Suggestion, string? Note, bool UsedAi);

// ---------- Feedback & error log ----------
public enum FeedbackCategory { Question, Feedback, Suggestion, BugReport, Complaint, Appeal }
public enum FeedbackStatus { New, InProgress, Resolved, Closed }
public enum ErrorStatus { Open, Resolved, Ignored }

public record CreateFeedbackRequest(FeedbackCategory Category, string Subject, string Message, string? Page = null,
    string? ScreenshotUrl = null, string? Name = null, string? Email = null);
public record FeedbackDto(Guid Id, FeedbackCategory Category, FeedbackStatus Status, string Subject, string Message, string? Page,
    string? ScreenshotUrl, UserSummaryDto? User, string? Name, string? Email, string? Reply, string? AdminNote,
    DateTime CreatedAt, DateTime UpdatedAt, UserSummaryDto? HandledBy);
public record UpdateFeedbackRequest(FeedbackStatus Status, string? Reply, string? AdminNote);

public record ClientErrorRequest(string Source, string Message, string? StackTrace, string? Path, string? UserAgent);
public record ErrorLogDto(Guid Id, string Source, string Level, string Message, string? ExceptionType, string? StackTrace, string? Path,
    string? Method, int? StatusCode, int Count, DateTime FirstSeen, DateTime LastSeen, ErrorStatus Status, string? Note,
    UserSummaryDto? LastUser, string? TraceId, string? UserAgent);
public record UpdateErrorRequest(ErrorStatus Status, string? Note);
/// <summary>Sent by the browser on each page it shows; VisitorId is a random ID kept in that browser.</summary>
public record VisitRequest(string VisitorId, string? Path, string? Referrer);
public record DailyVisitsDto(DateOnly Day, int Visitors, int PageViews);
public record CountDto(string Name, int Count);
public record VisitorStatsDto(int OnlineNow, int MembersOnline, int Today, int Yesterday, int Last7Days, int Last30Days, int AllTime,
    int PageViewsToday, int PageViews7Days, List<DailyVisitsDto> Daily, List<CountDto> TopPages, List<CountDto> Sources, List<CountDto> Devices);
/// <summary>Gold and silver prices per gram in a currency, and the two nisab thresholds.</summary>
public record NisabDto(string Currency, double GoldPerGram, double SilverPerGram, double GoldNisabGrams, double SilverNisabGrams,
    double GoldNisab, double SilverNisab, DateTime PricesAt, List<string> Currencies);

/// <summary>A masjid or halal place from OpenStreetMap.</summary>
public record PlaceDto(string Name, string Type, double Latitude, double Longitude, string? Address, string? OpeningHours,
    string? Phone, string? Website, string? Cuisine, double DistanceKm);

// ---------- Quran khatm together ----------
public record CreateKhatmRequest(string Title, string? Intention, DateOnly? DueDate);
public record KhatmPartDto(int Juz, UserSummaryDto? Reader, bool Done, bool Mine);
public record KhatmDto(Guid Id, string Title, string? Intention, DateOnly? DueDate, UserSummaryDto Creator, DateTime CreatedAt, DateTime? CompletedAt,
    List<KhatmPartDto> Parts);

public record AdminStatsDto(int NewFeedback, int OpenFeedback, int OpenErrors, int ErrorsLast24h, int Users, int UsersLast7d,
    int Posts, int PostsLast24h, int PendingReview, int OpenReports);

// ---------- Friend lists ----------
public enum FriendListKind { Family, CloseFriends, BestFriends, Custom }
public record FriendListDto(Guid Id, string Name, FriendListKind Kind, string Icon, List<UserSummaryDto> Members);
public record SaveFriendListRequest(string Name, string? Icon);

// ---------- Calls ----------
public enum CallStatus { Ringing, Active, Ended, Missed, Declined }
public record CallDto(Guid Id, Guid ConversationId, string Title, bool IsVideo, bool IsGroup, CallStatus Status, UserSummaryDto Caller,
    List<UserSummaryDto> Participants, DateTime CreatedAt, DateTime? AnsweredAt, DateTime? EndedAt);
public record StartCallRequest(Guid ConversationId, bool Video);

// ---------- Timeline ----------
public record TimelineMediaDto(Guid PostId, string Url, MediaType Type, string? AltText, DateTime CreatedAt);
public record LifeEventKind(string Key, string Icon, string Title, string Category);

/// <summary>Life events, including Islamic milestones Facebook doesn't have.</summary>
public static class LifeEvents
{
    public static readonly IReadOnlyList<LifeEventKind> All =
    [
        new("shahada", "☪️", "Took my Shahada", "Faith"),
        new("hajj", "🕋", "Performed Hajj", "Faith"),
        new("umrah", "🕋", "Performed Umrah", "Faith"),
        new("hifz", "📖", "Completed memorizing the Quran", "Faith"),
        new("surah", "📖", "Memorized a new surah", "Faith"),
        new("khatm", "📗", "Completed reading the Quran", "Faith"),
        new("first-ramadan", "🌙", "Fasted my first Ramadan", "Faith"),
        new("arabic", "🔤", "Started learning Arabic", "Faith"),
        new("nikah", "💍", "Got married (Nikah)", "Family"),
        new("baby", "👶", "Welcomed a new baby", "Family"),
        new("aqiqah", "🐑", "Held an Aqiqah", "Family"),
        new("graduated", "🎓", "Graduated", "Education"),
        new("new-job", "💼", "Started a new job", "Work"),
        new("business", "🏪", "Started a halal business", "Work"),
        new("moved", "🏠", "Moved to a new home", "Home"),
        new("travel", "✈️", "Travelled somewhere new", "Travel"),
        new("charity", "🤲", "Completed a charity project", "Community"),
        new("volunteer", "🌱", "Started volunteering", "Community"),
        new("other", "🌟", "Life event", "Other"),
    ];

    public static LifeEventKind? Find(string? key) => All.FirstOrDefault(e => e.Key == key);
}

public record LanguageSettingsRequest(string Language, bool AutoTranslate);

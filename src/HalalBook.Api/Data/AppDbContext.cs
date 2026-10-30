using Microsoft.EntityFrameworkCore;

namespace HalalBook.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<Follow> Follows => Set<Follow>();
    public DbSet<Block> Blocks => Set<Block>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<PostMedia> PostMedia => Set<PostMedia>();
    public DbSet<Hashtag> Hashtags => Set<Hashtag>();
    public DbSet<PostHashtag> PostHashtags => Set<PostHashtag>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<Reaction> Reactions => Set<Reaction>();
    public DbSet<Bookmark> Bookmarks => Set<Bookmark>();
    public DbSet<Story> Stories => Set<Story>();
    public DbSet<StoryView> StoryViews => Set<StoryView>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<Page> Pages => Set<Page>();
    public DbSet<PageFollower> PageFollowers => Set<PageFollower>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventRsvp> EventRsvps => Set<EventRsvp>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationParticipant> ConversationParticipants => Set<ConversationParticipant>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<EmailCode> EmailCodes => Set<EmailCode>();
    public DbSet<LiveSession> LiveSessions => Set<LiveSession>();
    public DbSet<PostAudience> PostAudiences => Set<PostAudience>();
    public DbSet<Feedback> Feedback => Set<Feedback>();
    public DbSet<FriendList> FriendLists => Set<FriendList>();
    public DbSet<Call> Calls => Set<Call>();
    public DbSet<FriendListMember> FriendListMembers => Set<FriendListMember>();
    public DbSet<ErrorLog> ErrorLogs => Set<ErrorLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.NormalizedUserName).IsUnique();
            e.HasIndex(x => x.NormalizedEmail).IsUnique();
            e.Property(x => x.UserName).HasMaxLength(32);
            e.Property(x => x.NormalizedUserName).HasMaxLength(32);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.NormalizedEmail).HasMaxLength(256);
            e.Property(x => x.DisplayName).HasMaxLength(80);
        });

        b.Entity<Friendship>(e =>
        {
            e.HasIndex(x => new { x.RequesterId, x.AddresseeId }).IsUnique();
            e.HasOne(x => x.Requester).WithMany().HasForeignKey(x => x.RequesterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Addressee).WithMany().HasForeignKey(x => x.AddresseeId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Follow>().HasKey(x => new { x.FollowerId, x.FolloweeId });
        b.Entity<Block>().HasKey(x => new { x.BlockerId, x.BlockedId });

        b.Entity<Post>(e =>
        {
            e.HasIndex(x => x.CreatedAt);
            e.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Page).WithMany().HasForeignKey(x => x.PageId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.SharedPost).WithMany().HasForeignKey(x => x.SharedPostId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.TargetUser).WithMany().HasForeignKey(x => x.TargetUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Media).WithOne().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Hashtags).WithOne().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Call>(e =>
        {
            e.HasIndex(x => new { x.ConversationId, x.CreatedAt });
            e.HasOne(x => x.Caller).WithMany().HasForeignKey(x => x.CallerId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<FriendList>(e =>
        {
            e.HasIndex(x => x.OwnerId);
            e.Property(x => x.Name).HasMaxLength(60);
            e.Property(x => x.Icon).HasMaxLength(16);
            e.HasMany(x => x.Members).WithOne().HasForeignKey(x => x.ListId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<FriendListMember>(e =>
        {
            e.HasKey(x => new { x.ListId, x.UserId });
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Feedback>(e =>
        {
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.Property(x => x.Subject).HasMaxLength(200);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.HandledBy).WithMany().HasForeignKey(x => x.HandledById).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ErrorLog>(e =>
        {
            e.HasIndex(x => x.Fingerprint).IsUnique();
            e.HasIndex(x => new { x.Status, x.LastSeen });
            e.Property(x => x.Fingerprint).HasMaxLength(40);
            e.Property(x => x.Source).HasMaxLength(20);
            e.Property(x => x.Level).HasMaxLength(20);
            e.Property(x => x.Method).HasMaxLength(10);
            e.Property(x => x.TraceId).HasMaxLength(100);
        });

        b.Entity<PostAudience>(e =>
        {
            e.HasKey(x => new { x.PostId, x.UserId });
            e.HasIndex(x => x.UserId);
        });

        b.Entity<Hashtag>().HasIndex(x => x.Tag).IsUnique();
        b.Entity<PostHashtag>().HasKey(x => new { x.PostId, x.HashtagId });

        b.Entity<Comment>(e =>
        {
            e.HasIndex(x => x.PostId);
            e.HasOne(x => x.Post).WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Reaction>(e =>
        {
            e.HasIndex(x => new { x.PostId, x.UserId });
            e.HasIndex(x => new { x.CommentId, x.UserId });
        });

        b.Entity<Bookmark>().HasKey(x => new { x.UserId, x.PostId });
        b.Entity<Story>().HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId);
        b.Entity<StoryView>().HasKey(x => new { x.StoryId, x.ViewerId });
        b.Entity<GroupMember>().HasKey(x => new { x.GroupId, x.UserId });
        b.Entity<Page>().HasIndex(x => x.Handle).IsUnique();
        b.Entity<PageFollower>().HasKey(x => new { x.PageId, x.UserId });

        b.Entity<Event>(e =>
        {
            e.HasOne(x => x.Host).WithMany().HasForeignKey(x => x.HostId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Page).WithMany().HasForeignKey(x => x.PageId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<EventRsvp>().HasKey(x => new { x.EventId, x.UserId });

        b.Entity<Conversation>().HasMany(x => x.Participants).WithOne().HasForeignKey(x => x.ConversationId);
        b.Entity<ConversationParticipant>(e =>
        {
            e.HasKey(x => new { x.ConversationId, x.UserId });
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        });
        b.Entity<Message>(e =>
        {
            e.HasIndex(x => new { x.ConversationId, x.SentAt });
            e.HasOne(x => x.Sender).WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Notification>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasOne(x => x.Actor).WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<LiveSession>(e =>
        {
            e.HasIndex(x => new { x.Status, x.ScheduledFor });
            e.Property(x => x.Title).HasMaxLength(140);
            e.HasOne(x => x.Host).WithMany().HasForeignKey(x => x.HostId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<EmailCode>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.Purpose, x.CreatedAt });
            e.Property(x => x.CodeHash).HasMaxLength(64);
        });

        b.Entity<Report>().HasOne(x => x.Reporter).WithMany().HasForeignKey(x => x.ReporterId).OnDelete(DeleteBehavior.Restrict);
    }
}

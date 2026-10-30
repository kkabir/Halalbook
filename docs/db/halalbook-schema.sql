IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE TABLE [Blocks] (
        [BlockerId] uniqueidentifier NOT NULL,
        [BlockedId] uniqueidentifier NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Blocks] PRIMARY KEY ([BlockerId], [BlockedId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE TABLE [Bookmarks] (
        [UserId] uniqueidentifier NOT NULL,
        [PostId] uniqueidentifier NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Bookmarks] PRIMARY KEY ([UserId], [PostId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE TABLE [Conversations] (
        [Id] uniqueidentifier NOT NULL,
        [Title] nvarchar(max) NULL,
        [IsGroup] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [LastActivityAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Conversations] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE TABLE [EventRsvps] (
        [EventId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Status] int NOT NULL,
        CONSTRAINT [PK_EventRsvps] PRIMARY KEY ([EventId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE TABLE [Follows] (
        [FollowerId] uniqueidentifier NOT NULL,
        [FolloweeId] uniqueidentifier NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Follows] PRIMARY KEY ([FollowerId], [FolloweeId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE TABLE [GroupMembers] (
        [GroupId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Role] int NOT NULL,
        [IsApproved] bit NOT NULL,
        [JoinedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_GroupMembers] PRIMARY KEY ([GroupId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE TABLE [Groups] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(max) NOT NULL,
        [Description] nvarchar(max) NULL,
        [CoverUrl] nvarchar(max) NULL,
        [Category] nvarchar(max) NULL,
        [Privacy] int NOT NULL,
        [OwnerId] uniqueidentifier NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Groups] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE TABLE [Hashtags] (
        [Id] uniqueidentifier NOT NULL,
        [Tag] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_Hashtags] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE TABLE [PageFollowers] (
        [PageId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_PageFollowers] PRIMARY KEY ([PageId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE TABLE [Pages] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(max) NOT NULL,
        [Handle] nvarchar(450) NOT NULL,
        [Category] nvarchar(max) NULL,
        [Description] nvarchar(max) NULL,
        [AvatarUrl] nvarchar(max) NULL,
        [CoverUrl] nvarchar(max) NULL,
        [Website] nvarchar(max) NULL,
        [IsVerified] bit NOT NULL,
        [OwnerId] uniqueidentifier NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Pages] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE TABLE [Reactions] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [PostId] uniqueidentifier NULL,
        [CommentId] uniqueidentifier NULL,
        [Type] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Reactions] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE TABLE [StoryViews] (
        [StoryId] uniqueidentifier NOT NULL,
        [ViewerId] uniqueidentifier NOT NULL,
        [ViewedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_StoryViews] PRIMARY KEY ([StoryId], [ViewerId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] uniqueidentifier NOT NULL,
        [UserName] nvarchar(32) NOT NULL,
        [NormalizedUserName] nvarchar(32) NOT NULL,
        [Email] nvarchar(256) NOT NULL,
        [NormalizedEmail] nvarchar(256) NOT NULL,
        [PasswordHash] nvarchar(max) NOT NULL,
        [DisplayName] nvarchar(80) NOT NULL,
        [Bio] nvarchar(max) NULL,
        [AvatarUrl] nvarchar(max) NULL,
        [CoverUrl] nvarchar(max) NULL,
        [Location] nvarchar(max) NULL,
        [Website] nvarchar(max) NULL,
        [Role] int NOT NULL,
        [IsVerified] bit NOT NULL,
        [IsSuspended] bit NOT NULL,
        [Latitude] float NULL,
        [Longitude] float NULL,
        [PrayerMethod] nvarchar(max) NOT NULL,
        [AsrSchool] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE TABLE [ConversationParticipants] (
        [ConversationId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [LastReadAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ConversationParticipants] PRIMARY KEY ([ConversationId], [UserId]),
        CONSTRAINT [FK_ConversationParticipants_Conversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [Conversations] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ConversationParticipants_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE TABLE [Events] (
        [Id] uniqueidentifier NOT NULL,
        [Title] nvarchar(max) NOT NULL,
        [Description] nvarchar(max) NULL,
        [StartsAt] datetime2 NOT NULL,
        [EndsAt] datetime2 NULL,
        [Location] nvarchar(max) NULL,
        [OnlineUrl] nvarchar(max) NULL,
        [CoverUrl] nvarchar(max) NULL,
        [Category] nvarchar(max) NULL,
        [HostId] uniqueidentifier NOT NULL,
        [GroupId] uniqueidentifier NULL,
        [PageId] uniqueidentifier NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Events] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Events_Groups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [Groups] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Events_Pages_PageId] FOREIGN KEY ([PageId]) REFERENCES [Pages] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Events_Users_HostId] FOREIGN KEY ([HostId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE TABLE [Friendships] (
        [Id] uniqueidentifier NOT NULL,
        [RequesterId] uniqueidentifier NOT NULL,
        [AddresseeId] uniqueidentifier NOT NULL,
        [Accepted] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Friendships] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Friendships_Users_AddresseeId] FOREIGN KEY ([AddresseeId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Friendships_Users_RequesterId] FOREIGN KEY ([RequesterId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE TABLE [Messages] (
        [Id] uniqueidentifier NOT NULL,
        [ConversationId] uniqueidentifier NOT NULL,
        [SenderId] uniqueidentifier NOT NULL,
        [Content] nvarchar(max) NOT NULL,
        [MediaUrl] nvarchar(max) NULL,
        [SentAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Messages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Messages_Users_SenderId] FOREIGN KEY ([SenderId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE TABLE [Notifications] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [ActorId] uniqueidentifier NULL,
        [Type] int NOT NULL,
        [Message] nvarchar(max) NOT NULL,
        [Link] nvarchar(max) NULL,
        [IsRead] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Notifications] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Notifications_Users_ActorId] FOREIGN KEY ([ActorId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE TABLE [Posts] (
        [Id] uniqueidentifier NOT NULL,
        [AuthorId] uniqueidentifier NOT NULL,
        [Content] nvarchar(max) NOT NULL,
        [Visibility] int NOT NULL,
        [Kind] int NOT NULL,
        [Feeling] nvarchar(max) NULL,
        [Location] nvarchar(max) NULL,
        [GroupId] uniqueidentifier NULL,
        [PageId] uniqueidentifier NULL,
        [SharedPostId] uniqueidentifier NULL,
        [Moderation] int NOT NULL,
        [ModerationNote] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [EditedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        CONSTRAINT [PK_Posts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Posts_Groups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [Groups] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Posts_Pages_PageId] FOREIGN KEY ([PageId]) REFERENCES [Pages] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Posts_Posts_SharedPostId] FOREIGN KEY ([SharedPostId]) REFERENCES [Posts] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Posts_Users_AuthorId] FOREIGN KEY ([AuthorId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE TABLE [Reports] (
        [Id] uniqueidentifier NOT NULL,
        [ReporterId] uniqueidentifier NOT NULL,
        [TargetType] int NOT NULL,
        [TargetId] uniqueidentifier NOT NULL,
        [Reason] nvarchar(max) NOT NULL,
        [Status] int NOT NULL,
        [ResolvedById] uniqueidentifier NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Reports] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Reports_Users_ReporterId] FOREIGN KEY ([ReporterId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE TABLE [Stories] (
        [Id] uniqueidentifier NOT NULL,
        [AuthorId] uniqueidentifier NOT NULL,
        [MediaUrl] nvarchar(max) NULL,
        [MediaType] int NOT NULL,
        [Text] nvarchar(max) NULL,
        [Background] nvarchar(max) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Stories] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Stories_Users_AuthorId] FOREIGN KEY ([AuthorId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE TABLE [Comments] (
        [Id] uniqueidentifier NOT NULL,
        [PostId] uniqueidentifier NOT NULL,
        [AuthorId] uniqueidentifier NOT NULL,
        [ParentId] uniqueidentifier NULL,
        [Content] nvarchar(max) NOT NULL,
        [Moderation] int NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Comments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Comments_Posts_PostId] FOREIGN KEY ([PostId]) REFERENCES [Posts] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_Comments_Users_AuthorId] FOREIGN KEY ([AuthorId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE TABLE [PostHashtags] (
        [PostId] uniqueidentifier NOT NULL,
        [HashtagId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_PostHashtags] PRIMARY KEY ([PostId], [HashtagId]),
        CONSTRAINT [FK_PostHashtags_Hashtags_HashtagId] FOREIGN KEY ([HashtagId]) REFERENCES [Hashtags] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_PostHashtags_Posts_PostId] FOREIGN KEY ([PostId]) REFERENCES [Posts] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE TABLE [PostMedia] (
        [Id] uniqueidentifier NOT NULL,
        [PostId] uniqueidentifier NOT NULL,
        [Url] nvarchar(max) NOT NULL,
        [Type] int NOT NULL,
        [SortOrder] int NOT NULL,
        CONSTRAINT [PK_PostMedia] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PostMedia_Posts_PostId] FOREIGN KEY ([PostId]) REFERENCES [Posts] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Comments_AuthorId] ON [Comments] ([AuthorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Comments_PostId] ON [Comments] ([PostId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ConversationParticipants_UserId] ON [ConversationParticipants] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Events_GroupId] ON [Events] ([GroupId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Events_HostId] ON [Events] ([HostId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Events_PageId] ON [Events] ([PageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Friendships_AddresseeId] ON [Friendships] ([AddresseeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Friendships_RequesterId_AddresseeId] ON [Friendships] ([RequesterId], [AddresseeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Hashtags_Tag] ON [Hashtags] ([Tag]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Messages_ConversationId_SentAt] ON [Messages] ([ConversationId], [SentAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Messages_SenderId] ON [Messages] ([SenderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Notifications_ActorId] ON [Notifications] ([ActorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Notifications_UserId_CreatedAt] ON [Notifications] ([UserId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Pages_Handle] ON [Pages] ([Handle]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PostHashtags_HashtagId] ON [PostHashtags] ([HashtagId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PostMedia_PostId] ON [PostMedia] ([PostId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Posts_AuthorId] ON [Posts] ([AuthorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Posts_CreatedAt] ON [Posts] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Posts_GroupId] ON [Posts] ([GroupId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Posts_PageId] ON [Posts] ([PageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Posts_SharedPostId] ON [Posts] ([SharedPostId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Reactions_CommentId_UserId] ON [Reactions] ([CommentId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Reactions_PostId_UserId] ON [Reactions] ([PostId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Reports_ReporterId] ON [Reports] ([ReporterId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Stories_AuthorId] ON [Stories] ([AuthorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_NormalizedEmail] ON [Users] ([NormalizedEmail]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_NormalizedUserName] ON [Users] ([NormalizedUserName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030012329_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261030012329_InitialCreate', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030022511_EmailVerification'
)
BEGIN
    ALTER TABLE [Users] ADD [EmailConfirmed] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030022511_EmailVerification'
)
BEGIN
    ALTER TABLE [Users] ADD [TwoFactorEnabled] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030022511_EmailVerification'
)
BEGIN
    CREATE TABLE [EmailCodes] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Purpose] int NOT NULL,
        [CodeHash] nvarchar(64) NOT NULL,
        [Attempts] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [ConsumedAt] datetime2 NULL,
        CONSTRAINT [PK_EmailCodes] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030022511_EmailVerification'
)
BEGIN
    CREATE INDEX [IX_EmailCodes_UserId_Purpose_CreatedAt] ON [EmailCodes] ([UserId], [Purpose], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030022511_EmailVerification'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261030022511_EmailVerification', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030023815_OnlineStatus'
)
BEGIN
    ALTER TABLE [Users] ADD [LastSeenAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030023815_OnlineStatus'
)
BEGIN
    ALTER TABLE [Users] ADD [ShowOnlineStatus] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030023815_OnlineStatus'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261030023815_OnlineStatus', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030024529_LiveSessions'
)
BEGIN
    CREATE TABLE [LiveSessions] (
        [Id] uniqueidentifier NOT NULL,
        [HostId] uniqueidentifier NOT NULL,
        [Title] nvarchar(140) NOT NULL,
        [Description] nvarchar(max) NULL,
        [Kind] int NOT NULL,
        [Status] int NOT NULL,
        [CoverUrl] nvarchar(max) NULL,
        [GroupId] uniqueidentifier NULL,
        [EventId] uniqueidentifier NULL,
        [ScheduledFor] datetime2 NULL,
        [StartedAt] datetime2 NULL,
        [EndedAt] datetime2 NULL,
        [PeakViewers] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_LiveSessions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_LiveSessions_Groups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [Groups] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_LiveSessions_Users_HostId] FOREIGN KEY ([HostId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030024529_LiveSessions'
)
BEGIN
    CREATE INDEX [IX_LiveSessions_GroupId] ON [LiveSessions] ([GroupId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030024529_LiveSessions'
)
BEGIN
    CREATE INDEX [IX_LiveSessions_HostId] ON [LiveSessions] ([HostId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030024529_LiveSessions'
)
BEGIN
    CREATE INDEX [IX_LiveSessions_Status_ScheduledFor] ON [LiveSessions] ([Status], [ScheduledFor]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030024529_LiveSessions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261030024529_LiveSessions', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030025742_ProfilePostsAndCommentMedia'
)
BEGIN
    ALTER TABLE [Users] ADD [WhoCanPost] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030025742_ProfilePostsAndCommentMedia'
)
BEGIN
    ALTER TABLE [Posts] ADD [TargetUserId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030025742_ProfilePostsAndCommentMedia'
)
BEGIN
    ALTER TABLE [Comments] ADD [MediaUrl] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030025742_ProfilePostsAndCommentMedia'
)
BEGIN
    CREATE INDEX [IX_Posts_TargetUserId] ON [Posts] ([TargetUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030025742_ProfilePostsAndCommentMedia'
)
BEGIN
    ALTER TABLE [Posts] ADD CONSTRAINT [FK_Posts_Users_TargetUserId] FOREIGN KEY ([TargetUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030025742_ProfilePostsAndCommentMedia'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261030025742_ProfilePostsAndCommentMedia', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030030543_PostAudience'
)
BEGIN
    CREATE TABLE [PostAudiences] (
        [PostId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_PostAudiences] PRIMARY KEY ([PostId], [UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030030543_PostAudience'
)
BEGIN
    CREATE INDEX [IX_PostAudiences_UserId] ON [PostAudiences] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030030543_PostAudience'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261030030543_PostAudience', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030031353_MediaAltText'
)
BEGIN
    ALTER TABLE [PostMedia] ADD [AltText] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030031353_MediaAltText'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261030031353_MediaAltText', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030035906_CommunityAgreement'
)
BEGIN
    ALTER TABLE [Users] ADD [AcceptedTermsAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030035906_CommunityAgreement'
)
BEGIN
    ALTER TABLE [Users] ADD [AcceptedTermsVersion] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030035906_CommunityAgreement'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261030035906_CommunityAgreement', N'10.0.14');
END;

COMMIT;
GO


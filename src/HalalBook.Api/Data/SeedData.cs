using HalalBook.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HalalBook.Api.Data;

/// <summary>Demo content for local development (enabled with Seed:Enabled in appsettings.Development.json).</summary>
public static class SeedData
{
    public static async Task RunAsync(AppDbContext db, IPasswordHasher<User> hasher, string password)
    {
        if (string.IsNullOrEmpty(password) || await db.Users.AnyAsync()) return;

        User Make(string userName, string name, string bio, string location, UserRole role = UserRole.User)
        {
            var u = new User
            {
                UserName = userName, NormalizedUserName = userName.ToUpperInvariant(),
                Email = $"{userName}@example.com", NormalizedEmail = $"{userName}@example.com".ToUpperInvariant(),
                DisplayName = name, Bio = bio, Location = location, Role = role, IsVerified = role == UserRole.Admin,
                CreatedAt = DateTime.UtcNow.AddDays(-Random.Shared.Next(20, 400)),
            };
            u.PasswordHash = hasher.HashPassword(u, password);
            return u;
        }

        var aisha = Make("aisha", "Aisha Rahman", "Software engineer · Lifelong learner · Tea enthusiast", "Toronto, Canada", UserRole.Admin);
        var omar = Make("omar", "Omar Siddiqui", "Imam's assistant, youth mentor, Quran teacher.", "Dhaka, Bangladesh");
        var yusuf = Make("yusuf", "Yusuf Karim", "Photographer capturing masjids around the world 📷", "Istanbul, Türkiye");
        var maryam = Make("maryam", "Maryam Hassan", "Mother of three. Halal recipes and home life.", "London, UK");
        db.Users.AddRange(aisha, omar, yusuf, maryam);

        db.Friendships.AddRange(
            new Friendship { RequesterId = aisha.Id, AddresseeId = omar.Id, Accepted = true },
            new Friendship { RequesterId = aisha.Id, AddresseeId = maryam.Id, Accepted = true },
            new Friendship { RequesterId = yusuf.Id, AddresseeId = omar.Id, Accepted = true },
            new Friendship { RequesterId = yusuf.Id, AddresseeId = aisha.Id, Accepted = false });
        db.Follows.AddRange(
            new Follow { FollowerId = aisha.Id, FolloweeId = omar.Id }, new Follow { FollowerId = omar.Id, FolloweeId = aisha.Id },
            new Follow { FollowerId = aisha.Id, FolloweeId = maryam.Id }, new Follow { FollowerId = maryam.Id, FolloweeId = aisha.Id },
            new Follow { FollowerId = aisha.Id, FolloweeId = yusuf.Id });

        var group = new Group { Name = "Ramadan Prep Circle", Description = "Plan, share and stay motivated for the blessed month together.", Category = "Faith", OwnerId = omar.Id };
        db.Groups.Add(group);
        db.GroupMembers.AddRange(
            new GroupMember { GroupId = group.Id, UserId = omar.Id, Role = MemberRole.Admin, IsApproved = true },
            new GroupMember { GroupId = group.Id, UserId = aisha.Id, IsApproved = true },
            new GroupMember { GroupId = group.Id, UserId = maryam.Id, IsApproved = true });

        var page = new Page { Name = "Daily Reminders", Handle = "dailyreminders", Category = "Education", Description = "A short reminder every day to keep the heart soft.", OwnerId = omar.Id, IsVerified = true };
        db.Pages.Add(page);
        db.PageFollowers.AddRange(new PageFollower { PageId = page.Id, UserId = aisha.Id }, new PageFollower { PageId = page.Id, UserId = maryam.Id });

        db.Events.Add(new Event
        {
            Title = "Community Iftar & Family Night", Description = "Bring a dish to share. Kids' corner and a short talk after Maghrib.",
            StartsAt = DateTime.UtcNow.AddDays(9).Date.AddHours(22), EndsAt = DateTime.UtcNow.AddDays(9).Date.AddHours(25),
            Location = "Central Masjid Hall", Category = "Community", HostId = omar.Id, GroupId = group.Id,
        });

        var hashtags = new Dictionary<string, Hashtag>();
        Post AddPost(User author, string content, int hoursAgo, Guid? groupId = null, Guid? pageId = null, string? image = null)
        {
            var p = new Post { AuthorId = author.Id, Content = content, CreatedAt = DateTime.UtcNow.AddHours(-hoursAgo), GroupId = groupId, PageId = pageId };
            if (image is not null) p.Media.Add(new PostMedia { Url = image, Type = MediaType.Image });
            foreach (var tag in Services.PostService.ExtractHashtags(content))
            {
                if (!hashtags.TryGetValue(tag, out var h)) hashtags[tag] = h = new Hashtag { Tag = tag };
                p.Hashtags.Add(new PostHashtag { PostId = p.Id, HashtagId = h.Id, Hashtag = h });
            }
            db.Posts.Add(p);
            return p;
        }

        var p1 = AddPost(aisha, "Alhamdulillah, finished memorizing Surah Al-Mulk today! It took 3 months of 10 minutes after Fajr. Small consistent steps really work. #quran #hifz #motivation", 3);
        AddPost(omar, "Reminder: the best of deeds are those done consistently, even if small. What's one small habit you're building this week? #reminder #habits", 6, pageId: page.Id);
        var p3 = AddPost(yusuf, "Golden hour at the Süleymaniye Mosque. The geometry of the domes never stops amazing me. #architecture #istanbul #masjid", 10);
        AddPost(maryam, "Tried a new slow-cooked lamb haleem recipe for the weekend. Recipe in the comments! #halalfood #recipes", 20);
        AddPost(omar, "Who's joining the Ramadan prep sessions this Saturday? We'll cover planning your Quran goals. #ramadan", 26, groupId: group.Id);
        AddPost(aisha, "Looking for good books on Islamic history for teenagers. Recommendations welcome! #books #history", 40);

        db.Comments.AddRange(
            new Comment { PostId = p1.Id, AuthorId = omar.Id, Content = "MashaAllah, may Allah make it a light for you!", CreatedAt = DateTime.UtcNow.AddHours(-2) },
            new Comment { PostId = p1.Id, AuthorId = maryam.Id, Content = "So inspiring, I'm starting tomorrow inshaAllah.", CreatedAt = DateTime.UtcNow.AddHours(-1) },
            new Comment { PostId = p3.Id, AuthorId = aisha.Id, Content = "SubhanAllah, stunning shot.", CreatedAt = DateTime.UtcNow.AddHours(-8) });
        db.Reactions.AddRange(
            new Reaction { PostId = p1.Id, UserId = omar.Id, Type = ReactionType.MashaAllah },
            new Reaction { PostId = p1.Id, UserId = maryam.Id, Type = ReactionType.Love },
            new Reaction { PostId = p3.Id, UserId = aisha.Id, Type = ReactionType.SubhanAllah },
            new Reaction { PostId = p3.Id, UserId = omar.Id, Type = ReactionType.Like });

        db.Stories.AddRange(
            new Story { AuthorId = omar.Id, Text = "Jumu'ah Mubarak! Don't forget Surah Al-Kahf today 🌙", Background = "emerald" },
            new Story { AuthorId = maryam.Id, Text = "Baking date cookies with the kids 🍪", Background = "sunset" });

        var conv = new Conversation { LastActivityAt = DateTime.UtcNow.AddMinutes(-30) };
        conv.Participants.AddRange([new ConversationParticipant { UserId = aisha.Id }, new ConversationParticipant { UserId = omar.Id }]);
        db.Conversations.Add(conv);
        db.Messages.AddRange(
            new Message { ConversationId = conv.Id, SenderId = omar.Id, Content = "Assalamu alaikum! Are you coming to the iftar next week?", SentAt = DateTime.UtcNow.AddMinutes(-40) },
            new Message { ConversationId = conv.Id, SenderId = aisha.Id, Content = "Wa alaikum assalam! Yes inshaAllah, I'll bring dessert.", SentAt = DateTime.UtcNow.AddMinutes(-30) });

        db.Notifications.Add(new Notification { UserId = aisha.Id, ActorId = yusuf.Id, Type = NotificationType.FriendRequest, Message = "Yusuf Karim sent you a friend request.", Link = "/friends" });

        await db.SaveChangesAsync();
    }
}

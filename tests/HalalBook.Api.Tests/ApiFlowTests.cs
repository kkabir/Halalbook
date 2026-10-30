using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HalalBook.Shared;
using Microsoft.AspNetCore.Hosting;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using HalalBook.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HalalBook.Api.Tests;

/// <summary>Captures outgoing email so tests can read the codes.</summary>
public sealed class CapturingEmailSender : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();
    public Task SendAsync(EmailMessage message, CancellationToken ct = default) { Sent.Enqueue(message); return Task.CompletedTask; }

    public string LastCode(string to) =>
        Regex.Match(Sent.Last(m => m.To.Equals(to, StringComparison.OrdinalIgnoreCase)).Text, @"\b(\d{6})\b").Groups[1].Value;
}

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public CapturingEmailSender Email { get; } = new();

    private readonly string _db = Path.Combine(Path.GetTempPath(), $"halalbook-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_db}");
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("Jwt:Key", "test-key-" + new string('x', 48));
        builder.UseSetting("Auth:RequestsPerMinute", "1000");
        builder.ConfigureTestServices(s => s.AddSingleton<IEmailSender>(Email));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_db); } catch (IOException) { }
    }
}

public class ApiFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private async Task<HttpClient> SignUp(string name)
    {
        var client = factory.CreateClient();
        var email = $"{name}@example.com";
        var res = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(name, email, "Bismillah-123", name, Terms.Version));
        res.EnsureSuccessStatusCode();
        var step = (await res.Content.ReadFromJsonAsync<AuthResult>(Json))!;
        Assert.Equal(AuthStep.VerifyEmail, step.Step);
        Assert.Null(step.Auth);

        var verified = await client.PostAsJsonAsync("/api/auth/verify-email", new VerifyCodeRequest(email, factory.Email.LastCode(email)));
        verified.EnsureSuccessStatusCode();
        var auth = (await verified.Content.ReadFromJsonAsync<AuthResult>(Json))!.Auth!;
        Assert.True(auth.User.EmailConfirmed);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return client;
    }

    [Fact]
    public async Task Feed_RequiresAuth()
    {
        var res = await factory.CreateClient().GetAsync("/api/feed");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Post_React_Comment_Flow()
    {
        var alice = await SignUp("alice_" + Random.Shared.Next(9999));
        var created = await alice.PostAsJsonAsync("/api/posts", new CreatePostRequest("Alhamdulillah for today #gratitude"), Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var post = (await created.Content.ReadFromJsonAsync<PostDto>(Json))!;
        Assert.Contains("gratitude", post.Hashtags);

        (await alice.PutAsJsonAsync($"/api/posts/{post.Id}/reaction", new ReactRequest(ReactionType.MashaAllah), Json)).EnsureSuccessStatusCode();
        (await alice.PostAsJsonAsync($"/api/posts/{post.Id}/comments", new CreateCommentRequest("Ameen"), Json)).EnsureSuccessStatusCode();

        var after = (await alice.GetFromJsonAsync<PostDto>($"/api/posts/{post.Id}", Json))!;
        Assert.Equal(ReactionType.MashaAllah, after.MyReaction);
        Assert.Equal(1, after.CommentCount);

        var feed = (await alice.GetFromJsonAsync<PagedResult<PostDto>>("/api/feed", Json))!;
        Assert.Contains(feed.Items, p => p.Id == post.Id);
    }

    [Fact]
    public async Task FriendsOnlyPost_IsHiddenFromStrangers()
    {
        var a = await SignUp("amina_" + Random.Shared.Next(9999));
        var b = await SignUp("bilal_" + Random.Shared.Next(9999));
        var res = await a.PostAsJsonAsync("/api/posts", new CreatePostRequest("Family only", Visibility.Friends), Json);
        var post = (await res.Content.ReadFromJsonAsync<PostDto>(Json))!;
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/posts/{post.Id}")).StatusCode);
    }

    [Fact]
    public async Task BlockedContent_IsRejected()
    {
        var c = await SignUp("carim_" + Random.Shared.Next(9999));
        var res = await c.PostAsJsonAsync("/api/posts", new CreatePostRequest("watch porn here"), Json);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
    }

    [Fact]
    public async Task PrayerTimes_ArePublic()
    {
        var t = await factory.CreateClient().GetFromJsonAsync<PrayerTimesDto>("/api/islamic/prayer-times?lat=21.4225&lng=39.8262&offset=180&method=Makkah", Json);
        Assert.NotNull(t);
        Assert.True(t!.Fajr < t.Isha);
    }

    [Fact]
    public async Task UnconfirmedAccount_CannotSignIn_AndWrongCodeFails()
    {
        var client = factory.CreateClient();
        var name = "dawud_" + Random.Shared.Next(9999);
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(name, $"{name}@example.com", "Bismillah-123", name, Terms.Version));
        var login = (await (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(name, "Bismillah-123"))).Content.ReadFromJsonAsync<AuthResult>(Json))!;
        Assert.Equal(AuthStep.VerifyEmail, login.Step);
        Assert.Null(login.Auth);
        var bad = await client.PostAsJsonAsync("/api/auth/verify-email", new VerifyCodeRequest($"{name}@example.com", "000000"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task ForgotAndResetPassword()
    {
        var name = "esa_" + Random.Shared.Next(9999);
        var email = $"{name}@example.com";
        await SignUp(name);
        var anon = factory.CreateClient();
        (await anon.PostAsJsonAsync("/api/auth/forgot-password", new EmailRequest(email))).EnsureSuccessStatusCode();
        Assert.Contains("reset", factory.Email.Sent.Last(m => m.To == email).Subject);
        var reset = await anon.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(email, factory.Email.LastCode(email), "New-Password-456"));
        reset.EnsureSuccessStatusCode();
        Assert.Contains("password was changed", factory.Email.Sent.Last(m => m.To == email).Subject);

        var old = await anon.PostAsJsonAsync("/api/auth/login", new LoginRequest(name, "Bismillah-123"));
        Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);
        var fresh = (await (await anon.PostAsJsonAsync("/api/auth/login", new LoginRequest(name, "New-Password-456"))).Content.ReadFromJsonAsync<AuthResult>(Json))!;
        Assert.Equal(AuthStep.Done, fresh.Step);

        // Unknown addresses get the same answer, so accounts can't be discovered.
        Assert.Equal(HttpStatusCode.OK, (await anon.PostAsJsonAsync("/api/auth/forgot-password", new EmailRequest("nobody@example.com"))).StatusCode);
    }

    [Fact]
    public async Task TwoStepSignIn_RequiresEmailedCode()
    {
        var name = "fatima_" + Random.Shared.Next(9999);
        var email = $"{name}@example.com";
        var me = await SignUp(name);
        (await me.PutAsJsonAsync("/api/users/me/two-factor", new TwoFactorRequest(true), Json)).EnsureSuccessStatusCode();

        var anon = factory.CreateClient();
        var login = (await (await anon.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Bismillah-123"))).Content.ReadFromJsonAsync<AuthResult>(Json))!;
        Assert.Equal(AuthStep.LoginCode, login.Step);
        Assert.Null(login.Auth);
        var done = (await (await anon.PostAsJsonAsync("/api/auth/login-code", new VerifyCodeRequest(email, factory.Email.LastCode(email)))).Content.ReadFromJsonAsync<AuthResult>(Json))!;
        Assert.Equal(AuthStep.Done, done.Step);
        Assert.NotNull(done.Auth);
    }

    [Fact]
    public async Task OnlineStatus_CanBeHidden()
    {
        var me = await SignUp("ghazi_" + Random.Shared.Next(9999));
        Assert.Empty((await me.GetFromJsonAsync<List<OnlineUserDto>>("/api/presence/online", Json))!);
        var hidden = await me.PutAsJsonAsync("/api/users/me/online-status", new OnlineStatusRequest(false), Json);
        Assert.False((await hidden.Content.ReadFromJsonAsync<UserDto>(Json))!.ShowOnlineStatus);
        var meDto = (await me.GetFromJsonAsync<UserDto>("/api/users/me", Json))!;
        Assert.False(meDto.ShowOnlineStatus);
    }

    [Fact]
    public async Task LiveSession_Lifecycle()
    {
        var host = await SignUp("hamza_" + Random.Shared.Next(9999));
        var viewer = await SignUp("ibrahim_" + Random.Shared.Next(9999));

        var scheduled = (await (await host.PostAsJsonAsync("/api/live",
            new CreateLiveRequest("Friday halaqa", null, LiveKind.Event, DateTime.UtcNow.AddDays(1), StartNow: false), Json))
            .Content.ReadFromJsonAsync<LiveSessionDto>(Json))!;
        Assert.Equal(LiveStatus.Scheduled, scheduled.Status);
        Assert.True(scheduled.IsHost);
        Assert.Contains((await viewer.GetFromJsonAsync<List<LiveSessionDto>>("/api/live?filter=upcoming", Json))!, s => s.Id == scheduled.Id);

        // Only the host can start or end it.
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync($"/api/live/{scheduled.Id}/start", null)).StatusCode);
        var started = (await (await host.PostAsync($"/api/live/{scheduled.Id}/start", null)).Content.ReadFromJsonAsync<LiveSessionDto>(Json))!;
        Assert.Equal(LiveStatus.Live, started.Status);
        Assert.Contains((await viewer.GetFromJsonAsync<List<LiveSessionDto>>("/api/live", Json))!, s => s.Id == scheduled.Id);

        var ended = (await (await host.PostAsync($"/api/live/{scheduled.Id}/end", null)).Content.ReadFromJsonAsync<LiveSessionDto>(Json))!;
        Assert.Equal(LiveStatus.Ended, ended.Status);
        Assert.NotEmpty((await viewer.GetFromJsonAsync<List<IceServerDto>>("/api/live/ice-servers", Json))!);
    }

    [Fact]
    public async Task ProfilePosts_FollowOwnersSetting()
    {
        var jamal = await SignUp("jamal_" + Random.Shared.Next(9999));
        var khalid = await SignUp("khalid_" + Random.Shared.Next(9999));
        var jamalId = (await jamal.GetFromJsonAsync<UserDto>("/api/users/me", Json))!.Id;

        // Default: friends only, and they aren't friends.
        var denied = await khalid.PostAsJsonAsync("/api/posts", new CreatePostRequest("Salaam!", TargetUserId: jamalId), Json);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        await jamal.PutAsJsonAsync("/api/users/me", new UpdateProfileRequest(null, null, null, null, null, null, null, null, null, null, ProfilePostPermission.Everyone), Json);
        var ok = await khalid.PostAsJsonAsync("/api/posts", new CreatePostRequest("Salaam brother!", TargetUserId: jamalId), Json);
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var post = (await ok.Content.ReadFromJsonAsync<PostDto>(Json))!;
        Assert.Equal(jamalId, post.TargetUser!.Id);

        // The profile owner can remove posts others leave on their profile.
        Assert.Equal(HttpStatusCode.NoContent, (await jamal.DeleteAsync($"/api/posts/{post.Id}")).StatusCode);
    }

    [Fact]
    public async Task CommentPhoto_MustBeAnUploadedImage()
    {
        var c = await SignUp("layla_" + Random.Shared.Next(9999));
        var post = (await (await c.PostAsJsonAsync("/api/posts", new CreatePostRequest("Photo thread"), Json)).Content.ReadFromJsonAsync<PostDto>(Json))!;
        var withPhoto = await c.PostAsJsonAsync($"/api/posts/{post.Id}/comments", new CreateCommentRequest("", null, "https://api.halalbook.app/uploads/2026/10/abc.jpg"), Json);
        withPhoto.EnsureSuccessStatusCode();
        Assert.Equal("https://api.halalbook.app/uploads/2026/10/abc.jpg", (await withPhoto.Content.ReadFromJsonAsync<CommentDto>(Json))!.MediaUrl);
        var external = await c.PostAsJsonAsync($"/api/posts/{post.Id}/comments", new CreateCommentRequest("", null, "https://evil.example.com/x.exe"), Json);
        Assert.Equal(HttpStatusCode.BadRequest, external.StatusCode);
    }

    [Fact]
    public async Task CommentUpload_RejectsLargePhotosAndVideos()
    {
        var c = await SignUp("musa_" + Random.Shared.Next(9999));
        var png = new byte[3_000_000];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(png, 0);
        using var form = new MultipartFormDataContent { { new ByteArrayContent(png), "file", "big.png" } };
        var res = await c.PostAsync("/api/media?purpose=comment", form);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, res.StatusCode);
    }

    private async Task<(HttpClient Client, Guid Id)> SignUpWithId(string prefix)
    {
        var c = await SignUp(prefix + "_" + Random.Shared.Next(99999));
        return (c, (await c.GetFromJsonAsync<UserDto>("/api/users/me", Json))!.Id);
    }

    private static async Task MakeFriends(HttpClient a, Guid aId, HttpClient b, Guid bId)
    {
        (await a.PostAsync($"/api/friends/{bId}", null)).EnsureSuccessStatusCode();
        (await b.PostAsync($"/api/friends/{aId}/accept", null)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task CustomAudiences_AreEnforced()
    {
        var (nadia, nadiaId) = await SignUpWithId("nadia");
        var (omar, omarId) = await SignUpWithId("omarx");
        var (sami, samiId) = await SignUpWithId("sami");
        await MakeFriends(nadia, nadiaId, omar, omarId);
        await MakeFriends(nadia, nadiaId, sami, samiId);

        async Task<Guid> Post(Visibility v, params Guid[] people) =>
            (await (await nadia.PostAsJsonAsync("/api/posts", new CreatePostRequest($"For {v}", v, AudienceUserIds: [.. people]), Json))
                .Content.ReadFromJsonAsync<PostDto>(Json))!.Id;

        var onlyOmar = await Post(Visibility.SpecificPeople, omarId);
        var notSami = await Post(Visibility.FriendsExcept, samiId);

        Assert.Equal(HttpStatusCode.OK, (await omar.GetAsync($"/api/posts/{onlyOmar}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await sami.GetAsync($"/api/posts/{onlyOmar}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await omar.GetAsync($"/api/posts/{notSami}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await sami.GetAsync($"/api/posts/{notSami}")).StatusCode);

        // The author sees who the audience is.
        var mine = (await nadia.GetFromJsonAsync<PostDto>($"/api/posts/{onlyOmar}", Json))!;
        Assert.Equal(omarId, Assert.Single(mine.Audience!).Id);
        // Others don't.
        Assert.Null((await omar.GetFromJsonAsync<PostDto>($"/api/posts/{onlyOmar}", Json))!.Audience);
    }

    [Fact]
    public async Task PostToSeveralGroupsAndProfile()
    {
        var (c, _) = await SignUpWithId("rania");
        async Task<Guid> Group(string name) =>
            (await (await c.PostAsJsonAsync("/api/groups", new CreateGroupRequest(name, null, null, GroupPrivacy.Public, "Faith"), Json))
                .Content.ReadFromJsonAsync<GroupDto>(Json))!.Id;
        var g1 = await Group("Sisters circle");
        var g2 = await Group("Quran study");

        var res = await c.PostAsJsonAsync("/api/posts", new CreatePostRequest("Halaqa tonight after Isha", GroupIds: [g1, g2], AlsoOnProfile: true), Json);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Single((await c.GetFromJsonAsync<PagedResult<PostDto>>($"/api/groups/{g1}/posts", Json))!.Items);
        Assert.Single((await c.GetFromJsonAsync<PagedResult<PostDto>>($"/api/groups/{g2}/posts", Json))!.Items);
        var me = (await c.GetFromJsonAsync<UserDto>("/api/users/me", Json))!;
        Assert.Contains((await c.GetFromJsonAsync<PagedResult<PostDto>>($"/api/users/{me.UserName}/posts", Json))!.Items, p => p.Content.StartsWith("Halaqa"));
    }

    [Fact]
    public async Task AiFeatures_WorkWithoutAnApiKey()
    {
        var (c, _) = await SignUpWithId("yasmin");

        var harsh = await (await c.PostAsJsonAsync("/api/ai/tone", new ToneRequest("you are so stupid"), Json)).Content.ReadFromJsonAsync<ToneResponse>(Json);
        Assert.True(harsh!.IsHarsh);
        var kind = await (await c.PostAsJsonAsync("/api/ai/tone", new ToneRequest("I see it differently, but thank you for sharing"), Json)).Content.ReadFromJsonAsync<ToneResponse>(Json);
        Assert.False(kind!.IsHarsh);

        var replies = await (await c.PostAsJsonAsync("/api/ai/replies", new SmartRepliesRequest("Alhamdulillah, we just had a baby girl!"), Json)).Content.ReadFromJsonAsync<SmartRepliesResponse>(Json);
        Assert.Contains("Mabrook! 🎉", replies!.Replies);

        var digest = await c.GetFromJsonAsync<AiTextResponse>("/api/ai/digest", Json);
        Assert.False(string.IsNullOrWhiteSpace(digest!.Text));

        var post = (await (await c.PostAsJsonAsync("/api/posts", new CreatePostRequest("The best of you are those who learn the Quran and teach it."), Json)).Content.ReadFromJsonAsync<PostDto>(Json))!;
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"/api/ai/check-sources/{post.Id}")).StatusCode);
    }

    [Theory]
    [InlineData("free p0rn here")]
    [InlineData("check out n.u.d.e.s")]
    [InlineData("visit www.xnxx.com now")]
    [InlineData("watch on pornhub")]
    public async Task DisguisedAdultContent_IsBlocked(string text)
    {
        var (c, _) = await SignUpWithId("zaid");
        var res = await c.PostAsJsonAsync("/api/posts", new CreatePostRequest(text), Json);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
    }

    [Fact]
    public async Task UncheckedMedia_WaitsForAModerator()
    {
        // No AI key in tests, so photo posts go to the review queue instead of going live unchecked.
        var (c, _) = await SignUpWithId("bushra");
        var png = new byte[2000];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(png, 0);
        using var form = new MultipartFormDataContent { { new ByteArrayContent(png), "file", "photo.png" } };
        var upload = (await (await c.PostAsync("/api/media?purpose=post", form)).Content.ReadFromJsonAsync<UploadResult>(Json))!;
        Assert.True(upload.NeedsReview);
        var post = (await (await c.PostAsJsonAsync("/api/posts", new CreatePostRequest("My photo", Media: [new MediaDto(upload.Url, MediaType.Image)]), Json))
            .Content.ReadFromJsonAsync<PostDto>(Json))!;
        Assert.Equal(ModerationStatus.PendingReview, post.Moderation);
    }

    [Fact]
    public async Task ThreeReports_HideAPost()
    {
        var (author, _) = await SignUpWithId("author");
        var post = (await (await author.PostAsJsonAsync("/api/posts", new CreatePostRequest("Ordinary words"), Json)).Content.ReadFromJsonAsync<PostDto>(Json))!;
        var viewer = await SignUpWithId("viewer");
        Assert.Equal(HttpStatusCode.OK, (await viewer.Client.GetAsync($"/api/posts/{post.Id}")).StatusCode);
        for (int i = 0; i < 3; i++)
        {
            var (r, _) = await SignUpWithId("reporter" + i);
            (await r.PostAsJsonAsync("/api/reports", new ReportRequest(ReportTarget.Post, post.Id, "Inappropriate"), Json)).EnsureSuccessStatusCode();
        }
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.Client.GetAsync($"/api/posts/{post.Id}")).StatusCode);
    }

    [Fact]
    public async Task Register_RequiresTheAgreement()
    {
        var name = "noterms_" + Random.Shared.Next(9999);
        var res = await factory.CreateClient().PostAsJsonAsync("/api/auth/register", new RegisterRequest(name, $"{name}@example.com", "Bismillah-123", name), Json);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    private async Task<HttpClient> SignUpAdmin()
    {
        var (c, id) = await SignUpWithId("admin");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HalalBook.Api.Data.AppDbContext>();
            var u = await db.Users.FindAsync(id);
            u!.Role = UserRole.Admin;
            await db.SaveChangesAsync();
        }
        // Sign in again so the token carries the Admin role.
        var me = (await c.GetFromJsonAsync<UserDto>("/api/users/me", Json))!;
        var login = (await (await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(me.UserName, "Bismillah-123"), Json))
            .Content.ReadFromJsonAsync<AuthResult>(Json))!;
        var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Auth!.Token);
        return admin;
    }

    [Fact]
    public async Task Feedback_FlowsToAdminAndBack()
    {
        var (member, _) = await SignUpWithId("feedbacker");
        (await member.PostAsJsonAsync("/api/feedback", new CreateFeedbackRequest(FeedbackCategory.Suggestion, "Add Quran stories", "It would be lovely."), Json)).EnsureSuccessStatusCode();

        // Visitors who aren't signed in must leave an email.
        var anon = await factory.CreateClient().PostAsJsonAsync("/api/feedback", new CreateFeedbackRequest(FeedbackCategory.Question, "Can't sign in", "Help"), Json);
        Assert.Equal(HttpStatusCode.BadRequest, anon.StatusCode);

        // Members can't open the admin console.
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/admin/feedback")).StatusCode);

        var admin = await SignUpAdmin();
        var item = (await admin.GetFromJsonAsync<List<FeedbackDto>>("/api/admin/feedback?q=Quran%20stories", Json))!.First();
        (await admin.PutAsJsonAsync($"/api/admin/feedback/{item.Id}", new UpdateFeedbackRequest(FeedbackStatus.Resolved, "Great idea, it's on our list!", null), Json)).EnsureSuccessStatusCode();

        var mine = (await member.GetFromJsonAsync<List<FeedbackDto>>("/api/feedback/mine", Json))!.Single();
        Assert.Equal(FeedbackStatus.Resolved, mine.Status);
        Assert.Equal("Great idea, it's on our list!", mine.Reply);
    }

    [Fact]
    public async Task Errors_AreLoggedGroupedAndResolvable()
    {
        var anon = factory.CreateClient();
        for (int i = 0; i < 3; i++)
            await anon.PostAsJsonAsync("/api/errors/client", new ClientErrorRequest("Browser", "TypeError: x is undefined (test)", "at foo (app.js:1)", "/reels", "test"), Json);

        var admin = await SignUpAdmin();
        ErrorLogDto? logged = null;
        for (int i = 0; i < 20 && logged is null; i++)
        {
            await Task.Delay(150); // written in the background
            logged = (await admin.GetFromJsonAsync<List<ErrorLogDto>>("/api/admin/errors?source=Browser", Json))!
                .FirstOrDefault(e => e.Message.Contains("x is undefined (test)") && e.Count == 3);
        }
        Assert.NotNull(logged);
        (await admin.PutAsJsonAsync($"/api/admin/errors/{logged!.Id}", new UpdateErrorRequest(ErrorStatus.Resolved, "Fixed"), Json)).EnsureSuccessStatusCode();
        Assert.Contains((await admin.GetFromJsonAsync<List<ErrorLogDto>>("/api/admin/errors?status=Resolved", Json))!, e => e.Id == logged.Id);
    }

    [Fact]
    public async Task PrivacySettings_AreEnforced()
    {
        var (owner, ownerId) = await SignUpWithId("private");
        var (stranger, _) = await SignUpWithId("stranger");
        var ownerName = (await owner.GetFromJsonAsync<UserDto>("/api/users/me", Json))!.UserName;

        var settings = new PrivacySettingsDto(PrivacyAudience.Friends, PrivacyAudience.FriendsOfFriends, PrivacyAudience.OnlyMe, PrivacyAudience.Friends, PrivacyAudience.Friends);
        var saved = (await (await owner.PutAsJsonAsync("/api/users/me/privacy", settings, Json)).Content.ReadFromJsonAsync<UserDto>(Json))!;
        Assert.Equal(PrivacyAudience.Friends, saved.Privacy!.WhoCanMessage);

        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PostAsJsonAsync("/api/conversations", new StartConversationRequest([ownerId], null), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PostAsync($"/api/friends/{ownerId}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync($"/api/users/{ownerName}/friends")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PostAsync($"/api/users/{ownerId}/follow", null)).StatusCode);

        var profile = (await stranger.GetFromJsonAsync<ProfileDto>($"/api/users/{ownerName}", Json))!;
        Assert.False(profile.CanMessage);
        Assert.False(profile.CanAddFriend);
        Assert.False(profile.CanSeeFriends);

        var search = (await stranger.GetFromJsonAsync<SearchResultDto>($"/api/search?q={ownerName}", Json))!;
        Assert.DoesNotContain(search.People, x => x.Id == ownerId);
    }

    [Fact]
    public async Task SharePost_InMessagesAndPublicLink()
    {
        var (a, aId) = await SignUpWithId("sharer");
        var (b, bId) = await SignUpWithId("receiver");
        await MakeFriends(a, aId, b, bId);
        var pub = (await (await a.PostAsJsonAsync("/api/posts", new CreatePostRequest("A public reminder"), Json)).Content.ReadFromJsonAsync<PostDto>(Json))!;
        var priv = (await (await a.PostAsJsonAsync("/api/posts", new CreatePostRequest("Friends only", Visibility.Friends), Json)).Content.ReadFromJsonAsync<PostDto>(Json))!;

        var sent = await (await a.PostAsJsonAsync("/api/conversations/send-post", new SendPostRequest(pub.Id, [bId], "Look at this"), Json)).Content.ReadFromJsonAsync<int>(Json);
        Assert.Equal(1, sent);
        var conv = (await b.GetFromJsonAsync<List<ConversationDto>>("/api/conversations", Json))!.Single();
        var msg = (await b.GetFromJsonAsync<List<MessageDto>>($"/api/conversations/{conv.Id}/messages", Json))!.Last();
        Assert.Equal(pub.Id, msg.SharedPost!.PostId);

        var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync($"/api/public/posts/{pub.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/api/public/posts/{priv.Id}")).StatusCode);
    }

    [Fact]
    public async Task FriendLists_WorkAsAudiencesAndFeeds()
    {
        var (me, meId) = await SignUpWithId("listowner");
        var (mum, mumId) = await SignUpWithId("mum");
        var (coworker, coId) = await SignUpWithId("coworker");
        await MakeFriends(me, meId, mum, mumId);
        await MakeFriends(me, meId, coworker, coId);

        var lists = (await me.GetFromJsonAsync<List<FriendListDto>>("/api/friend-lists", Json))!;
        Assert.Equal(["Family", "Close friends", "Best friends"], lists.Select(l => l.Name).ToArray());
        var family = lists.First(l => l.Kind == FriendListKind.Family);
        (await me.PostAsync($"/api/friend-lists/{family.Id}/members/{mumId}", null)).EnsureSuccessStatusCode();

        var post = (await (await me.PostAsJsonAsync("/api/posts", new CreatePostRequest("Family dinner tonight", Visibility.FriendList, FriendListId: family.Id), Json))
            .Content.ReadFromJsonAsync<PostDto>(Json))!;
        Assert.Equal(HttpStatusCode.OK, (await mum.GetAsync($"/api/posts/{post.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await coworker.GetAsync($"/api/posts/{post.Id}")).StatusCode);

        // Mum's posts show up in my Family feed.
        await mum.PostAsJsonAsync("/api/posts", new CreatePostRequest("Love you all"), Json);
        var feed = (await me.GetFromJsonAsync<PagedResult<PostDto>>($"/api/feed?list={family.Id}", Json))!;
        Assert.All(feed.Items, p => Assert.Equal(mumId, p.Author.Id));
        Assert.NotEmpty(feed.Items);
    }

    [Fact]
    public async Task Calls_RingAnswerEndAndLogInChat()
    {
        var (a, aId) = await SignUpWithId("caller");
        var (b, bId) = await SignUpWithId("callee");
        await MakeFriends(a, aId, b, bId);
        var conv = (await (await a.PostAsJsonAsync("/api/conversations", new StartConversationRequest([bId], null), Json)).Content.ReadFromJsonAsync<ConversationDto>(Json))!;

        var call = (await (await a.PostAsJsonAsync("/api/calls", new StartCallRequest(conv.Id, true), Json)).Content.ReadFromJsonAsync<CallDto>(Json))!;
        Assert.Equal(CallStatus.Ringing, call.Status);
        // Starting again while it rings returns the same call.
        var again = (await (await b.PostAsJsonAsync("/api/calls", new StartCallRequest(conv.Id, true), Json)).Content.ReadFromJsonAsync<CallDto>(Json))!;
        Assert.Equal(call.Id, again.Id);

        var accepted = (await (await b.PostAsync($"/api/calls/{call.Id}/accept", null)).Content.ReadFromJsonAsync<CallDto>(Json))!;
        Assert.Equal(CallStatus.Active, accepted.Status);
        (await a.PostAsync($"/api/calls/{call.Id}/end", null)).EnsureSuccessStatusCode();

        var history = (await b.GetFromJsonAsync<List<CallDto>>("/api/calls/history", Json))!;
        Assert.Equal(CallStatus.Ended, history.First(c => c.Id == call.Id).Status);
        var messages = (await b.GetFromJsonAsync<List<MessageDto>>($"/api/conversations/{conv.Id}/messages", Json))!;
        Assert.Contains(messages, m => m.Content.StartsWith("🎥 Video call ·"));

        // A call nobody answers is logged as missed.
        var missed = (await (await a.PostAsJsonAsync("/api/calls", new StartCallRequest(conv.Id, false), Json)).Content.ReadFromJsonAsync<CallDto>(Json))!;
        (await a.PostAsync($"/api/calls/{missed.Id}/end", null)).EnsureSuccessStatusCode();
        messages = (await b.GetFromJsonAsync<List<MessageDto>>($"/api/conversations/{conv.Id}/messages", Json))!;
        Assert.Contains(messages, m => m.Content == "📵 Missed voice call");
    }

    [Fact]
    public async Task Timeline_LifeEventsPinningAndMedia()
    {
        var (me, _) = await SignUpWithId("timeline");
        var name = (await me.GetFromJsonAsync<UserDto>("/api/users/me", Json))!.UserName;

        var hajj = (await (await me.PostAsJsonAsync("/api/posts", new CreatePostRequest("", LifeEvent: "hajj", LifeEventDate: new DateOnly(2025, 6, 4)), Json))
            .Content.ReadFromJsonAsync<PostDto>(Json))!;
        Assert.Equal("hajj", hajj.LifeEvent);
        Assert.Equal(new DateOnly(2025, 6, 4), hajj.LifeEventDate);
        var normal = (await (await me.PostAsJsonAsync("/api/posts", new CreatePostRequest("Ordinary day"), Json)).Content.ReadFromJsonAsync<PostDto>(Json))!;

        var life = (await me.GetFromJsonAsync<PagedResult<PostDto>>($"/api/users/{name}/posts?filter=life", Json))!;
        Assert.Equal(hajj.Id, Assert.Single(life.Items).Id);

        (await me.PostAsync($"/api/users/me/pin/{hajj.Id}", null)).EnsureSuccessStatusCode();
        var profile = (await me.GetFromJsonAsync<ProfileDto>($"/api/users/{name}", Json))!;
        Assert.Equal(hajj.Id, profile.PinnedPostId);
        // The pinned post is shown on its own, not repeated in the list.
        var timeline = (await me.GetFromJsonAsync<PagedResult<PostDto>>($"/api/users/{name}/posts", Json))!;
        Assert.DoesNotContain(timeline.Items, p => p.Id == hajj.Id);
        Assert.Contains(timeline.Items, p => p.Id == normal.Id);
        Assert.True((await me.GetFromJsonAsync<PostDto>($"/api/posts/{hajj.Id}", Json))!.IsPinned);

        Assert.Empty((await me.GetFromJsonAsync<List<TimelineMediaDto>>($"/api/users/{name}/media?type=Image", Json))!);
        Assert.NotNull(await me.GetFromJsonAsync<List<PostDto>>("/api/memories", Json));
    }

    [Fact]
    public async Task Multilingual_SettingsDetectionAndTranslation()
    {
        var (c, _) = await SignUpWithId("polyglot");
        var u = (await (await c.PutAsJsonAsync("/api/users/me/language", new LanguageSettingsRequest("ar-SA", true), Json)).Content.ReadFromJsonAsync<UserDto>(Json))!;
        Assert.Equal("ar", u.Language);

        var post = (await (await c.PostAsJsonAsync("/api/posts", new CreatePostRequest("السلام عليكم ورحمة الله", TranslateForUmmah: true), Json))
            .Content.ReadFromJsonAsync<PostDto>(Json))!;
        Assert.Equal("ar", post.OriginalLanguage);

        // Without an AI key the original text comes back, flagged as not translated.
        var t = (await c.GetFromJsonAsync<TranslatedTextDto>($"/api/posts/{post.Id}/translation?lang=en", Json))!;
        Assert.False(t.UsedAi);

        var ui = (await (await factory.CreateClient().PostAsJsonAsync("/api/i18n/translate", new TranslateUiRequest("en", ["Home", "Friends"]), Json))
            .Content.ReadFromJsonAsync<Dictionary<string, string>>(Json))!;
        Assert.Equal("Home", ui["Home"]);
        Assert.Equal(19, (await factory.CreateClient().GetFromJsonAsync<List<LanguageInfo>>("/api/i18n/languages", Json))!.Count);
    }
}

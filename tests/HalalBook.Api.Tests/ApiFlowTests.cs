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
        var res = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(name, email, "Bismillah-123", name));
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
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(name, $"{name}@example.com", "Bismillah-123", name));
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
}

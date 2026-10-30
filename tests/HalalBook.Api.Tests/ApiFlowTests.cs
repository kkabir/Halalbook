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
}

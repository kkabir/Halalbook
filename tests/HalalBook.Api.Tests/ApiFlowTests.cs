using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HalalBook.Shared;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HalalBook.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"halalbook-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_db}");
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("Jwt:Key", "test-key-" + new string('x', 48));
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
        var res = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(name, $"{name}@example.com", "Bismillah-123", name));
        res.EnsureSuccessStatusCode();
        var auth = (await res.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
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
}

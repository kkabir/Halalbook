using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HalalBook.Shared;
using Microsoft.AspNetCore.Components.Forms;

namespace HalalBook.Web.Services;

public class ApiException(string message, int status) : Exception(message)
{
    public int Status { get; } = status;
}

/// <summary>Thin typed wrapper over the HalalBook REST API. Adds the signed-in user's bearer token.</summary>
public class ApiClient(HttpClient http, AuthSession session)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public Uri BaseAddress => http.BaseAddress!;

    private HttpRequestMessage Build(HttpMethod method, string url, object? body = null)
    {
        var req = new HttpRequestMessage(method, url.TrimStart('/'));
        if (session.Token is { } token) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) req.Content = JsonContent.Create(body, body.GetType(), options: Json);
        return req;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req)
    {
        var res = await http.SendAsync(req);
        if (res.IsSuccessStatusCode) return res;
        if ((int)res.StatusCode == 401 && session.IsAuthenticated) await session.SignOutAsync();

        string message = res.ReasonPhrase ?? "Something went wrong.";
        try
        {
            var problem = await res.Content.ReadFromJsonAsync<JsonElement>(Json);
            if (problem.TryGetProperty("errors", out var errors))
                message = string.Join(" ", errors.EnumerateObject().SelectMany(e => e.Value.EnumerateArray().Select(v => v.GetString())));
            else if (problem.TryGetProperty("detail", out var detail) && detail.GetString() is { Length: > 0 } d) message = d;
            else if (problem.TryGetProperty("title", out var title)) message = title.GetString() ?? message;
        }
        catch (JsonException) { }
        throw new ApiException(message, (int)res.StatusCode);
    }

    public async Task<T> GetAsync<T>(string url)
    {
        using var res = await SendAsync(Build(HttpMethod.Get, url));
        return (await res.Content.ReadFromJsonAsync<T>(Json))!;
    }

    public async Task<T> PostAsync<T>(string url, object? body = null)
    {
        using var res = await SendAsync(Build(HttpMethod.Post, url, body ?? new { }));
        return (await res.Content.ReadFromJsonAsync<T>(Json))!;
    }

    public async Task PostAsync(string url, object? body = null)
    {
        using var _ = await SendAsync(Build(HttpMethod.Post, url, body ?? new { }));
    }

    public async Task<T> PutAsync<T>(string url, object body)
    {
        using var res = await SendAsync(Build(HttpMethod.Put, url, body));
        return (await res.Content.ReadFromJsonAsync<T>(Json))!;
    }

    public async Task PutAsync(string url, object body)
    {
        using var _ = await SendAsync(Build(HttpMethod.Put, url, body));
    }

    public async Task DeleteAsync(string url)
    {
        using var _ = await SendAsync(Build(HttpMethod.Delete, url));
    }

    public async Task<UploadResult> UploadAsync(IBrowserFile file, long maxBytes = 100_000_000)
    {
        using var content = new MultipartFormDataContent();
        var stream = new StreamContent(file.OpenReadStream(maxBytes));
        stream.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType);
        content.Add(stream, "file", file.Name);
        var req = Build(HttpMethod.Post, "api/media");
        req.Content = content;
        using var res = await SendAsync(req);
        return (await res.Content.ReadFromJsonAsync<UploadResult>(Json))!;
    }
}

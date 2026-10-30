using System.Net.Http.Json;
using System.Threading.Channels;

namespace HalalBook.Api.Services;

/// <summary>Search-engine settings. The IndexNow key is public by design: the web app serves it at /{key}.txt.</summary>
public class SeoOptions
{
    public string SiteUrl { get; set; } = "https://halalbook.app";
    public string? IndexNowKey { get; set; }
    public bool IsEnabled => !string.IsNullOrWhiteSpace(IndexNowKey);
}

/// <summary>Public pages waiting to be announced to IndexNow search engines (Bing, Yandex, Naver, Seznam, Yep...).</summary>
public class IndexNowQueue
{
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>();
    public void Enqueue(string path) => _channel.Writer.TryWrite(path);
    public ChannelReader<string> Reader => _channel.Reader;
}

/// <summary>Sends queued URLs to IndexNow in small batches, so new public posts show up in search within minutes.</summary>
public class IndexNowWorker(IndexNowQueue queue, SeoOptions seo, IHttpClientFactory http, IHostEnvironment env, ILogger<IndexNowWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await queue.Reader.WaitToReadAsync(stoppingToken))
        {
            // Gather what arrives in the next 30 seconds into one request.
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            var paths = new HashSet<string>();
            while (paths.Count < 1000 && queue.Reader.TryRead(out var p)) paths.Add(p);
            // Only the live site announces itself; development and tests never do.
            if (!seo.IsEnabled || !env.IsProduction() || paths.Count == 0) continue;
            try { await SubmitAsync(http.CreateClient(), seo, paths, stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "IndexNow submission failed"); }
        }
    }

    public static async Task<int> SubmitAsync(HttpClient client, SeoOptions seo, IEnumerable<string> paths, CancellationToken ct = default)
    {
        var site = new Uri(seo.SiteUrl.TrimEnd('/') + "/");
        var body = new
        {
            host = site.Host,
            key = seo.IndexNowKey,
            keyLocation = new Uri(site, $"{seo.IndexNowKey}.txt").ToString(),
            urlList = paths.Select(p => new Uri(site, p.TrimStart('/')).ToString()).ToList(),
        };
        using var res = await client.PostAsJsonAsync("https://api.indexnow.org/indexnow", body, ct);
        return (int)res.StatusCode;
    }
}

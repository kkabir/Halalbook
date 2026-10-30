using System.Net.Http.Json;
using System.Security;
using System.Text;

namespace HalalBook.Web.Services;

/// <summary>robots.txt and sitemap.xml, so every search engine can find the public pages and shared public posts.</summary>
public static class SearchEngineFiles
{
    /// <summary>Pages anyone can read without signing in, with how often they change.</summary>
    private static readonly (string Path, string Change, string Priority)[] PublicPages =
    [
        ("/welcome", "weekly", "1.0"),
        ("/register", "monthly", "0.8"),
        ("/login", "monthly", "0.6"),
        ("/terms", "monthly", "0.4"),
        ("/contact", "monthly", "0.4"),
    ];

    private record SitemapPost(Guid Id, DateTime Modified);

    public static void MapSearchEngineFiles(this IEndpointRouteBuilder app)
    {
        app.MapGet("/robots.txt", (IConfiguration config) =>
        {
            var site = SiteUrl(config);
            return Results.Text($"""
                User-agent: *
                Allow: /welcome
                Allow: /p/
                Allow: /register
                Allow: /login
                Allow: /terms
                Allow: /contact
                Disallow: /admin
                Disallow: /moderation
                Disallow: /messages
                Disallow: /settings
                Disallow: /notifications
                Disallow: /call/
                Disallow: /_blazor
                Disallow: /_framework

                Sitemap: {site}/sitemap.xml
                """, "text/plain");
        });

        app.MapGet("/sitemap.xml", async (IConfiguration config, IHttpClientFactory http, ILoggerFactory logs) =>
        {
            var site = SiteUrl(config);
            var xml = new StringBuilder("""<?xml version="1.0" encoding="UTF-8"?>""").Append('\n')
                .Append("""<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">""").Append('\n');
            foreach (var (path, change, priority) in PublicPages)
                xml.Append($"<url><loc>{site}{path}</loc><changefreq>{change}</changefreq><priority>{priority}</priority></url>\n");
            try
            {
                var posts = await http.CreateClient("api").GetFromJsonAsync<List<SitemapPost>>("api/public/sitemap") ?? [];
                foreach (var p in posts)
                    xml.Append($"<url><loc>{site}/p/{p.Id}</loc><lastmod>{p.Modified:yyyy-MM-dd}</lastmod><priority>0.5</priority></url>\n");
            }
            catch (Exception ex)
            {
                // The fixed pages still go out when the API can't be reached.
                logs.CreateLogger("Sitemap").LogWarning(ex, "Couldn't load public posts for the sitemap");
            }
            xml.Append("</urlset>");
            return Results.Text(xml.ToString(), "application/xml", Encoding.UTF8);
        });
    }

    public static string SiteUrl(IConfiguration config) => SecurityElement.Escape((config["Seo:SiteUrl"] ?? "https://halalbook.app").TrimEnd('/'));
}

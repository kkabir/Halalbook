using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using HalalBook.Api.Data;
using HalalBook.Api.Endpoints;
using HalalBook.Api.Hubs;
using HalalBook.Api.Services;
using HalalBook.Shared;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------- Database: SQLite locally, SQL Server in production (SmarterASP.net) ----------
builder.Services.AddDbContext<AppDbContext>(o =>
{
    var provider = config["Database:Provider"] ?? "Sqlite";
    var cs = config.GetConnectionString("Default");
    if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)) o.UseSqlServer(cs, s => s.EnableRetryOnFailure());
    else o.UseSqlite(cs ?? "Data Source=halalbook.db");
});

// ---------- Options & services ----------
var jwt = config.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwt.Key) || jwt.Key.Length < 32)
    throw new InvalidOperationException("Jwt:Key must be set to a random string of at least 32 characters (user-secrets, appsettings or environment).");
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton(config.GetSection("Ai").Get<AiOptions>() ?? new AiOptions());
builder.Services.AddSingleton(config.GetSection("Moderation").Get<ModerationOptions>() ?? new ModerationOptions());
builder.Services.AddSingleton<ContentModerator>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<PostService>();
builder.Services.AddScoped<Notifier>();
builder.Services.AddHttpClient<AiService>(c => c.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddSingleton(config.GetSection("Email").Get<EmailOptions>() ?? new EmailOptions());
builder.Services.AddSingleton(config.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions());
builder.Services.AddSingleton<EmailTemplates>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<OtpService>();
builder.Services.AddSingleton<PresenceTracker>();
var mediaOptions = config.GetSection("Media").Get<MediaOptions>() ?? new MediaOptions();
builder.Services.AddSingleton(mediaOptions);
builder.Services.AddSingleton<MediaPolicy>();
builder.Services.AddSingleton<HalalBook.Api.Hubs.LiveRoomTracker>();
builder.Services.AddSingleton(config.GetSection("Live").Get<LiveOptions>() ?? new LiveOptions());
builder.Services.AddScoped<Mailer>();

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Request bodies are capped just above the largest allowed video, so oversized uploads are cut off early.
var maxBody = (long)(Math.Max(Math.Max(mediaOptions.MaxVideoMB, mediaOptions.MaxImageMB), 1) * 1_000_000) + 1_000_000;
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = maxBody);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = maxBody);
builder.Services.AddOpenApi(o => o.AddDocumentTransformer<HalalBook.Api.OpenApiDocumentInfo>());
builder.Services.AddProblemDetails();

// ---------- Auth ----------
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            NameClaimType = System.Security.Claims.ClaimTypes.Name,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
        };
        // SignalR sends the token in the query string for WebSockets.
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs")) ctx.Token = token;
                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("moderator", p => p.RequireRole(nameof(UserRole.Moderator), nameof(UserRole.Admin)));
builder.Services.AddSingleton<Microsoft.AspNetCore.SignalR.IUserIdProvider, NameIdentifierUserIdProvider>();

// ---------- CORS (halalbook.app + local dev) ----------
var origins = config.GetSection("Cors:Origins").Get<string[]>() ?? ["https://halalbook.app", "https://www.halalbook.app"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

// ---------- Rate limiting for AI calls ----------
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("ai", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.Identity?.Name ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("upload", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.Identity?.Name ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = mediaOptions.UploadsPerMinute, Window = TimeSpan.FromMinutes(1) }));
    // Sign-in, codes and password resets: slows down guessing and email flooding.
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = config.GetValue("Auth:RequestsPerMinute", 20), Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // SQL Server (production) evolves through EF migrations; local SQLite is created directly.
    if (db.Database.IsSqlServer()) await db.Database.MigrateAsync();
    else await db.Database.EnsureCreatedAsync();
    if (app.Environment.IsDevelopment() && config.GetValue<bool>("Seed:Enabled"))
        await SeedData.RunAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>(), config["Seed:Password"] ?? "");
}

app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto });
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpsRedirection();
// Uploaded photos and videos. File names are random GUIDs, so they can be cached forever; range requests let videos seek.
var uploadsRoot = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "uploads");
Directory.CreateDirectory(uploadsRoot);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadsRoot),
    RequestPath = "/uploads",
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        ctx.Context.Response.Headers.XContentTypeOptions = "nosniff";
        ctx.Context.Response.Headers.AccessControlAllowOrigin = "*";
    },
});
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapOpenApi();
// Swagger UI on the API's home page (https://api.halalbook.app/). Use "Authorize" with a token from /api/auth/login.
app.UseSwaggerUI(o =>
{
    o.SwaggerEndpoint("/openapi/v1.json", "HalalBook API v1");
    o.RoutePrefix = "";
    o.DocumentTitle = "HalalBook API";
    o.EnablePersistAuthorization();
    o.DisplayRequestDuration();
});
app.MapGet("/health", () => Results.Ok("healthy")).ExcludeFromDescription();
app.MapUserEndpoints();
app.MapPostEndpoints();
app.MapCommunityEndpoints();
app.MapMessagingEndpoints();
app.MapPlatformEndpoints();
app.MapPresenceEndpoints();
app.MapLiveEndpoints();
app.MapHub<RealtimeHub>("/hubs/realtime");
app.MapHub<LiveHub>("/hubs/live");

app.Run();

public partial class Program;

internal sealed class NameIdentifierUserIdProvider : Microsoft.AspNetCore.SignalR.IUserIdProvider
{
    public string? GetUserId(Microsoft.AspNetCore.SignalR.HubConnectionContext connection) =>
        connection.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
}

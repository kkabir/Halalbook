using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HalalBook.Api.Data;
using HalalBook.Api.Hubs;
using HalalBook.Shared;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;

namespace HalalBook.Api.Services;

public class JwtOptions
{
    public string Issuer { get; set; } = "https://api.halalbook.app";
    public string Audience { get; set; } = "halalbook";
    public string Key { get; set; } = "";
    public int ExpiryDays { get; set; } = 30;
}

public class TokenService(JwtOptions options)
{
    public (string Token, DateTime ExpiresAt) Create(User user)
    {
        var expires = DateTime.UtcNow.AddDays(options.ExpiryDays);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.UserName),
            new Claim("display_name", user.DisplayName),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
        };
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(options.Issuer, options.Audience, claims, expires: expires, signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}

public static class ClaimsExtensions
{
    public static Guid UserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    public static bool IsModerator(this ClaimsPrincipal user) =>
        user.IsInRole(nameof(UserRole.Moderator)) || user.IsInRole(nameof(UserRole.Admin));
}

/// <summary>Stores a notification and pushes it to the recipient's live connections.</summary>
public class Notifier(AppDbContext db, IHubContext<RealtimeHub> hub, PushQueue push)
{
    public async Task SendAsync(Guid userId, Guid? actorId, NotificationType type, string message, string? link)
    {
        if (actorId == userId) return;
        var n = new Notification { UserId = userId, ActorId = actorId, Type = type, Message = message, Link = link };
        db.Notifications.Add(n);
        await db.SaveChangesAsync();
        var actor = actorId is null ? null : await db.Users.FindAsync(actorId);
        await hub.Clients.User(userId.ToString()).SendAsync("notification",
            new NotificationDto(n.Id, n.Type, actor?.ToSummary(), n.Message, n.Link, false, n.CreatedAt));
        push.Enqueue(userId, "HalalBook", message, link ?? "/notifications", $"n-{type}", senderId: actorId);
    }
}

public static class Mapping
{
    public static UserSummaryDto ToSummary(this User u) => new(u.Id, u.UserName, u.DisplayName, u.AvatarUrl, u.IsVerified);

    public static UserDto ToDto(this User u) => new(u.Id, u.UserName, u.Email, u.DisplayName, u.Bio, u.AvatarUrl, u.CoverUrl,
        u.Location, u.Website, u.Role, u.IsVerified, u.CreatedAt, u.Latitude, u.Longitude, u.PrayerMethod, u.AsrSchool,
        u.EmailConfirmed, u.TwoFactorEnabled, u.ShowOnlineStatus, u.WhoCanPost, u.AcceptedTermsVersion,
        new PrivacySettingsDto(u.WhoCanMessage, u.WhoCanSendFriendRequests, u.WhoCanSeeFriendsList, u.WhoCanFindMe, u.WhoCanFollow),
        u.Language, u.AutoTranslate, u.FaithFeatures,
        new NotificationSettingsDto(u.NotifyShowPreview, u.NotifyDisappearing, u.DoNotDisturb, u.VipsBreakThrough));

    public static GroupSummaryDto ToSummary(this Group g) => new(g.Id, g.Name, g.CoverUrl);
    public static PageSummaryDto ToSummary(this Page p) => new(p.Id, p.Name, p.Handle, p.AvatarUrl);
}

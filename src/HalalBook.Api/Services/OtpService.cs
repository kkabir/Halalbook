using System.Security.Cryptography;
using System.Text;
using HalalBook.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace HalalBook.Api.Services;

/// <summary>Issues and checks 6-digit email codes. Codes are stored hashed, expire, and lock after too many attempts.</summary>
public class OtpService(AppDbContext db)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
    public const int MaxAttempts = 5;

    public enum Result { Ok, Invalid, Expired, TooManyAttempts }

    /// <summary>Creates a new code (invalidating older ones), or returns null while the resend cooldown is active.</summary>
    public async Task<string?> IssueAsync(Guid userId, EmailCodePurpose purpose)
    {
        var now = DateTime.UtcNow;
        var latest = await db.EmailCodes.Where(c => c.UserId == userId && c.Purpose == purpose)
            .OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync();
        if (latest is not null && latest.ConsumedAt is null && now - latest.CreatedAt < ResendCooldown) return null;

        await db.EmailCodes.Where(c => c.UserId == userId && c.Purpose == purpose && c.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now));

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        db.EmailCodes.Add(new EmailCode { UserId = userId, Purpose = purpose, CodeHash = Hash(userId, purpose, code), ExpiresAt = now + Lifetime });
        await db.SaveChangesAsync();
        return code;
    }

    public async Task<Result> VerifyAsync(Guid userId, EmailCodePurpose purpose, string? code)
    {
        var now = DateTime.UtcNow;
        var entry = await db.EmailCodes.Where(c => c.UserId == userId && c.Purpose == purpose && c.ConsumedAt == null)
            .OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync();
        if (entry is null) return Result.Invalid;
        if (entry.ExpiresAt < now) return Result.Expired;
        if (entry.Attempts >= MaxAttempts) return Result.TooManyAttempts;

        var expected = Convert.FromHexString(entry.CodeHash);
        var actual = Convert.FromHexString(Hash(userId, purpose, (code ?? "").Trim()));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            entry.Attempts++;
            await db.SaveChangesAsync();
            return entry.Attempts >= MaxAttempts ? Result.TooManyAttempts : Result.Invalid;
        }
        entry.ConsumedAt = now;
        await db.SaveChangesAsync();
        return Result.Ok;
    }

    public static string Message(Result r) => r switch
    {
        Result.Expired => "That code has expired. Request a new one.",
        Result.TooManyAttempts => "Too many wrong attempts. Request a new code.",
        _ => "That code isn't right. Check the email and try again.",
    };

    private static string Hash(Guid userId, EmailCodePurpose purpose, string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{userId:N}:{purpose}:{code}")));
}

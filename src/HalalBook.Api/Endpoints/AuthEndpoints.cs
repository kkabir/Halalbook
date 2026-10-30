using System.Security.Claims;
using System.Text.RegularExpressions;
using HalalBook.Api.Data;
using HalalBook.Api.Services;
using HalalBook.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HalalBook.Api.Endpoints;

public class AuthOptions
{
    /// <summary>When true, new accounts must enter the emailed code before they can sign in.</summary>
    public bool RequireConfirmedEmail { get; set; } = true;
}

/// <summary>Sign-up, sign-in, email verification, two-step codes and password reset.</summary>
public static partial class AuthEndpoints
{
    private const int MinPassword = 8;

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Auth").RequireRateLimiting("auth");

        auth.MapPost("/register", async (RegisterRequest req, AppDbContext db, TokenService tokens, IPasswordHasher<User> hasher,
            AuthOptions options, Mailer mailer, Notifier notifier) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (!UserNameRegex().IsMatch(req.UserName ?? "")) errors["userName"] = ["Use 3-32 letters, numbers, dots or underscores."];
            if (!EmailRegex().IsMatch(req.Email?.Trim() ?? "")) errors["email"] = ["Enter a valid email."];
            if ((req.Password ?? "").Length < MinPassword) errors["password"] = [$"Use at least {MinPassword} characters."];
            if (string.IsNullOrWhiteSpace(req.DisplayName)) errors["displayName"] = ["Enter your name."];
            if (req.AcceptedTermsVersion != Terms.Version) errors["terms"] = ["Please read and accept the Community Agreement."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var nu = req.UserName!.ToUpperInvariant();
            var ne = req.Email!.Trim().ToUpperInvariant();
            if (await db.Users.AnyAsync(u => u.NormalizedUserName == nu)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["userName"] = ["That username is taken."] });
            if (await db.Users.AnyAsync(u => u.NormalizedEmail == ne)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["That email is already registered. Try signing in or resetting your password."] });

            var user = new User
            {
                UserName = req.UserName!, NormalizedUserName = nu, Email = req.Email.Trim(), NormalizedEmail = ne,
                DisplayName = req.DisplayName.Trim(),
                Role = await db.Users.AnyAsync() ? UserRole.User : UserRole.Admin, // first account administers the site
                EmailConfirmed = !options.RequireConfirmedEmail,
                AcceptedTermsVersion = Terms.Version, AcceptedTermsAt = DateTime.UtcNow,
            };
            user.PasswordHash = hasher.HashPassword(user, req.Password!);
            db.Users.Add(user);
            // Joined through a friend's invite link: they become friends straight away.
            var inviter = string.IsNullOrWhiteSpace(req.InviteCode) ? null
                : await db.Users.FirstOrDefaultAsync(u => u.InviteCode == req.InviteCode.Trim() && !u.IsSuspended);
            if (inviter is not null)
            {
                user.InvitedById = inviter.Id;
                db.Friendships.Add(new Friendship { RequesterId = inviter.Id, AddresseeId = user.Id, Accepted = true });
            }
            await db.SaveChangesAsync();
            if (inviter is not null)
                await notifier.SendAsync(inviter.Id, user.Id, NotificationType.FriendAccepted,
                    $"{user.DisplayName} joined HalalBook from your invite. You're now friends! 🎉", $"/u/{user.UserName}");

            if (options.RequireConfirmedEmail)
                return Results.Ok(await mailer.SendCodeAsync(user, EmailCodePurpose.VerifyEmail));

            var (token, exp) = tokens.Create(user);
            return Results.Ok(new AuthResult(AuthStep.Done, new AuthResponse(token, exp, user.ToDto()), user.Email, null));
        });

        auth.MapPost("/login", async (LoginRequest req, AppDbContext db, TokenService tokens, IPasswordHasher<User> hasher,
            AuthOptions options, Mailer mailer) =>
        {
            var key = (req.UserNameOrEmail ?? "").Trim().ToUpperInvariant();
            var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedUserName == key || u.NormalizedEmail == key);
            if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password ?? "") == PasswordVerificationResult.Failed)
                return Results.Problem("Incorrect username or password.", statusCode: 401);
            if (user.IsSuspended) return Results.Problem("This account is suspended.", statusCode: 403);

            if (options.RequireConfirmedEmail && !user.EmailConfirmed)
                return Results.Ok(await mailer.SendCodeAsync(user, EmailCodePurpose.VerifyEmail));
            if (user.TwoFactorEnabled)
                return Results.Ok(await mailer.SendCodeAsync(user, EmailCodePurpose.Login));

            var (token, exp) = tokens.Create(user);
            return Results.Ok(new AuthResult(AuthStep.Done, new AuthResponse(token, exp, user.ToDto()), user.Email, null));
        });

        auth.MapPost("/verify-email", async (VerifyCodeRequest req, AppDbContext db, OtpService otp, TokenService tokens, Mailer mailer) =>
        {
            var user = await FindByEmail(db, req.Email);
            if (user is null) return Results.Problem(OtpService.Message(OtpService.Result.Invalid), statusCode: 400);
            var result = await otp.VerifyAsync(user.Id, EmailCodePurpose.VerifyEmail, req.Code);
            if (result != OtpService.Result.Ok) return Results.Problem(OtpService.Message(result), statusCode: 400);

            user.EmailConfirmed = true;
            await db.SaveChangesAsync();
            await mailer.SendAsync(t => t.Welcome(user.Email, user.DisplayName));
            var (token, exp) = tokens.Create(user);
            return Results.Ok(new AuthResult(AuthStep.Done, new AuthResponse(token, exp, user.ToDto()), user.Email, "Your email is confirmed. Welcome!"));
        });

        auth.MapPost("/login-code", async (VerifyCodeRequest req, AppDbContext db, OtpService otp, TokenService tokens) =>
        {
            var user = await FindByEmail(db, req.Email);
            if (user is null) return Results.Problem(OtpService.Message(OtpService.Result.Invalid), statusCode: 400);
            var result = await otp.VerifyAsync(user.Id, EmailCodePurpose.Login, req.Code);
            if (result != OtpService.Result.Ok) return Results.Problem(OtpService.Message(result), statusCode: 400);
            var (token, exp) = tokens.Create(user);
            return Results.Ok(new AuthResult(AuthStep.Done, new AuthResponse(token, exp, user.ToDto()), user.Email, null));
        });

        auth.MapPost("/resend-code", async (ResendCodeRequest req, AppDbContext db, Mailer mailer) =>
        {
            var user = await FindByEmail(db, req.Email);
            if (user is not null)
            {
                if (req.Step == AuthStep.VerifyEmail && !user.EmailConfirmed)
                    await mailer.SendCodeAsync(user, EmailCodePurpose.VerifyEmail);
                // Only re-send a sign-in code when a sign-in with the right password is already in progress.
                else if (req.Step == AuthStep.LoginCode && await db.EmailCodes.AnyAsync(c =>
                             c.UserId == user.Id && c.Purpose == EmailCodePurpose.Login && c.CreatedAt > DateTime.UtcNow.AddMinutes(-15)))
                    await mailer.SendCodeAsync(user, EmailCodePurpose.Login);
            }
            return Results.Ok(new AuthResult(req.Step, null, req.Email, "If the address is registered, a new code is on its way."));
        });

        auth.MapPost("/forgot-password", async (EmailRequest req, AppDbContext db, Mailer mailer) =>
        {
            // Same answer whether or not the address exists, so accounts can't be discovered.
            if (await FindByEmail(db, req.Email) is { IsSuspended: false } user)
                await mailer.SendCodeAsync(user, EmailCodePurpose.ResetPassword);
            return Results.Ok(new AuthResult(AuthStep.Done, null, req.Email, "If that email has an account, we've sent a reset code."));
        });

        auth.MapPost("/reset-password", async (ResetPasswordRequest req, AppDbContext db, OtpService otp, IPasswordHasher<User> hasher, Mailer mailer) =>
        {
            if ((req.NewPassword ?? "").Length < MinPassword)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["newPassword"] = [$"Use at least {MinPassword} characters."] });
            var user = await FindByEmail(db, req.Email);
            if (user is null) return Results.Problem(OtpService.Message(OtpService.Result.Invalid), statusCode: 400);
            var result = await otp.VerifyAsync(user.Id, EmailCodePurpose.ResetPassword, req.Code);
            if (result != OtpService.Result.Ok) return Results.Problem(OtpService.Message(result), statusCode: 400);

            user.PasswordHash = hasher.HashPassword(user, req.NewPassword!);
            user.EmailConfirmed = true; // receiving the code proves they own the address
            await db.SaveChangesAsync();
            await mailer.SendAsync(t => t.PasswordChanged(user.Email, user.DisplayName));
            return Results.Ok(new AuthResult(AuthStep.Done, null, user.Email, "Your password has been reset. You can sign in now."));
        });

        // ---- Signed-in account security ----
        var me = app.MapGroup("/api/users/me").WithTags("Users").RequireAuthorization();

        me.MapPut("/password", async (ChangePasswordRequest req, ClaimsPrincipal cp, AppDbContext db, IPasswordHasher<User> hasher, Mailer mailer) =>
        {
            var user = await db.Users.FindAsync(cp.UserId());
            if (user is null) return Results.Unauthorized();
            if (hasher.VerifyHashedPassword(user, user.PasswordHash, req.CurrentPassword ?? "") == PasswordVerificationResult.Failed)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["currentPassword"] = ["Your current password is incorrect."] });
            if ((req.NewPassword ?? "").Length < MinPassword)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["newPassword"] = [$"Use at least {MinPassword} characters."] });
            user.PasswordHash = hasher.HashPassword(user, req.NewPassword!);
            await db.SaveChangesAsync();
            await mailer.SendAsync(t => t.PasswordChanged(user.Email, user.DisplayName));
            return Results.Ok();
        });

        me.MapPost("/accept-terms", async (AcceptTermsRequest req, ClaimsPrincipal cp, AppDbContext db) =>
        {
            if (req.Version != Terms.Version) return Results.Problem("Please read the latest Community Agreement.", statusCode: 400);
            var user = await db.Users.FindAsync(cp.UserId());
            if (user is null) return Results.Unauthorized();
            user.AcceptedTermsVersion = Terms.Version;
            user.AcceptedTermsAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(user.ToDto());
        });

        me.MapPut("/two-factor", async (TwoFactorRequest req, ClaimsPrincipal cp, AppDbContext db) =>
        {
            var user = await db.Users.FindAsync(cp.UserId());
            if (user is null) return Results.Unauthorized();
            if (req.Enabled && !user.EmailConfirmed) return Results.Problem("Confirm your email before turning on two-step sign-in.", statusCode: 400);
            user.TwoFactorEnabled = req.Enabled;
            await db.SaveChangesAsync();
            return Results.Ok(user.ToDto());
        });
    }

    private static Task<User?> FindByEmail(AppDbContext db, string? email)
    {
        var key = (email ?? "").Trim().ToUpperInvariant();
        return db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == key || u.NormalizedUserName == key);
    }

    [GeneratedRegex(@"^[A-Za-z0-9_\.]{3,32}$")] private static partial Regex UserNameRegex();
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")] private static partial Regex EmailRegex();
}

/// <summary>Sends account emails. Failures are logged, never shown as a crash to the person signing in.</summary>
public class Mailer(IEmailSender sender, EmailTemplates templates, OtpService otp, ILogger<Mailer> log)
{
    public async Task SendAsync(Func<EmailTemplates, EmailMessage> build)
    {
        var message = build(templates);
        try { await sender.SendAsync(message); }
        catch (Exception ex) { log.LogError(ex, "Failed to send \"{Subject}\" to {To}", message.Subject, message.To); }
    }

    /// <summary>Issues a code for the purpose and emails it; returns what the client should show next.</summary>
    public async Task<AuthResult> SendCodeAsync(User user, EmailCodePurpose purpose)
    {
        var step = purpose == EmailCodePurpose.Login ? AuthStep.LoginCode : AuthStep.VerifyEmail;
        var code = await otp.IssueAsync(user.Id, purpose);
        if (code is not null)
        {
            await SendAsync(t => purpose switch
            {
                EmailCodePurpose.VerifyEmail => t.VerifyEmail(user.Email, user.DisplayName, code),
                EmailCodePurpose.ResetPassword => t.PasswordReset(user.Email, user.DisplayName, code),
                _ => t.LoginCode(user.Email, user.DisplayName, code),
            });
        }
        var masked = Mask(user.Email);
        return new AuthResult(step, null, user.Email, code is null
            ? $"We already sent a code to {masked}. You can request another in a minute."
            : $"We sent a 6-digit code to {masked}.");
    }

    private static string Mask(string email)
    {
        var at = email.IndexOf('@');
        return at <= 1 ? email : email[0] + new string('•', Math.Min(at - 1, 6)) + email[at..];
    }
}

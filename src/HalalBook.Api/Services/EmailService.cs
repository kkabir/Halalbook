using System.Net;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace HalalBook.Api.Services;

public class EmailOptions
{
    public string FromAddress { get; set; } = "noreply@halalbook.app";
    public string FromName { get; set; } = "HalalBook";
    public string? ReplyTo { get; set; } = "support@halalbook.app";
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    /// <summary>Auto, StartTls, SslOnConnect or None.</summary>
    public string Security { get; set; } = "Auto";
    public string? UserName { get; set; }
    public string? Password { get; set; }
    /// <summary>Used when SmtpHost is empty (local dev): emails are saved here as .eml files instead of sent.</summary>
    public string PickupDirectory { get; set; } = "emails";
    public string WebsiteUrl { get; set; } = "https://halalbook.app";
}

public record EmailMessage(string To, string ToName, string Subject, string Html, string Text);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

/// <summary>Sends through SMTP when configured; otherwise drops .eml files into a pickup folder for local testing.</summary>
public class SmtpEmailSender(EmailOptions options, IWebHostEnvironment env, ILogger<SmtpEmailSender> log) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(options.FromName, options.FromAddress));
        mime.To.Add(new MailboxAddress(message.ToName, message.To));
        if (!string.IsNullOrWhiteSpace(options.ReplyTo)) mime.ReplyTo.Add(MailboxAddress.Parse(options.ReplyTo));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.Html, TextBody = message.Text }.ToMessageBody();

        if (string.IsNullOrWhiteSpace(options.SmtpHost))
        {
            var dir = Path.Combine(env.ContentRootPath, options.PickupDirectory);
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.eml");
            await mime.WriteToAsync(file, ct);
            log.LogInformation("SMTP not configured; saved email \"{Subject}\" for {To} to {File}", message.Subject, message.To, file);
            return;
        }

        using var smtp = new SmtpClient();
        var security = Enum.TryParse<SecureSocketOptions>(options.Security, true, out var s) ? s : SecureSocketOptions.Auto;
        await smtp.ConnectAsync(options.SmtpHost, options.SmtpPort, security, ct);
        if (!string.IsNullOrEmpty(options.UserName)) await smtp.AuthenticateAsync(options.UserName, options.Password ?? "", ct);
        await smtp.SendAsync(mime, ct);
        await smtp.DisconnectAsync(true, ct);
    }
}

/// <summary>Branded HalalBook email templates (emerald and gold, works in all major mail clients).</summary>
public class EmailTemplates(EmailOptions options)
{
    private string Layout(string preheader, string heading, string bodyHtml)
    {
        var site = options.WebsiteUrl.TrimEnd('/');
        return $$"""
        <!DOCTYPE html>
        <html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
        <body style="margin:0;background:#fbf8f1;font-family:'Segoe UI',Arial,sans-serif;color:#17241f">
        <span style="display:none;max-height:0;overflow:hidden">{{WebUtility.HtmlEncode(preheader)}}</span>
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#fbf8f1;padding:32px 12px">
          <tr><td align="center">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px">
              <tr><td align="center" style="background:#0b3d2e;border-radius:140px 140px 0 0;padding:36px 24px 22px;border-bottom:4px solid #c9a227">
                <div style="font-size:30px;font-weight:700;color:#ffffff;letter-spacing:.5px">Halal<span style="color:#e8d48b">Book</span></div>
                <div style="color:#e8d48b;font-size:20px;margin-top:6px;font-family:'Times New Roman',serif">بِسْمِ ٱللَّٰهِ</div>
              </td></tr>
              <tr><td style="background:#ffffff;padding:32px 32px 28px;border:1px solid #e7dfcc;border-top:0">
                <h1 style="margin:0 0 16px;font-size:22px;color:#0b3d2e">{{WebUtility.HtmlEncode(heading)}}</h1>
                {{bodyHtml}}
              </td></tr>
              <tr><td align="center" style="padding:18px 12px;color:#7d8c85;font-size:12px;line-height:1.6">
                You're receiving this because of activity on your HalalBook account.<br>
                <a href="{{site}}" style="color:#0f5c45">{{site.Replace("https://", "")}}</a> · A beautiful home for the Ummah
              </td></tr>
            </table>
          </td></tr>
        </table>
        </body></html>
        """;
    }

    private static string P(string text) => $"<p style=\"margin:0 0 14px;font-size:15px;line-height:1.6\">{text}</p>";

    private static string CodeBox(string code) =>
        $"<div style=\"margin:22px 0;text-align:center\"><span style=\"display:inline-block;font-size:34px;letter-spacing:10px;font-weight:700;color:#0b3d2e;background:#f8f0d4;border:1px dashed #c9a227;border-radius:14px;padding:14px 24px\">{code}</span></div>";

    private string Button(string text, string path) =>
        $"<div style=\"margin:22px 0;text-align:center\"><a href=\"{options.WebsiteUrl.TrimEnd('/')}{path}\" style=\"display:inline-block;background:#0f5c45;color:#ffffff;text-decoration:none;font-weight:700;padding:12px 26px;border-radius:999px\">{text}</a></div>";

    public EmailMessage VerifyEmail(string to, string name, string code) => new(to, name,
        $"{code} is your HalalBook verification code",
        Layout($"Your code is {code}", $"Assalamu alaikum, {name}",
            P("Welcome to HalalBook! Use this code to confirm your email address and activate your account:") + CodeBox(code) +
            P("The code expires in 15 minutes. If you didn't sign up, you can safely ignore this email.")),
        $"Assalamu alaikum {name},\n\nYour HalalBook verification code is {code}. It expires in 15 minutes.\n\nIf you didn't sign up, ignore this email.");

    public EmailMessage Welcome(string to, string name) => new(to, name,
        "Welcome to HalalBook 🌙",
        Layout("Your account is active", $"Welcome, {name}!",
            P("Your email is confirmed and your account is active. Alhamdulillah!") +
            P("Set your location to get prayer times and the Qibla direction, add friends, and join groups that inspire you.") +
            Button("Open HalalBook", "/")),
        $"Welcome, {name}! Your HalalBook account is active. Visit {options.WebsiteUrl}");

    public EmailMessage PasswordReset(string to, string name, string code) => new(to, name,
        $"{code} is your HalalBook password reset code",
        Layout($"Reset code {code}", "Reset your password",
            P($"We received a request to reset the password for {WebUtility.HtmlEncode(to)}. Enter this code to choose a new password:") + CodeBox(code) +
            P("The code expires in 15 minutes. If you didn't ask for this, ignore this email: your password won't change.")),
        $"Your HalalBook password reset code is {code}. It expires in 15 minutes. If you didn't request it, ignore this email.");

    public EmailMessage LoginCode(string to, string name, string code) => new(to, name,
        $"{code} is your HalalBook sign-in code",
        Layout($"Sign-in code {code}", "Your sign-in code",
            P("Use this code to finish signing in to HalalBook:") + CodeBox(code) +
            P("It expires in 15 minutes. If this wasn't you, change your password right away.")),
        $"Your HalalBook sign-in code is {code}. It expires in 15 minutes.");

    public EmailMessage PasswordChanged(string to, string name) => new(to, name,
        "Your HalalBook password was changed",
        Layout("Password changed", "Your password was changed",
            P($"The password for your HalalBook account was changed on {DateTime.UtcNow:MMMM d, yyyy 'at' HH:mm} UTC.") +
            P("If this was you, no action is needed. If not, reset your password immediately and contact us.") +
            Button("Reset password", "/forgot-password")),
        "Your HalalBook password was changed. If this wasn't you, reset it immediately.");
}

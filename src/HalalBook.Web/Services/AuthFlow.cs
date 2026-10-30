using HalalBook.Shared;
using Microsoft.AspNetCore.Components;

namespace HalalBook.Web.Services;

/// <summary>Moves the person to the next step after sign-in, sign-up or a code check.</summary>
public static class AuthFlow
{
    public static async Task ContinueAsync(AuthResult result, AuthSession session, NavigationManager nav, string? returnUrl)
    {
        if (result.Step == AuthStep.Done && result.Auth is { } auth)
        {
            await session.SignInAsync(auth);
            var target = SafeReturn(returnUrl);
            // Shared post pages are server-rendered only and need a full page load.
            nav.NavigateTo(target, forceLoad: target.StartsWith("/p/"));
            return;
        }
        var query = $"?email={Uri.EscapeDataString(result.Email ?? "")}&step={result.Step}&msg={Uri.EscapeDataString(result.Message ?? "")}";
        if (!string.IsNullOrEmpty(returnUrl)) query += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";
        nav.NavigateTo("/verify" + query);
    }

    /// <summary>A local path to go back to after signing in; the sign-in and welcome pages themselves lead home.</summary>
    public static string SafeReturn(string? r) =>
        r is { Length: > 1 } && r.StartsWith('/') && !r.StartsWith("//") && !IsEntryPage(r) ? r : "/";

    private static bool IsEntryPage(string path) =>
        new[] { "/welcome", "/login", "/register", "/verify" }.Any(p => path.Split('?', '#')[0].Equals(p, StringComparison.OrdinalIgnoreCase));
}

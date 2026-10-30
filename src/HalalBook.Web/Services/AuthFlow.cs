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
            nav.NavigateTo(SafeReturn(returnUrl));
            return;
        }
        var query = $"?email={Uri.EscapeDataString(result.Email ?? "")}&step={result.Step}&msg={Uri.EscapeDataString(result.Message ?? "")}";
        if (!string.IsNullOrEmpty(returnUrl)) query += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";
        nav.NavigateTo("/verify" + query);
    }

    public static string SafeReturn(string? r) => r is { Length: > 1 } && r.StartsWith('/') && !r.StartsWith("//") ? r : "/";
}

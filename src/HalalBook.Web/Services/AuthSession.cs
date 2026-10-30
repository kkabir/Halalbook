using System.Net.Http.Json;
using HalalBook.Shared;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace HalalBook.Web.Services;

/// <summary>
/// The signed-in user for this circuit. The token is kept in encrypted browser storage so it
/// survives reloads; it is loaded on the first interactive render (prerendering is off).
/// </summary>
public class AuthSession(ProtectedLocalStorage storage, IHttpClientFactory factory)
{
    private const string Key = "hb.session";
    private bool _loaded;

    public string? Token { get; private set; }
    public UserDto? User { get; private set; }
    public bool IsAuthenticated => Token is not null && User is not null;
    public bool IsModerator => User?.Role is UserRole.Moderator or UserRole.Admin;

    public event Action? Changed;

    public async Task LoadAsync()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            var saved = await storage.GetAsync<AuthResponse>(Key);
            if (saved.Success && saved.Value is { } s && s.ExpiresAt > DateTime.UtcNow)
            {
                Token = s.Token;
                User = s.User;
                // Refresh the profile in the background of the first load.
                using var http = factory.CreateClient("api");
                using var req = new HttpRequestMessage(HttpMethod.Get, "api/users/me");
                req.Headers.Authorization = new("Bearer", Token);
                using var res = await http.SendAsync(req);
                if (res.IsSuccessStatusCode) User = await res.Content.ReadFromJsonAsync<UserDto>(ApiClient.Json);
                else if ((int)res.StatusCode == 401) { Token = null; User = null; await storage.DeleteAsync(Key); }
            }
        }
        catch (Exception) { Token = null; User = null; }
        Changed?.Invoke();
    }

    public async Task SignInAsync(AuthResponse auth)
    {
        Token = auth.Token;
        User = auth.User;
        _loaded = true;
        await storage.SetAsync(Key, auth);
        Changed?.Invoke();
    }

    public async Task UpdateUserAsync(UserDto user)
    {
        User = user;
        var saved = await storage.GetAsync<AuthResponse>(Key);
        if (saved.Success && saved.Value is { } s) await storage.SetAsync(Key, s with { User = user });
        Changed?.Invoke();
    }

    public async Task SignOutAsync()
    {
        Token = null;
        User = null;
        try { await storage.DeleteAsync(Key); } catch (Exception) { /* circuit may be gone */ }
        Changed?.Invoke();
    }
}

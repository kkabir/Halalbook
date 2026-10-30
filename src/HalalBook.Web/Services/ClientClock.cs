using Microsoft.JSInterop;

namespace HalalBook.Web.Services;

/// <summary>The browser's UTC offset, so times render in the viewer's zone rather than the server's.</summary>
public class ClientClock(IJSRuntime js)
{
    public int OffsetMinutes { get; private set; }
    private bool _loaded;

    public async Task LoadAsync()
    {
        if (_loaded) return;
        try { OffsetMinutes = await js.InvokeAsync<int>("halalbook.utcOffset"); _loaded = true; }
        catch (JSException) { }
    }

    public DateTime Local(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).AddMinutes(OffsetMinutes);
    public DateTime ToUtc(DateTime local) => DateTime.SpecifyKind(local.AddMinutes(-OffsetMinutes), DateTimeKind.Utc);
    public DateTime Now => DateTime.UtcNow.AddMinutes(OffsetMinutes);
}

using HalalBook.Web.Components;
using HalalBook.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var apiBase = builder.Configuration["Api:BaseUrl"] ?? "https://api.halalbook.app/";
builder.Services.AddHttpClient("api", c => c.BaseAddress = new Uri(apiBase.TrimEnd('/') + "/"));
builder.Services.AddHttpClient<ApiClient>(c =>
{
    c.BaseAddress = new Uri(apiBase.TrimEnd('/') + "/");
    c.Timeout = TimeSpan.FromMinutes(5); // large video uploads
});
builder.Services.AddScoped<AuthSession>();
builder.Services.AddScoped<Realtime>();
builder.Services.AddScoped<Toasts>();
builder.Services.AddScoped<ClientClock>();
builder.Services.AddScoped<MediaGuard>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

using System.Net.Http.Json;
using System.Threading.Channels;
using HalalBook.Shared;

namespace HalalBook.Web.Services;

/// <summary>
/// Forwards Error-level logs from the web app (for example a crashed page or circuit) to the API's error log,
/// so admins see web and API errors in one place.
/// </summary>
public sealed class ApiErrorLoggerProvider : ILoggerProvider
{
    private readonly Channel<ClientErrorRequest> _queue = Channel.CreateBounded<ClientErrorRequest>(
        new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest });

    // The HTTP client factory is resolved lazily: logging is built before it, and resolving it here eagerly would loop.
    public ApiErrorLoggerProvider(IServiceProvider services)
    {
        _ = Task.Run(async () =>
        {
            await foreach (var e in _queue.Reader.ReadAllAsync())
            {
                try
                {
                    using var http = services.GetRequiredService<IHttpClientFactory>().CreateClient("api");
                    await http.PostAsJsonAsync("api/errors/client", e);
                }
                catch (Exception) { /* the API may be down; nothing else to do */ }
            }
        });
    }

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);
    public void Dispose() => _queue.Writer.TryComplete();

    private sealed class Logger(ApiErrorLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        // Skip the HTTP client's own logs so a failing API can't cause a loop.
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Error && !category.StartsWith("System.Net.Http");

        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            owner._queue.Writer.TryWrite(new ClientErrorRequest("Web", formatter(state, exception) is { Length: > 0 } m ? m : exception?.Message ?? "Error",
                exception?.ToString(), category, null));
        }
    }
}

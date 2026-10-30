using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using HalalBook.Api.Data;
using HalalBook.Shared;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace HalalBook.Api.Services;

public record ErrorEntry(string Source, string Level, string Message, string? ExceptionType, string? StackTrace,
    string? Path, string? Method, int? StatusCode, Guid? UserId, string? TraceId, string? UserAgent);

/// <summary>
/// Collects errors from anywhere (unhandled exceptions, error logs, the web app, browsers) without blocking the caller;
/// <see cref="ErrorLogWriter"/> saves them to the database, grouping repeats of the same error.
/// </summary>
public class ErrorSink
{
    private readonly Channel<ErrorEntry> _channel = Channel.CreateBounded<ErrorEntry>(
        new BoundedChannelOptions(2000) { FullMode = BoundedChannelFullMode.DropOldest });

    public void Report(ErrorEntry entry) => _channel.Writer.TryWrite(entry);
    public ChannelReader<ErrorEntry> Reader => _channel.Reader;

    /// <summary>Same type, message and place = same error, counted instead of duplicated.</summary>
    public static string Fingerprint(ErrorEntry e)
    {
        var path = e.Path is null ? "" : System.Text.RegularExpressions.Regex.Replace(e.Path, @"[0-9a-fA-F\-]{32,36}|\d+", "{id}");
        var firstFrame = e.StackTrace?.Split('\n').FirstOrDefault()?.Trim() ?? "";
        var key = $"{e.Source}|{e.ExceptionType}|{Truncate(e.Message, 300)}|{path}|{firstFrame}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..40];
    }

    public static string Truncate(string? s, int n) => s is null ? "" : s.Length <= n ? s : s[..n];
}

public class ErrorLogWriter(ErrorSink sink, IServiceScopeFactory scopes, ILogger<ErrorLogWriter> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var e in sink.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var hash = ErrorSink.Fingerprint(e);
                var existing = await db.ErrorLogs.FirstOrDefaultAsync(x => x.Fingerprint == hash, stoppingToken);
                if (existing is null)
                {
                    db.ErrorLogs.Add(new ErrorLog
                    {
                        Fingerprint = hash, Source = e.Source, Level = e.Level, Message = ErrorSink.Truncate(e.Message, 2000),
                        ExceptionType = ErrorSink.Truncate(e.ExceptionType, 300), StackTrace = ErrorSink.Truncate(e.StackTrace, 8000),
                        Path = ErrorSink.Truncate(e.Path, 500), Method = e.Method, StatusCode = e.StatusCode, UserId = e.UserId,
                        TraceId = e.TraceId, UserAgent = ErrorSink.Truncate(e.UserAgent, 300),
                    });
                }
                else
                {
                    existing.Count++;
                    existing.LastSeen = DateTime.UtcNow;
                    existing.UserId = e.UserId ?? existing.UserId;
                    existing.TraceId = e.TraceId ?? existing.TraceId;
                    if (existing.Status == ErrorStatus.Resolved) existing.Status = ErrorStatus.Open; // it came back
                }
                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Never log this through the DB logger, or a broken database would loop forever.
                log.LogWarning("Could not save an error log entry: {Message}", ex.Message);
            }
        }
    }
}

/// <summary>Records unhandled exceptions; the default problem-details response is still returned to the caller.</summary>
public class DbExceptionHandler(ErrorSink sink) : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        Guid? user = Guid.TryParse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
        sink.Report(new ErrorEntry("Api", "Error", ex.Message, ex.GetType().FullName, ex.ToString(),
            ctx.Request.Path, ctx.Request.Method, 500, user, ctx.TraceIdentifier, ctx.Request.Headers.UserAgent.ToString()));
        return ValueTask.FromResult(false);
    }
}

/// <summary>Sends Error and Critical log messages from any part of the API (hubs, background work) to the error log.</summary>
public sealed class DbLoggerProvider(ErrorSink sink) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new DbLogger(sink, categoryName);
    public void Dispose() { }

    private sealed class DbLogger(ErrorSink sink, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        // Skip EF Core, the error writer itself and the exception middleware (already recorded by DbExceptionHandler).
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Error
            && !category.StartsWith("Microsoft.EntityFrameworkCore") && !category.Contains(nameof(ErrorLogWriter))
            && !category.StartsWith("Microsoft.AspNetCore.Diagnostics");

        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            sink.Report(new ErrorEntry("Api", level.ToString(), formatter(state, exception), exception?.GetType().FullName,
                exception?.ToString(), category, null, null, null, null, null));
        }
    }
}

using System.Diagnostics;

namespace Geotrack.Server.Middleware;

public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    private const int MaxCorrelationIdLength = 128;
    private static readonly char[] AllowedExtraCharacters = ['-', '_', '.', ':'];

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);
        context.TraceIdentifier = correlationId;
        Activity.Current?.SetTag("correlation.id", correlationId);

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        }))
        {
            await _next(context);
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].FirstOrDefault();
        return IsSafeCorrelationId(incoming)
            ? incoming!.Trim()
            : context.TraceIdentifier;
    }

    private static bool IsSafeCorrelationId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= MaxCorrelationIdLength &&
               trimmed.All(ch => char.IsLetterOrDigit(ch) || AllowedExtraCharacters.Contains(ch));
    }
}

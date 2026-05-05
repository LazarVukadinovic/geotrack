using System.Text.RegularExpressions;
using Geotrack.Application.DTOs.Analytics;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/analytics")]
[EnableRateLimiting("analytics")]
public class AnalyticsController : ControllerBase
{
    private readonly IAnalyticsService _analyticsService;
    private readonly ILogger<AnalyticsController> _logger;

    public AnalyticsController(
        IAnalyticsService analyticsService,
        ILogger<AnalyticsController> logger)
    {
        _analyticsService = analyticsService;
        _logger = logger;
    }

    /// <summary>
    /// Batched event ingestion. Accepts both authenticated and anonymous traffic.
    /// Always returns 204 — analytics is best-effort and must never break the
    /// caller's user flow.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("events")]
    public async Task<IActionResult> Ingest([FromBody] AnalyticsEventBatchDto? dto, CancellationToken ct)
    {
        if (dto is null)
        {
            return NoContent();
        }

        var context = new AnalyticsContext(
            UserId: int.TryParse(User.FindFirst("userId")?.Value, out var id) ? id : null,
            AnonymousSessionId: dto.AnonymousSessionId,
            UserAgentFamily: ParseUserAgentFamily(Request.Headers.UserAgent.ToString()),
            CoarseRegion: TrimToNull(Request.Headers["X-Geo-Region"].FirstOrDefault()),
            CoarseCountry: TrimToNull(Request.Headers["X-Geo-Country"].FirstOrDefault()));

        try
        {
            await _analyticsService.IngestBatchAsync(dto, context, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Analytics ingest failed; swallowing.");
        }

        return NoContent();
    }

    private static string? TrimToNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static readonly Regex IPhoneOrIPad = new("(iPhone|iPad|iPod)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AndroidUa = new("Android", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DesktopUa = new("(Windows NT|Macintosh|X11; Linux)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string ParseUserAgentFamily(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return "other";
        }

        // Check iOS first because some iPad UA strings also match desktop patterns.
        if (IPhoneOrIPad.IsMatch(userAgent)) return "mobile-ios";
        if (AndroidUa.IsMatch(userAgent)) return "mobile-android";
        if (DesktopUa.IsMatch(userAgent)) return "desktop";
        return "other";
    }
}

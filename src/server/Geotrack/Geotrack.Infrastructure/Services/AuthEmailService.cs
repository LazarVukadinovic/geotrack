using System.Net;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Options;
using Geotrack.Domain.Entities.Users;
using Microsoft.Extensions.Options;

namespace Geotrack.Infrastructure.Services;

public class AuthEmailService : IAuthEmailService
{
    private readonly IEmailSender _emailSender;
    private readonly EmailOptions _options;

    public AuthEmailService(IEmailSender emailSender, IOptions<EmailOptions> options)
    {
        _emailSender = emailSender;
        _options = options.Value;
    }

    public Task SendEmailVerificationAsync(User user, string token, CancellationToken cancellationToken = default)
    {
        var link = BuildFrontendLink("verify-email", user.Email, token);
        var appName = Safe(_options.AppName);
        var subject = $"Verify your {appName} email address";
        var textBody = $"Welcome to {_options.AppName}! Verify your email address by opening this link: {link}\n\nThis link expires in {_options.EmailVerificationTokenLifetimeHours} hours.";
        var htmlBody = BuildHtmlEmail(
            "Verify your email address",
            $"Welcome to {appName}. Confirm your email address to finish creating your account.",
            "Verify email",
            link,
            $"This link expires in {_options.EmailVerificationTokenLifetimeHours} hours.");

        return _emailSender.SendAsync(user.Email, subject, htmlBody, textBody, cancellationToken);
    }

    public Task SendPasswordResetAsync(User user, string token, CancellationToken cancellationToken = default)
    {
        var link = BuildFrontendLink("reset-password", user.Email, token);
        var appName = Safe(_options.AppName);
        var subject = $"Reset your {appName} password";
        var textBody = $"Reset your {_options.AppName} password by opening this link: {link}\n\nThis link expires in {_options.PasswordResetTokenLifetimeMinutes} minutes. If you did not request it, ignore this email.";
        var htmlBody = BuildHtmlEmail(
            "Reset your password",
            $"We received a request to reset your {appName} password.",
            "Reset password",
            link,
            $"This link expires in {_options.PasswordResetTokenLifetimeMinutes} minutes. If you did not request it, you can ignore this email.");

        return _emailSender.SendAsync(user.Email, subject, htmlBody, textBody, cancellationToken);
    }

    private string BuildFrontendLink(string path, string email, string token)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.FrontendBaseUrl)
            ? "http://localhost:4200"
            : _options.FrontendBaseUrl.Trim().TrimEnd('/');

        return $"{baseUrl}/{path}?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
    }

    private string AppName => string.IsNullOrWhiteSpace(_options.AppName) ? "GeoTrack" : _options.AppName.Trim();

    private static string BuildHtmlEmail(string title, string intro, string buttonText, string link, string note)
    {
        var safeTitle = Safe(title);
        var safeIntro = Safe(intro);
        var safeButton = Safe(buttonText);
        var safeLink = Safe(link);
        var safeNote = Safe(note);

        return $$"""
        <!doctype html>
        <html>
        <body style="margin:0;padding:0;background:#f8fafc;font-family:Arial,sans-serif;color:#0f172a;">
          <div style="max-width:560px;margin:0 auto;padding:32px 20px;">
            <div style="background:#ffffff;border:1px solid #e2e8f0;border-radius:20px;padding:28px;">
              <h1 style="margin:0 0 14px;font-size:24px;line-height:1.2;color:#0f172a;">{{safeTitle}}</h1>
              <p style="margin:0 0 24px;font-size:16px;line-height:1.6;color:#475569;">{{safeIntro}}</p>
              <p style="margin:0 0 24px;">
                <a href="{{safeLink}}" style="display:inline-block;background:#e8570a;color:#ffffff;text-decoration:none;font-weight:700;border-radius:999px;padding:13px 22px;">{{safeButton}}</a>
              </p>
              <p style="margin:0 0 12px;font-size:14px;line-height:1.6;color:#64748b;">{{safeNote}}</p>
              <p style="margin:0;font-size:12px;line-height:1.6;color:#94a3b8;word-break:break-all;">{{safeLink}}</p>
            </div>
          </div>
        </body>
        </html>
        """;
    }

    private static string Safe(string value) => WebUtility.HtmlEncode(value);
}

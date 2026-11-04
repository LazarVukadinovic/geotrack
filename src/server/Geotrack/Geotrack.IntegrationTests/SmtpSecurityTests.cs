using System.Net;
using System.Net.Sockets;
using System.Text;
using Geotrack.Application.Options;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class SmtpSecurityTests
{
    private const string Token = "synthetic-secret+token/8327";
    private const string Recipient = "private-recipient@example.test";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disabled_delivery_never_logs_authentication_content(bool verification)
    {
        var options = Options.Create(new EmailOptions { Enabled = false, FrontendBaseUrl = "https://example.test" });
        var logger = new SecurityCaptureLogger<SmtpEmailSender>();
        var sender = new SmtpEmailSender(options, logger);
        var auth = new AuthEmailService(sender, options);
        var user = new User { Email = Recipient };
        if (verification) await auth.SendEmailVerificationAsync(user, Token);
        else await auth.SendPasswordResetAsync(user, Token);
        // Exercise every argument, including subject, as potentially sensitive input.
        await sender.SendAsync(Recipient, Token, Token, Token);
        Assert.Contains("Suppressed", logger.AllOutput);
        AssertConfidential(logger.AllOutput);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Enabled_smtp_delivers_auth_links_without_logging_them(bool verification)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var receive = ReceiveMessageAsync(listener, timeout.Token);
        var options = Options.Create(new EmailOptions
        {
            Enabled = true, Host = "127.0.0.1", Port = ((IPEndPoint)listener.LocalEndpoint).Port,
            UseSsl = false, FromEmail = "sender@example.test", FrontendBaseUrl = "https://example.test"
        });
        var logger = new SecurityCaptureLogger<SmtpEmailSender>();
        var auth = new AuthEmailService(new SmtpEmailSender(options, logger), options);
        var user = new User { Email = Recipient };
        if (verification) await auth.SendEmailVerificationAsync(user, Token, timeout.Token);
        else await auth.SendPasswordResetAsync(user, Token, timeout.Token);
        var wire = (await receive).Replace("=\r\n", "");
        Assert.Contains(Recipient, wire);
        Assert.Contains(verification ? "verify-email" : "reset-password", wire);
        Assert.Contains(Uri.EscapeDataString(Token), wire);
        Assert.Contains("Sent", logger.AllOutput);
        AssertConfidential(logger.AllOutput);
    }

    [Fact]
    public async Task Smtp_failure_does_not_disclose_server_reply_in_logs_or_exception()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var receive = ReceiveMessageAsync(listener, timeout.Token, reject: true);
        var options = Options.Create(new EmailOptions
        {
            Enabled = true, Host = "127.0.0.1", Port = ((IPEndPoint)listener.LocalEndpoint).Port,
            UseSsl = false, FromEmail = "sender@example.test"
        });
        var logger = new SecurityCaptureLogger<SmtpEmailSender>();
        var sender = new SmtpEmailSender(options, logger);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(Recipient, Token, Token, Token, timeout.Token));
        await receive;
        Assert.Contains("Failed", logger.AllOutput);
        AssertConfidential(logger.AllOutput + exception);
    }

    private static void AssertConfidential(string output)
    {
        foreach (var secret in new[] { Token, Uri.EscapeDataString(Token), Recipient, "https://example.test", "reset-password", "verify-email" })
            Assert.DoesNotContain(secret, output);
    }

    // A private loopback SMTP peer; no provider, credentials, or real recipient is contacted.
    private static async Task<string> ReceiveMessageAsync(TcpListener listener, CancellationToken cancellationToken, bool reject = false)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        await using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };
        await writer.WriteLineAsync("220 localhost test SMTP");
        while (await reader.ReadLineAsync(cancellationToken) is { } command)
        {
            if (reject && command.StartsWith("MAIL", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync($"550 {Token} {Recipient}");
                return "";
            }
            if (command.Equals("DATA", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("354 End with a dot");
                var body = new StringBuilder();
                while (await reader.ReadLineAsync(cancellationToken) is { } line && line != ".")
                    body.Append(line).Append("\r\n");
                await writer.WriteLineAsync("250 Accepted");
                return body.ToString();
            }
            await writer.WriteLineAsync("250 OK");
        }
        throw new InvalidOperationException("Test SMTP connection closed before delivery.");
    }
}

internal sealed class SecurityCaptureLogger<T> : ILogger<T>
{
    private readonly List<string> _output = [];
    public string AllOutput => string.Join("\n", _output);
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        _output.Add(formatter(state, exception));
        if (state is IEnumerable<KeyValuePair<string, object?>> properties)
            _output.AddRange(properties.Select(property => $"{property.Key}={property.Value}"));
        if (exception is not null) _output.Add(exception.ToString());
    }
}

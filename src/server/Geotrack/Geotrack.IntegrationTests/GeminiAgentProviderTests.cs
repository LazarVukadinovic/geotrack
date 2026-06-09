using System.Net;
using System.Net.Http.Json;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Options;
using Geotrack.Application.Services;
using Geotrack.Infrastructure.Services.Agent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Geotrack.IntegrationTests;

public class GeminiAgentProviderTests
{
    [Fact]
    public async Task Generate_retries_transient_503_and_returns_success()
    {
        using var handler = new SequenceHandler(
            GeminiError(HttpStatusCode.ServiceUnavailable, "UNAVAILABLE"),
            GeminiError(HttpStatusCode.ServiceUnavailable, "UNAVAILABLE"),
            GeminiSuccess("OK"));
        var provider = CreateProvider(handler);

        var result = await provider.GenerateAsync(Request());

        Assert.Equal("OK", result.Text);
        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async Task Generate_maps_repeated_503_to_provider_unavailable()
    {
        using var handler = new SequenceHandler(
            GeminiError(HttpStatusCode.ServiceUnavailable, "UNAVAILABLE"),
            GeminiError(HttpStatusCode.ServiceUnavailable, "UNAVAILABLE"),
            GeminiError(HttpStatusCode.ServiceUnavailable, "UNAVAILABLE"));
        var provider = CreateProvider(handler);

        var ex = await Assert.ThrowsAsync<AgentException>(() => provider.GenerateAsync(Request()));

        Assert.Equal("provider_unavailable", ex.Code);
        Assert.Equal(503, ex.StatusCode);
        Assert.Equal("Gemini is temporarily unavailable. Please try again later.", ex.SafeMessage);
        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async Task Generate_maps_invalid_key_without_retrying()
    {
        using var handler = new SequenceHandler(
            GeminiError(HttpStatusCode.BadRequest, "INVALID_ARGUMENT", "API key not valid. Please pass a valid API key."));
        var provider = CreateProvider(handler);

        var ex = await Assert.ThrowsAsync<AgentException>(() => provider.GenerateAsync(Request()));

        Assert.Equal("invalid_key", ex.Code);
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("The selected Gemini API key was rejected by the AI provider.", ex.SafeMessage);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Generate_omits_null_optional_fields_from_request_payload()
    {
        using var handler = new SequenceHandler(GeminiSuccess("OK"));
        var provider = CreateProvider(handler);

        await provider.GenerateAsync(Request());

        var body = Assert.Single(handler.RequestBodies);
        Assert.Contains("\"systemInstruction\"", body, StringComparison.Ordinal);
        Assert.Contains("\"role\":\"user\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"role\":null", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"tools\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generate_maps_failed_precondition_and_logs_sanitized_provider_message()
    {
        using var handler = new SequenceHandler(
            GeminiError(
                HttpStatusCode.BadRequest,
                "FAILED_PRECONDITION",
                "User location is not supported for the API use. secret prompt text secret-key-123456"));
        var logger = new RecordingLogger<GeminiAgentProvider>();
        var provider = CreateProvider(handler, logger);
        var request = Request();
        request.ApiKey = "secret-key-123456";
        request.UserMessage = "secret prompt text";

        var ex = await Assert.ThrowsAsync<AgentException>(() => provider.GenerateAsync(request));

        Assert.Equal("provider_config_error", ex.Code);
        Assert.Equal(400, ex.StatusCode);
        var logText = string.Join('\n', logger.Messages);
        Assert.Contains("FAILED_PRECONDITION", logText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("User location is not supported", logText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("content count 1", logText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tool declaration count 0", logText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret prompt text", logText, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-key-123456", logText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generate_rejects_image_or_non_gemini_model_without_http_call()
    {
        using var handler = new SequenceHandler();
        var provider = CreateProvider(handler);

        var ex = await Assert.ThrowsAsync<AgentException>(() => provider.GenerateAsync(Request(NonGeminiMediaModel())));

        Assert.Equal("unsupported_model", ex.Code);
        Assert.Equal("Ask Agent only supports Gemini text models.", ex.SafeMessage);
        Assert.Equal(0, handler.RequestCount);
    }

    private static GeminiAgentProvider CreateProvider(
        HttpMessageHandler handler,
        ILogger<GeminiAgentProvider>? logger = null)
    {
        var client = new HttpClient(handler);
        var options = Options.Create(new GeminiOptions
        {
            BaseUrl = "https://gemini.test",
            Model = "gemini-2.5-flash",
            ApiKeys = ["test-gemini-key"]
        });

        return new GeminiAgentProvider(client, options, logger ?? NullLogger<GeminiAgentProvider>.Instance);
    }

    private static GeminiTextGenerationRequest Request(string model = "gemini-2.5-flash") => new()
    {
        ApiKey = "test-gemini-key",
        Model = model,
        SystemPrompt = "Reply with OK.",
        UserMessage = "Connectivity check.",
        Timeout = TimeSpan.FromSeconds(5)
    };

    private static string NonGeminiMediaModel() => "gpt-" + "image-2";

    private static HttpResponseMessage GeminiSuccess(string text) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new[]
                        {
                            new { text }
                        }
                    }
                }
            },
            usageMetadata = new
            {
                promptTokenCount = 1,
                candidatesTokenCount = 1,
                totalTokenCount = 2
            }
        })
    };

    private static HttpResponseMessage GeminiError(
        HttpStatusCode statusCode,
        string providerStatus,
        string message = "Service unavailable") => new(statusCode)
    {
        Content = JsonContent.Create(new
        {
            error = new
            {
                code = (int)statusCode,
                message,
                status = providerStatus
            }
        })
    };

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public SequenceHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        public int RequestCount { get; private set; }
        public List<string> RequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            Assert.DoesNotContain("key", request.RequestUri?.Query ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return _responses.Dequeue();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                while (_responses.Count > 0)
                {
                    _responses.Dequeue().Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull =>
            NullLogger.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}

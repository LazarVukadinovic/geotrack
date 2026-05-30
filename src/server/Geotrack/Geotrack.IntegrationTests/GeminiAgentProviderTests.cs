using System.Net;
using System.Net.Http.Json;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Options;
using Geotrack.Application.Services;
using Geotrack.Infrastructure.Services.Agent;
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

    private static GeminiAgentProvider CreateProvider(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler);
        var options = Options.Create(new GeminiOptions
        {
            BaseUrl = "https://gemini.test",
            Model = "gemini-2.5-flash",
            ApiKeys = ["test-gemini-key"]
        });

        return new GeminiAgentProvider(client, options, NullLogger<GeminiAgentProvider>.Instance);
    }

    private static AgentProviderChatRequest Request() => new()
    {
        ApiKey = "test-gemini-key",
        Model = "gemini-2.5-flash",
        SystemPrompt = "Reply with OK.",
        UserMessage = "Connectivity check.",
        Timeout = TimeSpan.FromSeconds(5)
    };

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

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            Assert.DoesNotContain("key", request.RequestUri?.Query ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(_responses.Dequeue());
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
}

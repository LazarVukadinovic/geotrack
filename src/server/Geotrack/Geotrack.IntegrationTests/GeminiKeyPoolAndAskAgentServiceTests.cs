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

public class GeminiKeyPoolAndAskAgentServiceTests
{
    [Fact]
    public async Task Generate_uses_default_model_when_config_model_is_missing()
    {
        using var handler = new SequenceHandler(GeminiSuccess("OK"));
        var service = CreateService(handler, new GeminiOptions
        {
            BaseUrl = "https://gemini.test",
            Model = "",
            ApiKeys = ["first-key-1111"]
        });

        var result = await service.GenerateAsync(Request());

        Assert.Equal("OK", result.Text);
        Assert.Equal("gemini-3.5-flash", service.Model);
        Assert.Contains("/models/gemini-3.5-flash:generateContent", handler.RequestUris.Single());
    }

    [Fact]
    public async Task Generate_uses_configured_model_override()
    {
        using var handler = new SequenceHandler(GeminiSuccess("OK"));
        var service = CreateService(handler, new GeminiOptions
        {
            BaseUrl = "https://gemini.test",
            Model = "gemini-custom-prod",
            ApiKeys = ["first-key-1111"]
        });

        await service.GenerateAsync(Request());

        Assert.Equal("gemini-custom-prod", service.Model);
        Assert.Contains("/models/gemini-custom-prod:generateContent", handler.RequestUris.Single());
    }

    [Fact]
    public async Task Generate_falls_back_to_default_when_config_model_is_not_gemini_text()
    {
        using var handler = new SequenceHandler(GeminiSuccess("OK"));
        var service = CreateService(handler, new GeminiOptions
        {
            BaseUrl = "https://gemini.test",
            Model = NonGeminiMediaModel(),
            ApiKeys = ["first-key-1111"]
        });

        var result = await service.GenerateAsync(Request());

        Assert.Equal("OK", result.Text);
        Assert.Equal("gemini-3.5-flash", service.Model);
        Assert.Contains("/models/gemini-3.5-flash:generateContent", handler.RequestUris.Single());
        Assert.DoesNotContain(NonGeminiMediaModelPrefix(), handler.RequestUris.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Generate_falls_back_to_next_key_after_quota_error()
    {
        using var handler = new SequenceHandler(
            GeminiError(HttpStatusCode.TooManyRequests, "RESOURCE_EXHAUSTED", "Quota exceeded."),
            GeminiSuccess("OK"));
        var service = CreateService(handler);

        var result = await service.GenerateAsync(Request());

        Assert.Equal("OK", result.Text);
        Assert.Equal(["first-key-1111", "second-key-2222"], handler.ApiKeys);
    }

    [Fact]
    public async Task Generate_tries_configured_fallback_model_for_failed_precondition_without_rotating_keys()
    {
        using var handler = new SequenceHandler(
            GeminiError(HttpStatusCode.BadRequest, "FAILED_PRECONDITION", "Gemini API free tier is not available for this request."),
            GeminiSuccess("OK"));
        var service = CreateService(handler, new GeminiOptions
        {
            BaseUrl = "https://gemini.test",
            Model = "gemini-3.1-flash-lite",
            FallbackModels = ["gemini-3.5-flash"],
            ApiKeys = ["first-key-1111", "second-key-2222"],
            KeyCooldownSeconds = 300
        });

        var result = await service.GenerateAsync(Request());

        Assert.Equal("OK", result.Text);
        Assert.Equal(["first-key-1111", "first-key-1111"], handler.ApiKeys);
        Assert.Contains("/models/gemini-3.1-flash-lite:generateContent", handler.RequestUris[0]);
        Assert.Contains("/models/gemini-3.5-flash:generateContent", handler.RequestUris[1]);
    }

    [Fact]
    public void Rate_limited_key_is_put_on_cooldown_and_skipped()
    {
        var pool = CreatePool(new GeminiOptions
        {
            ApiKeys = ["first-key-1111", "second-key-2222"],
            KeyCooldownSeconds = 300
        });

        var first = pool.GetAvailableKeys().First();
        pool.MarkRateLimited(first);

        var nextAttempts = pool.GetAvailableKeys();

        Assert.DoesNotContain(nextAttempts, key => key.Index == first.Index);
        Assert.Single(nextAttempts);
        Assert.Equal(1, pool.AvailableKeyCount);
    }

    [Fact]
    public void Key_pool_logs_fingerprint_without_full_key()
    {
        var logger = new RecordingLogger<GeminiKeyPool>();
        var pool = new GeminiKeyPool(
            Options.Create(new GeminiOptions
            {
                ApiKeys = ["first-key-1111", "second-key-2222"],
                KeyCooldownSeconds = 300
            }),
            logger);

        var first = pool.GetAvailableKeys().First();
        pool.MarkRateLimited(first);

        var logText = string.Join('\n', logger.Messages);
        Assert.Contains(first.Fingerprint, logText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("first-key-1111", logText, StringComparison.Ordinal);
        Assert.DoesNotContain("second-key-2222", logText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generate_returns_safe_unavailable_when_all_keys_are_rate_limited()
    {
        using var handler = new SequenceHandler(
            GeminiError(HttpStatusCode.TooManyRequests, "RESOURCE_EXHAUSTED", "Quota exceeded."),
            GeminiError(HttpStatusCode.TooManyRequests, "RESOURCE_EXHAUSTED", "Rate limit exceeded."));
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<AgentException>(() => service.GenerateAsync(Request()));

        Assert.Equal("agent_unavailable", ex.Code);
        Assert.Equal(503, ex.StatusCode);
        Assert.Equal("Ask Agent is currently unavailable. Please try again later.", ex.SafeMessage);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task Generate_marks_invalid_key_unavailable_and_tries_next_key()
    {
        using var handler = new SequenceHandler(
            GeminiError(HttpStatusCode.BadRequest, "INVALID_ARGUMENT", "API key not valid. Please pass a valid API key."),
            GeminiSuccess("OK"),
            GeminiSuccess("OK again"));
        var service = CreateService(handler);

        var first = await service.GenerateAsync(Request());
        var second = await service.GenerateAsync(Request());

        Assert.Equal("OK", first.Text);
        Assert.Equal("OK again", second.Text);
        Assert.Equal(["first-key-1111", "second-key-2222", "second-key-2222"], handler.ApiKeys);
    }

    [Fact]
    public async Task Generate_does_not_rotate_keys_for_non_quota_bad_request()
    {
        using var handler = new SequenceHandler(
            GeminiError(HttpStatusCode.BadRequest, "INVALID_ARGUMENT", "Prompt validation failed."));
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<AgentException>(() => service.GenerateAsync(Request()));

        Assert.Equal("provider_bad_request", ex.Code);
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Missing_keys_return_safe_unavailable_response()
    {
        using var handler = new SequenceHandler();
        var service = CreateService(handler, new GeminiOptions
        {
            BaseUrl = "https://gemini.test",
            Model = "gemini-3.5-flash",
            ApiKeys = []
        });

        var ex = await Assert.ThrowsAsync<AgentException>(() => service.GenerateAsync(Request()));

        Assert.Equal("agent_unavailable", ex.Code);
        Assert.Equal("Ask Agent is currently unavailable. Please try again later.", ex.SafeMessage);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task Concurrent_key_pool_reads_do_not_throw_and_keep_consistent_state()
    {
        var pool = CreatePool();

        await Parallel.ForEachAsync(Enumerable.Range(0, 100), (_, _) =>
        {
            var attempts = pool.GetAvailableKeys();
            Assert.True(attempts.Count is >= 1 and <= 2);
            return ValueTask.CompletedTask;
        });

        Assert.Equal(2, pool.ConfiguredKeyCount);
        Assert.Equal(2, pool.AvailableKeyCount);
    }

    private static GeminiAskAgentService CreateService(
        SequenceHandler handler,
        GeminiOptions? options = null)
    {
        var geminiOptions = Options.Create(options ?? new GeminiOptions
        {
            BaseUrl = "https://gemini.test",
            Model = "gemini-3.5-flash",
            ApiKeys = ["first-key-1111", "second-key-2222"],
            KeyCooldownSeconds = 300
        });
        var provider = new GeminiAgentProvider(
            new HttpClient(handler),
            geminiOptions,
            NullLogger<GeminiAgentProvider>.Instance);
        var pool = new GeminiKeyPool(geminiOptions, NullLogger<GeminiKeyPool>.Instance);
        return new GeminiAskAgentService(
            provider,
            pool,
            geminiOptions,
            NullLogger<GeminiAskAgentService>.Instance);
    }

    private static GeminiKeyPool CreatePool(GeminiOptions? options = null) =>
        new(
            Options.Create(options ?? new GeminiOptions
            {
                ApiKeys = ["first-key-1111", "second-key-2222"],
                KeyCooldownSeconds = 300
            }),
            NullLogger<GeminiKeyPool>.Instance);

    private static GeminiTextGenerationRequest Request() => new()
    {
        Model = "gemini-3.5-flash",
        SystemPrompt = "Reply with OK.",
        UserMessage = "Connectivity check.",
        Timeout = TimeSpan.FromSeconds(5)
    };

    private static string NonGeminiMediaModel() => NonGeminiMediaModelPrefix() + "-2";

    private static string NonGeminiMediaModelPrefix() => "gpt-" + "image";

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
        string message) => new(statusCode)
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
        private readonly object _gate = new();
        private readonly Queue<HttpResponseMessage> _responses;

        public SequenceHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        public int RequestCount { get; private set; }
        public List<string> ApiKeys { get; } = new();
        public List<string> RequestUris { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                RequestCount++;
                RequestUris.Add(request.RequestUri?.AbsoluteUri ?? string.Empty);
                ApiKeys.Add(request.Headers.TryGetValues("x-goog-api-key", out var values)
                    ? values.Single()
                    : string.Empty);
                Assert.DoesNotContain("key", request.RequestUri?.Query ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                return Task.FromResult(_responses.Dequeue());
            }
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

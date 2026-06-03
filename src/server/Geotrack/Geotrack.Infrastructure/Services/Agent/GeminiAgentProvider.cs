using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Geotrack.Application.DTOs.Agent;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Options;
using Geotrack.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geotrack.Infrastructure.Services.Agent;

public class GeminiAgentProvider
{
    private const int MaxTransientAttempts = 3;
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _geminiOptions;
    private readonly ILogger<GeminiAgentProvider> _logger;

    public GeminiAgentProvider(
        HttpClient httpClient,
        IOptions<GeminiOptions> geminiOptions,
        ILogger<GeminiAgentProvider> logger)
    {
        _httpClient = httpClient;
        _geminiOptions = geminiOptions.Value;
        _logger = logger;
    }

    public async Task<GeminiTextGenerationResult> GenerateAsync(
        GeminiTextGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        var model = NormalizeModel(request.Model);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (request.Timeout > TimeSpan.Zero)
        {
            timeoutCts.CancelAfter(request.Timeout);
        }

        var url = BuildGenerateContentUrl(model);
        for (var attempt = 1; attempt <= MaxTransientAttempts; attempt++)
        {
            using var httpRequest = CreateGenerateContentRequest(url, request);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(httpRequest, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new AgentException("provider_timeout", "Gemini timed out. Please try again later.", 504);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(
                    "Gemini request attempt {Attempt} failed with {ExceptionType}.",
                    attempt,
                    ex.GetType().Name);

                if (attempt < MaxTransientAttempts)
                {
                    await DelayBeforeRetryAsync(attempt, timeoutCts.Token, cancellationToken);
                    continue;
                }

                throw new AgentException(
                    "provider_unavailable",
                    "Gemini is temporarily unavailable. Please try again later.",
                    503);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    GeminiErrorResponse? providerError;
                    try
                    {
                        providerError = await ReadProviderErrorAsync(response, timeoutCts.Token);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new AgentException("provider_timeout", "Gemini timed out. Please try again later.", 504);
                    }

                    var mapped = MapProviderError(response.StatusCode, providerError);
                    if (ShouldRetry(response.StatusCode, providerError) && attempt < MaxTransientAttempts)
                    {
                        _logger.LogWarning(
                            "Gemini transient failure {StatusCode} on attempt {Attempt}.",
                            (int)response.StatusCode,
                            attempt);
                        await DelayBeforeRetryAsync(attempt, timeoutCts.Token, cancellationToken);
                        continue;
                    }

                    _logger.LogWarning(
                        "Gemini provider returned status {StatusCode} provider status {ProviderStatus}; mapped to {ErrorCode} on attempt {Attempt}.",
                        (int)response.StatusCode,
                        providerError?.Error?.Status,
                        mapped.Code,
                        attempt);

                    throw mapped;
                }

                GeminiGenerateContentResponse? body;
                try
                {
                    body = await response.Content.ReadFromJsonAsync<GeminiGenerateContentResponse>(
                        cancellationToken: timeoutCts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new AgentException("provider_timeout", "Gemini timed out. Please try again later.", 504);
                }
                catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.Text.Json.JsonException)
                {
                    _logger.LogWarning("Gemini response parsing failed with {ExceptionType}.", ex.GetType().Name);
                    throw new AgentException("provider_unavailable", "Gemini returned an unreadable response.", 503);
                }

                var text = body?.Candidates?
                    .SelectMany(candidate => candidate.Content?.Parts ?? new List<GeminiPart>())
                    .Select(part => part.Text)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

                if (string.IsNullOrWhiteSpace(text))
                {
                    throw new AgentException("provider_unavailable", "Gemini did not return an answer.", 503);
                }

                return new GeminiTextGenerationResult
                {
                    Text = text.Trim(),
                    Usage = body?.UsageMetadata is null
                        ? null
                        : new AgentUsageMetadataDto
                        {
                            PromptTokens = body.UsageMetadata.PromptTokenCount,
                            CompletionTokens = body.UsageMetadata.CandidatesTokenCount,
                            TotalTokens = body.UsageMetadata.TotalTokenCount
                        }
                };
            }
        }

        throw new AgentException(
            "provider_unavailable",
            "Gemini is temporarily unavailable. Please try again later.",
            503);
    }

    private Uri BuildGenerateContentUrl(string model)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_geminiOptions.BaseUrl)
            ? GeminiOptions.DefaultBaseUrl
            : _geminiOptions.BaseUrl.TrimEnd('/');
        return new Uri($"{baseUrl}/v1beta/models/{Uri.EscapeDataString(model)}:generateContent");
    }

    private string NormalizeModel(string model)
    {
        var candidate = string.IsNullOrWhiteSpace(model)
            ? GeminiOptions.DefaultModel
            : model.Trim();
        if (candidate.Length > 200 ||
            candidate.Any(char.IsControl) ||
            !candidate.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase))
        {
            throw new AgentException("unsupported_model", "Ask Agent only supports Gemini text models.", 400);
        }

        return candidate;
    }

    private static HttpRequestMessage CreateGenerateContentRequest(Uri url, GeminiTextGenerationRequest request)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Headers.TryAddWithoutValidation("x-goog-api-key", request.ApiKey);
        httpRequest.Content = JsonContent.Create(new GeminiGenerateContentRequest
        {
            SystemInstruction = new GeminiContent
            {
                Parts = new List<GeminiPart>
                {
                    new() { Text = request.SystemPrompt }
                }
            },
            Contents = new List<GeminiContent>
            {
                new()
                {
                    Role = "user",
                    Parts = new List<GeminiPart>
                    {
                        new() { Text = request.UserMessage }
                    }
                }
            },
            GenerationConfig = new GeminiGenerationConfig
            {
                MaxOutputTokens = 1024,
                Temperature = 0.2
            }
        });

        return httpRequest;
    }

    private static async Task<GeminiErrorResponse?> ReadProviderErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<GeminiErrorResponse>(
                cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static AgentException MapProviderError(
        HttpStatusCode statusCode,
        GeminiErrorResponse? providerError)
    {
        var providerStatus = providerError?.Error?.Status;
        var providerMessage = providerError?.Error?.Message;

        if (IsInvalidKeyError(statusCode, providerStatus, providerMessage))
        {
            return new AgentException(
                "invalid_key",
                "The selected Gemini API key was rejected by the AI provider.",
                400);
        }

        if (IsQuotaOrRateLimitError(statusCode, providerStatus, providerMessage))
        {
            return new AgentException(
                "quota_exceeded",
                "Gemini quota or rate limit was reached.",
                429);
        }

        if (statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout)
        {
            return new AgentException("provider_timeout", "Gemini timed out. Please try again later.", 504);
        }

        if (statusCode == HttpStatusCode.NotFound)
        {
            return new AgentException(
                "unsupported_model",
                "The selected model is not available for Gemini.",
                400);
        }

        if (statusCode == HttpStatusCode.BadRequest)
        {
            return new AgentException(
                "provider_bad_request",
                "Ask Agent could not process that request.",
                400);
        }

        return new AgentException(
            "provider_unavailable",
            "Gemini is temporarily unavailable. Please try again later.",
            503);
    }

    private static bool ShouldRetry(HttpStatusCode statusCode, GeminiErrorResponse? providerError)
    {
        if (providerError?.Error?.Status is { } status &&
            string.Equals(status, "UNAVAILABLE", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return statusCode is
            HttpStatusCode.RequestTimeout or
            HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout;
    }

    private static async Task DelayBeforeRetryAsync(
        int attempt,
        CancellationToken retryCancellationToken,
        CancellationToken callerCancellationToken)
    {
        try
        {
            await Task.Delay(ComputeBackoff(attempt), retryCancellationToken);
        }
        catch (OperationCanceledException) when (!callerCancellationToken.IsCancellationRequested)
        {
            throw new AgentException("provider_timeout", "Gemini timed out. Please try again later.", 504);
        }
    }

    private static TimeSpan ComputeBackoff(int attempt)
    {
        var exponentialDelayMs = Math.Min(800, 200 * (1 << Math.Max(0, attempt - 1)));
        var jitterMs = Random.Shared.Next(0, 100);
        return TimeSpan.FromMilliseconds(exponentialDelayMs + jitterMs);
    }

    private static bool IsInvalidKeyError(
        HttpStatusCode statusCode,
        string? providerStatus,
        string? providerMessage)
    {
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return true;
        }

        if (string.Equals(providerStatus, "UNAUTHENTICATED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(providerStatus, "PERMISSION_DENIED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (statusCode != HttpStatusCode.BadRequest || string.IsNullOrWhiteSpace(providerMessage))
        {
            return false;
        }

        return providerMessage.Contains("api key", StringComparison.OrdinalIgnoreCase) &&
            (providerMessage.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
             providerMessage.Contains("not valid", StringComparison.OrdinalIgnoreCase) ||
             providerMessage.Contains("rejected", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsQuotaOrRateLimitError(
        HttpStatusCode statusCode,
        string? providerStatus,
        string? providerMessage)
    {
        if (statusCode == HttpStatusCode.TooManyRequests ||
            string.Equals(providerStatus, "RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(providerMessage))
        {
            return false;
        }

        return providerMessage.Contains("quota", StringComparison.OrdinalIgnoreCase) ||
            providerMessage.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
            providerMessage.Contains("resource exhausted", StringComparison.OrdinalIgnoreCase) ||
            providerMessage.Contains("daily limit", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class GeminiGenerateContentRequest
    {
        [JsonPropertyName("systemInstruction")]
        public GeminiContent? SystemInstruction { get; set; }

        [JsonPropertyName("contents")]
        public List<GeminiContent> Contents { get; set; } = new();

        [JsonPropertyName("generationConfig")]
        public GeminiGenerationConfig GenerationConfig { get; set; } = new();
    }

    private sealed class GeminiGenerationConfig
    {
        [JsonPropertyName("maxOutputTokens")]
        public int MaxOutputTokens { get; set; }

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }
    }

    private sealed class GeminiGenerateContentResponse
    {
        [JsonPropertyName("candidates")]
        public List<GeminiCandidate>? Candidates { get; set; }

        [JsonPropertyName("usageMetadata")]
        public GeminiUsageMetadata? UsageMetadata { get; set; }
    }

    private sealed class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent? Content { get; set; }
    }

    private sealed class GeminiContent
    {
        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("parts")]
        public List<GeminiPart> Parts { get; set; } = new();
    }

    private sealed class GeminiPart
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    private sealed class GeminiUsageMetadata
    {
        [JsonPropertyName("promptTokenCount")]
        public int? PromptTokenCount { get; set; }

        [JsonPropertyName("candidatesTokenCount")]
        public int? CandidatesTokenCount { get; set; }

        [JsonPropertyName("totalTokenCount")]
        public int? TotalTokenCount { get; set; }
    }

    private sealed class GeminiErrorResponse
    {
        [JsonPropertyName("error")]
        public GeminiError? Error { get; set; }
    }

    private sealed class GeminiError
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }
    }
}

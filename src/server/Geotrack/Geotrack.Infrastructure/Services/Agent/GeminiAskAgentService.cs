using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Options;
using Geotrack.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geotrack.Infrastructure.Services.Agent;

public class GeminiAskAgentService : IGeminiAskAgentService
{
    private const string GeminiProviderId = "gemini";
    private readonly GeminiAgentProvider _provider;
    private readonly IGeminiKeyPool _keyPool;
    private readonly GeminiOptions _options;
    private readonly ILogger<GeminiAskAgentService> _logger;

    public GeminiAskAgentService(
        GeminiAgentProvider provider,
        IGeminiKeyPool keyPool,
        IOptions<GeminiOptions> options,
        ILogger<GeminiAskAgentService> logger)
    {
        _provider = provider;
        _keyPool = keyPool;
        _options = options.Value;
        _logger = logger;
    }

    public string ProviderId => GeminiProviderId;
    public string Model => ResolveModelOrder(_options.Model, _options.FallbackModels).First();
    public bool HasAvailableKey => _keyPool.AvailableKeyCount > 0;

    public async Task<GeminiTextGenerationResult> GenerateAsync(
        GeminiTextGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        var attempts = _keyPool.GetAvailableKeys();
        if (attempts.Count == 0)
        {
            _logger.LogWarning(
                "Gemini key pool has no available keys for Ask Agent; configured {ConfiguredKeyCount}, available {AvailableKeyCount}.",
                _keyPool.ConfiguredKeyCount,
                _keyPool.AvailableKeyCount);
            throw TemporarilyUnavailable();
        }

        var modelOrder = ResolveModelOrder(_options.Model, _options.FallbackModels);
        _logger.LogInformation(
            "Gemini Ask Agent request will try up to {AttemptCount} key(s) for models {Models}; configured {ConfiguredKeyCount}, available {AvailableKeyCount}.",
            attempts.Count,
            string.Join(",", modelOrder),
            _keyPool.ConfiguredKeyCount,
            _keyPool.AvailableKeyCount);

        foreach (var key in attempts)
        {
            for (var modelIndex = 0; modelIndex < modelOrder.Count; modelIndex++)
            {
                var model = modelOrder[modelIndex];
                try
                {
                    return await _provider.GenerateAsync(
                        new GeminiTextGenerationRequest
                        {
                            ApiKey = key.ApiKey,
                            Model = model,
                            SystemPrompt = request.SystemPrompt,
                            UserMessage = request.UserMessage,
                            Timeout = request.Timeout
                        },
                        cancellationToken);
                }
                catch (AgentException ex) when (IsQuotaOrRateLimit(ex))
                {
                    _keyPool.MarkRateLimited(key);
                    _logger.LogWarning(
                        "Gemini API key {KeyFingerprint} hit quota/rate limits while serving Ask Agent; status {StatusCode}.",
                        key.Fingerprint,
                        ex.StatusCode);
                    break;
                }
                catch (AgentException ex) when (IsInvalidKey(ex))
                {
                    _keyPool.MarkInvalid(key);
                    _logger.LogWarning(
                        "Gemini API key {KeyFingerprint} was rejected by Gemini; status {StatusCode}.",
                        key.Fingerprint,
                        ex.StatusCode);
                    break;
                }
                catch (AgentException ex) when (IsModelFallbackCandidate(ex) && modelIndex < modelOrder.Count - 1)
                {
                    _logger.LogWarning(
                        "Gemini model {Model} failed with {ErrorCode}; trying fallback model {FallbackModel}.",
                        model,
                        ex.Code,
                        modelOrder[modelIndex + 1]);
                }
                catch (AgentException ex) when (IsProviderConfigurationError(ex))
                {
                    _keyPool.MarkConfigurationUnavailable(key);
                    _logger.LogWarning(
                        "Gemini API key {KeyFingerprint} hit provider configuration error while serving Ask Agent; status {StatusCode}.",
                        key.Fingerprint,
                        ex.StatusCode);
                    break;
                }
                catch (AgentException)
                {
                    throw;
                }
            }
        }

        _logger.LogWarning(
            "Gemini Ask Agent exhausted {AttemptCount} key attempt(s); configured {ConfiguredKeyCount}, available {AvailableKeyCount}.",
            attempts.Count,
            _keyPool.ConfiguredKeyCount,
            _keyPool.AvailableKeyCount);
        throw TemporarilyUnavailable();
    }

    private static bool IsQuotaOrRateLimit(AgentException ex) =>
        string.Equals(ex.Code, "quota_exceeded", StringComparison.OrdinalIgnoreCase);

    private static bool IsInvalidKey(AgentException ex) =>
        string.Equals(ex.Code, "invalid_key", StringComparison.OrdinalIgnoreCase);

    private static bool IsModelFallbackCandidate(AgentException ex) =>
        string.Equals(ex.Code, "provider_config_error", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ex.Code, "unsupported_model", StringComparison.OrdinalIgnoreCase);

    private static bool IsProviderConfigurationError(AgentException ex) =>
        string.Equals(ex.Code, "provider_config_error", StringComparison.OrdinalIgnoreCase);

    private static AgentException TemporarilyUnavailable() =>
        new("agent_unavailable", "Ask Agent is currently unavailable. Please try again later.", 503);

    private static string ResolveModel(string? configuredModel)
    {
        var model = string.IsNullOrWhiteSpace(configuredModel)
            ? GeminiOptions.DefaultModel
            : configuredModel.Trim();

        return model.Length > 200 || model.Any(char.IsControl) || !IsGeminiTextModel(model)
            ? GeminiOptions.DefaultModel
            : model;
    }

    private static List<string> ResolveModelOrder(string? configuredModel, IEnumerable<string>? fallbackModels)
    {
        var models = new List<string> { ResolveModel(configuredModel) };
        if (fallbackModels is not null)
        {
            models.AddRange(fallbackModels.Select(ResolveModel));
        }

        return models
            .Where(IsGeminiTextModel)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
    }

    private static bool IsGeminiTextModel(string model) =>
        model.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase);
}

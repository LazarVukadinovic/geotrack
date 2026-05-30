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
    public string Model => ResolveModel(_options.Model);
    public bool HasAvailableKey => _keyPool.AvailableKeyCount > 0;

    public async Task<AgentProviderChatResult> GenerateAsync(
        AgentProviderChatRequest request,
        CancellationToken cancellationToken = default)
    {
        var attempts = _keyPool.GetAvailableKeys();
        if (attempts.Count == 0)
        {
            throw TemporarilyUnavailable();
        }

        var model = Model;
        foreach (var key in attempts)
        {
            try
            {
                return await _provider.GenerateAsync(
                    new AgentProviderChatRequest
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
                    "Gemini API key {KeyFingerprint} hit quota/rate limits while serving Ask Agent.",
                    key.Fingerprint);
            }
            catch (AgentException ex) when (IsInvalidKey(ex))
            {
                _keyPool.MarkInvalid(key);
                _logger.LogWarning(
                    "Gemini API key {KeyFingerprint} was rejected by Gemini.",
                    key.Fingerprint);
            }
            catch (AgentException)
            {
                throw;
            }
        }

        throw TemporarilyUnavailable();
    }

    private static bool IsQuotaOrRateLimit(AgentException ex) =>
        string.Equals(ex.Code, "quota_exceeded", StringComparison.OrdinalIgnoreCase);

    private static bool IsInvalidKey(AgentException ex) =>
        string.Equals(ex.Code, "invalid_key", StringComparison.OrdinalIgnoreCase);

    private static AgentException TemporarilyUnavailable() =>
        new("agent_unavailable", "Ask Agent is currently unavailable. Please try again later.", 503);

    private static string ResolveModel(string? configuredModel)
    {
        var model = string.IsNullOrWhiteSpace(configuredModel)
            ? GeminiOptions.DefaultModel
            : configuredModel.Trim();

        return model.Length > 200 || model.Any(char.IsControl)
            ? GeminiOptions.DefaultModel
            : model;
    }
}

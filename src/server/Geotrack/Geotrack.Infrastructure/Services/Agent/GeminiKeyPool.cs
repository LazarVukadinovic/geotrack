using System.Security.Cryptography;
using System.Text;
using Geotrack.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geotrack.Infrastructure.Services.Agent;

public interface IGeminiKeyPool
{
    int ConfiguredKeyCount { get; }
    int AvailableKeyCount { get; }
    IReadOnlyList<GeminiApiKeyLease> GetAvailableKeys();
    void MarkRateLimited(GeminiApiKeyLease key, TimeSpan? retryAfter = null);
    void MarkInvalid(GeminiApiKeyLease key);
}

public sealed record GeminiApiKeyLease(int Index, string ApiKey, string Fingerprint);

public class GeminiKeyPool : IGeminiKeyPool
{
    private readonly object _gate = new();
    private readonly List<GeminiKeyState> _keys;
    private readonly TimeSpan _cooldown;
    private readonly int _maxAttemptsPerRequest;
    private readonly ILogger<GeminiKeyPool> _logger;
    private int _nextIndex;

    public GeminiKeyPool(IOptions<GeminiOptions> options, ILogger<GeminiKeyPool> logger)
    {
        var geminiOptions = options.Value;
        _logger = logger;
        _cooldown = TimeSpan.FromSeconds(Math.Clamp(geminiOptions.KeyCooldownSeconds, 30, 86_400));
        _maxAttemptsPerRequest = Math.Max(0, geminiOptions.MaxAttemptsPerRequest);
        _keys = geminiOptions.ApiKeys
            .Select(key => key?.Trim() ?? string.Empty)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .Select((key, index) => new GeminiKeyState(index, key, Fingerprint(key)))
            .ToList();
    }

    public int ConfiguredKeyCount
    {
        get
        {
            lock (_gate)
            {
                return _keys.Count;
            }
        }
    }

    public int AvailableKeyCount
    {
        get
        {
            lock (_gate)
            {
                var now = DateTimeOffset.UtcNow;
                return _keys.Count(key => key.IsAvailable(now));
            }
        }
    }

    public IReadOnlyList<GeminiApiKeyLease> GetAvailableKeys()
    {
        lock (_gate)
        {
            if (_keys.Count == 0)
            {
                return Array.Empty<GeminiApiKeyLease>();
            }

            var now = DateTimeOffset.UtcNow;
            var maxAttempts = ResolveMaxAttempts();
            var attempts = new List<GeminiApiKeyLease>(maxAttempts);
            var startIndex = _nextIndex;

            for (var offset = 0; offset < _keys.Count && attempts.Count < maxAttempts; offset++)
            {
                var index = (startIndex + offset) % _keys.Count;
                var key = _keys[index];
                if (!key.IsAvailable(now))
                {
                    continue;
                }

                attempts.Add(new GeminiApiKeyLease(key.Index, key.ApiKey, key.Fingerprint));
            }

            _nextIndex = (_nextIndex + 1) % _keys.Count;
            return attempts;
        }
    }

    public void MarkRateLimited(GeminiApiKeyLease key, TimeSpan? retryAfter = null)
    {
        lock (_gate)
        {
            var state = Find(key);
            if (state is null)
            {
                return;
            }

            var cooldown = retryAfter.HasValue && retryAfter.Value > _cooldown
                ? retryAfter.Value
                : _cooldown;
            state.CooldownUntil = DateTimeOffset.UtcNow.Add(cooldown);
            _logger.LogWarning(
                "Gemini API key {KeyFingerprint} entered cooldown for {CooldownSeconds} seconds.",
                state.Fingerprint,
                (int)cooldown.TotalSeconds);
        }
    }

    public void MarkInvalid(GeminiApiKeyLease key)
    {
        lock (_gate)
        {
            var state = Find(key);
            if (state is null)
            {
                return;
            }

            state.Invalid = true;
            _logger.LogWarning("Gemini API key {KeyFingerprint} was marked invalid.", state.Fingerprint);
        }
    }

    private int ResolveMaxAttempts()
    {
        if (_maxAttemptsPerRequest == 0)
        {
            return _keys.Count;
        }

        return Math.Clamp(_maxAttemptsPerRequest, 1, _keys.Count);
    }

    private GeminiKeyState? Find(GeminiApiKeyLease key) =>
        _keys.FirstOrDefault(state =>
            state.Index == key.Index &&
            string.Equals(state.Fingerprint, key.Fingerprint, StringComparison.Ordinal));

    private static string Fingerprint(string apiKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToHexString(hash.AsSpan(0, 4)).ToLowerInvariant();
    }

    private sealed class GeminiKeyState
    {
        public GeminiKeyState(int index, string apiKey, string fingerprint)
        {
            Index = index;
            ApiKey = apiKey;
            Fingerprint = fingerprint;
        }

        public int Index { get; }
        public string ApiKey { get; }
        public string Fingerprint { get; }
        public DateTimeOffset CooldownUntil { get; set; }
        public bool Invalid { get; set; }

        public bool IsAvailable(DateTimeOffset now) => !Invalid && CooldownUntil <= now;
    }
}

using System.Text.RegularExpressions;
using Geotrack.Application.DTOs.Moderation;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Reviews;
using Microsoft.Extensions.Caching.Memory;

namespace Geotrack.Infrastructure.Services;

public class ReviewContentModerationService : IReviewContentModerationService
{
    private const string ViolationMessage =
        "Review text contains language that violates GeoTrack content rules. Please edit it and try again.";

    private readonly IModerationTermCache _termCache;
    private readonly IMemoryCache _memoryCache;

    public ReviewContentModerationService(IModerationTermCache termCache, IMemoryCache memoryCache)
    {
        _termCache = termCache;
        _memoryCache = memoryCache;
    }

    public async Task EnsureAllowedReviewTextAsync(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var normalized = ModerationTextNormalizer.NormalizeForModeration(text);
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        var matcher = await GetMatcherAsync();
        if (matcher.IsViolation(normalized))
            throw new InvalidOperationException(ViolationMessage);
    }

    private async Task<ModerationMatcher> GetMatcherAsync()
    {
        var matcher = await _memoryCache.GetOrCreateAsync(
            ModerationCacheKeys.CompiledReviewContentMatcher,
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                entry.SlidingExpiration = TimeSpan.FromMinutes(1);

                var activeRules = await _termCache.GetActiveRulesAsync();
                return ModerationMatcher.Build(activeRules);
            });

        return matcher ?? ModerationMatcher.Empty;
    }

    private sealed class ModerationMatcher
    {
        internal static readonly ModerationMatcher Empty = new(
            new HashSet<string>(StringComparer.Ordinal),
            Array.Empty<string>(),
            Array.Empty<Regex>(),
            Array.Empty<Regex>());

        private readonly HashSet<string> _exactTerms;
        private readonly string[] _prefixTerms;
        private readonly Regex[] _exactObfuscatedPatterns;
        private readonly Regex[] _prefixObfuscatedPatterns;

        private ModerationMatcher(
            HashSet<string> exactTerms,
            string[] prefixTerms,
            Regex[] exactObfuscatedPatterns,
            Regex[] prefixObfuscatedPatterns)
        {
            _exactTerms = exactTerms;
            _prefixTerms = prefixTerms;
            _exactObfuscatedPatterns = exactObfuscatedPatterns;
            _prefixObfuscatedPatterns = prefixObfuscatedPatterns;
        }

        internal static ModerationMatcher Build(IReadOnlyList<ModerationTermRule> rules)
        {
            var exactTerms = rules
                .Where(rule => rule.MatchType == ModerationTermMatchType.Exact)
                .Select(rule => rule.NormalizedTerm)
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .Distinct(StringComparer.Ordinal)
                .ToHashSet(StringComparer.Ordinal);

            var prefixTerms = rules
                .Where(rule => rule.MatchType == ModerationTermMatchType.Prefix)
                .Select(rule => rule.NormalizedTerm)
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(term => term.Length)
                .ToArray();

            return new ModerationMatcher(
                exactTerms,
                prefixTerms,
                exactTerms
                    .Select(term => BuildSeparatedPattern(term, requireTrailingBoundary: true))
                    .OfType<Regex>()
                    .ToArray(),
                prefixTerms
                    .Select(term => BuildSeparatedPattern(term, requireTrailingBoundary: false))
                    .OfType<Regex>()
                    .ToArray());
        }

        internal bool IsViolation(string normalized)
        {
            var tokens = Regex
                .Split(normalized, "[^a-z0-9]+", RegexOptions.CultureInvariant)
                .Where(token => token.Length > 0)
                .ToArray();

            if (tokens.Any(IsBlockedToken))
                return true;

            return _exactObfuscatedPatterns.Any(pattern => pattern.IsMatch(normalized)) ||
                _prefixObfuscatedPatterns.Any(pattern => pattern.IsMatch(normalized));
        }

        private bool IsBlockedToken(string token)
        {
            if (_exactTerms.Contains(token))
                return true;

            return _prefixTerms.Any(prefix => token.StartsWith(prefix, StringComparison.Ordinal));
        }

        private static Regex? BuildSeparatedPattern(string term, bool requireTrailingBoundary)
        {
            var compactTerm = Regex.Replace(term, "[^a-z0-9]+", string.Empty, RegexOptions.CultureInvariant);
            if (compactTerm.Length < 3)
                return null;

            var pieces = compactTerm.Select(ch => Regex.Escape(ch.ToString()));
            var trailingBoundary = requireTrailingBoundary ? "(?![a-z0-9])" : string.Empty;
            var pattern = $@"(?<![a-z0-9]){string.Join("[^a-z0-9]*", pieces)}{trailingBoundary}";
            return new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
        }
    }
}

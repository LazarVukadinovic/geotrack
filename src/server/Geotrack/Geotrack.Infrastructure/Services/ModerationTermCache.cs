using Geotrack.Application.DTOs.Moderation;
using Geotrack.Application.Interfaces.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Geotrack.Infrastructure.Services;

public class ModerationTermCache : IModerationTermCache
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public ModerationTermCache(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<IReadOnlyList<ModerationTermRule>> GetActiveRulesAsync()
    {
        if (_cache.TryGetValue<IReadOnlyList<ModerationTermRule>>(
            ModerationCacheKeys.ActiveModerationTermRules,
            out var cachedRules))
        {
            return cachedRules ?? Array.Empty<ModerationTermRule>();
        }

        var rules = await _db.ModerationTerms
            .AsNoTracking()
            .Where(term => term.IsActive && term.NormalizedTerm != string.Empty)
            .OrderByDescending(term => term.Severity)
            .ThenBy(term => term.NormalizedTerm)
            .Select(term => new ModerationTermRule(
                term.Id,
                term.NormalizedTerm,
                term.MatchType))
            .ToListAsync();

        _cache.Set(
            ModerationCacheKeys.ActiveModerationTermRules,
            rules,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
                SlidingExpiration = TimeSpan.FromMinutes(1)
            });

        return rules;
    }

    public void Invalidate()
    {
        _cache.Remove(ModerationCacheKeys.ActiveModerationTermRules);
        _cache.Remove(ModerationCacheKeys.CompiledReviewContentMatcher);
    }
}

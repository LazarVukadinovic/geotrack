using Geotrack.Application.DTOs.Agent;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services.Agent;

public sealed class AgentAreaResolver : IAgentAreaResolver
{
    private const int CandidateLimit = 500;
    private const double MinimumTrigramSimilarity = 0.12d;
    private static readonly HashSet<string> AreaStopWords = new(StringComparer.Ordinal)
    {
        "sta", "šta", "ima", "nesto", "nešto", "zanimljivo", "aktivnosti", "aktivnost",
        "koji", "koje", "kakvi", "kakve", "u", "na", "kod", "oko", "za", "od", "da",
        "vidi", "vidim", "videti", "vidjeti", "restorani", "restoran", "kafici", "kafic",
        "plaze", "plaza", "beaches", "restaurants", "events", "places", "nearby"
    };

    private readonly AppDbContext _db;
    private readonly IAgentDatabaseCapabilityService _capabilities;

    public AgentAreaResolver(
        AppDbContext db,
        IAgentDatabaseCapabilityService capabilities)
    {
        _db = db;
        _capabilities = capabilities;
    }

    public async Task<AgentAreaResolutionDto?> ResolveFromTextAsync(
        string text,
        bool includeEntities = false,
        CancellationToken cancellationToken = default)
    {
        var normalized = AgentTextNormalizer.Normalize(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var tokens = AgentTextNormalizer.Tokenize(text).ToHashSet(StringComparer.Ordinal);
        var capabilities = await _capabilities.GetCapabilitiesAsync(cancellationToken);
        var candidates = capabilities.SupportsPgTrgm
            ? await LoadFuzzyCandidatesAsync(normalized, tokens, includeEntities, capabilities.SupportsUnaccent, cancellationToken)
            : await LoadFallbackCandidatesAsync(includeEntities, cancellationToken);

        if (capabilities.SupportsPgTrgm)
        {
            candidates.AddRange(await LoadFallbackCandidatesAsync(includeEntities, cancellationToken));
        }

        var scored = candidates
            .GroupBy(candidate => $"{candidate.Type}:{candidate.Id}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(candidate => ScoreAreaCandidate(candidate, normalized, tokens))
            .Where(match => match.Score > 0)
            .OrderByDescending(match => match.Score)
            .ThenBy(match => AreaTypeRank(match.Item.Type))
            .ThenBy(match => match.Item.Name)
            .Take(5)
            .ToList();

        var best = scored.FirstOrDefault();
        if (best is null)
        {
            return null;
        }

        var second = scored.Skip(1).FirstOrDefault();
        var ambiguous = second is not null &&
            best.Score < 480 &&
            Math.Abs(best.Score - second.Score) <= 20 &&
            best.Item.DestinationId != second.Item.DestinationId &&
            !string.Equals(
                AgentTextNormalizer.Normalize(best.Item.DisplayName),
                AgentTextNormalizer.Normalize(second.Item.DisplayName),
                StringComparison.Ordinal);

        return ToResolution(best, ambiguous, scored);
    }

    public async Task<AgentAreaResolutionDto?> ResolveFromContextAsync(
        AgentContextOptionsDto? contextOptions,
        CancellationToken cancellationToken = default)
    {
        if (contextOptions is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(contextOptions.ConversationAreaType) &&
            contextOptions.ConversationAreaId is > 0)
        {
            var resolved = await ResolveByIdAsync(
                contextOptions.ConversationAreaType,
                contextOptions.ConversationAreaId.Value,
                cancellationToken);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        if (contextOptions.ConversationDestinationId is > 0)
        {
            var resolved = await ResolveByIdAsync("destination", contextOptions.ConversationDestinationId.Value, cancellationToken);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        if (contextOptions.DestinationId is > 0)
        {
            var resolved = await ResolveByIdAsync("destination", contextOptions.DestinationId.Value, cancellationToken);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        foreach (var value in new[]
        {
            CleanContextText(contextOptions.ConversationDestinationName),
            CleanContextText(contextOptions.ConversationAreaName),
            CleanContextText(contextOptions.CurrentDestinationName),
            CleanContextText(contextOptions.CurrentCity),
            CleanContextText(contextOptions.CurrentLocationName)
        })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var resolved = await ResolveFromTextAsync(value, includeEntities: false, cancellationToken);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        return null;
    }

    public async Task<AgentAreaResolutionDto?> ResolveByIdAsync(
        string areaType,
        int areaId,
        CancellationToken cancellationToken = default)
    {
        var normalizedType = NormalizeAreaType(areaType);
        if (normalizedType is null || areaId <= 0)
        {
            return null;
        }

        AreaCandidate? candidate = normalizedType switch
        {
            "destination" => await _db.Destinations
                .AsNoTracking()
                .WherePublicDestinations()
                .Where(destination => destination.Id == areaId)
                .Select(destination => new AreaCandidate
                {
                    Type = "destination",
                    Id = destination.Id,
                    Name = destination.Name,
                    DisplayName = destination.Name,
                    LocationText = destination.Name,
                    DestinationId = destination.Id,
                    DestinationName = destination.Name,
                    Latitude = destination.Latitude,
                    Longitude = destination.Longitude
                })
                .SingleOrDefaultAsync(cancellationToken),
            "location" => await _db.Locations
                .AsNoTracking()
                .WherePublicLocations()
                .Where(location => location.Id == areaId)
                .Select(location => new AreaCandidate
                {
                    Type = "location",
                    Id = location.Id,
                    Name = location.Name,
                    DisplayName = location.Name == location.Destination.Name
                        ? location.Name
                        : location.Name + ", " + location.Destination.Name,
                    LocationText = location.Name,
                    ParentNames = new List<string> { location.Destination.Name },
                    DestinationId = location.DestinationId,
                    DestinationName = location.Destination.Name,
                    Latitude = location.Latitude,
                    Longitude = location.Longitude
                })
                .SingleOrDefaultAsync(cancellationToken),
            "region" => await _db.Regions
                .AsNoTracking()
                .Where(region => region.Id == areaId &&
                    region.Destinations.Any(destination => destination.Status == PublicVisibilityQueryExtensions.PublishedStatus))
                .Select(region => new AreaCandidate
                {
                    Type = "region",
                    Id = region.Id,
                    Name = region.Name,
                    DisplayName = region.Name,
                    LocationText = region.Name,
                    ParentNames = new List<string> { region.Territory.Name }
                })
                .SingleOrDefaultAsync(cancellationToken),
            "territory" => await _db.Territories
                .AsNoTracking()
                .Where(territory => territory.Id == areaId &&
                    territory.Regions.Any(region =>
                        region.Destinations.Any(destination => destination.Status == PublicVisibilityQueryExtensions.PublishedStatus)))
                .Select(territory => new AreaCandidate
                {
                    Type = "territory",
                    Id = territory.Id,
                    Name = territory.Name,
                    DisplayName = territory.Name,
                    LocationText = territory.Name,
                    ParentNames = new List<string> { territory.CountryCode ?? territory.CountryIso3 ?? string.Empty },
                    Aliases = new List<string> { territory.CountryCode ?? string.Empty, territory.CountryIso3 ?? string.Empty }
                })
                .SingleOrDefaultAsync(cancellationToken),
            _ => null
        };

        return candidate is null
            ? null
            : ToResolution(new ScoredAreaCandidate(candidate, 500, "area_scope"), false, []);
    }

    private async Task<List<AreaCandidate>> LoadFuzzyCandidatesAsync(
        string normalizedText,
        IReadOnlySet<string> tokens,
        bool includeEntities,
        bool useUnaccent,
        CancellationToken cancellationToken)
    {
        var searchPhrase = BuildDbSearchPhrase(normalizedText, tokens);
        var rawPattern = LikePattern(searchPhrase);
        var candidates = new List<AreaCandidate>();

        var destinations = _db.Destinations
            .AsNoTracking()
            .WherePublicDestinations()
            .Select(destination => new
            {
                Candidate = new AreaCandidate
                {
                    Type = "destination",
                    Id = destination.Id,
                    Name = destination.Name,
                    DisplayName = destination.Name,
                    LocationText = destination.Name,
                    DestinationId = destination.Id,
                    DestinationName = destination.Name,
                    Latitude = destination.Latitude,
                    Longitude = destination.Longitude
                },
                destination.Name,
                destination.SearchText,
                Similarity = EF.Functions.TrigramsSimilarity(destination.SearchText, searchPhrase)
            });
        destinations = useUnaccent
            ? destinations.Where(row =>
                row.Similarity >= MinimumTrigramSimilarity ||
                EF.Functions.ILike(row.SearchText, rawPattern) ||
                EF.Functions.ILike(AgentPostgresDbFunctions.Unaccent(row.Name), AgentPostgresDbFunctions.Unaccent(rawPattern)))
            : destinations.Where(row =>
                row.Similarity >= MinimumTrigramSimilarity ||
                EF.Functions.ILike(row.SearchText, rawPattern) ||
                EF.Functions.ILike(row.Name, rawPattern));
        var destinationRows = await destinations
            .OrderByDescending(row => row.Similarity)
            .ThenBy(row => row.Name)
            .Take(CandidateLimit)
            .ToListAsync(cancellationToken);
        candidates.AddRange(destinationRows.Select(row => row.Candidate with { DatabaseScore = row.Similarity }));

        var locations = _db.Locations
            .AsNoTracking()
            .WherePublicLocations()
            .Select(location => new
            {
                Candidate = new AreaCandidate
                {
                    Type = "location",
                    Id = location.Id,
                    Name = location.Name,
                    DisplayName = location.Name == location.Destination.Name
                        ? location.Name
                        : location.Name + ", " + location.Destination.Name,
                    LocationText = location.Name,
                    ParentNames = new List<string> { location.Destination.Name },
                    DestinationId = location.DestinationId,
                    DestinationName = location.Destination.Name,
                    Latitude = location.Latitude,
                    Longitude = location.Longitude
                },
                location.Name,
                location.SearchText,
                Similarity = EF.Functions.TrigramsSimilarity(location.SearchText, searchPhrase)
            });
        locations = useUnaccent
            ? locations.Where(row =>
                row.Similarity >= MinimumTrigramSimilarity ||
                EF.Functions.ILike(row.SearchText, rawPattern) ||
                EF.Functions.ILike(AgentPostgresDbFunctions.Unaccent(row.Name), AgentPostgresDbFunctions.Unaccent(rawPattern)))
            : locations.Where(row =>
                row.Similarity >= MinimumTrigramSimilarity ||
                EF.Functions.ILike(row.SearchText, rawPattern) ||
                EF.Functions.ILike(row.Name, rawPattern));
        var locationRows = await locations
            .OrderByDescending(row => row.Similarity)
            .ThenBy(row => row.Name)
            .Take(CandidateLimit)
            .ToListAsync(cancellationToken);
        candidates.AddRange(locationRows.Select(row => row.Candidate with { DatabaseScore = row.Similarity }));

        candidates.AddRange(await LoadFallbackRegionAndTerritoryCandidatesAsync(cancellationToken));

        if (includeEntities)
        {
            candidates.AddRange(await LoadFuzzyEntityCandidatesAsync(searchPhrase, rawPattern, useUnaccent, cancellationToken));
        }

        return candidates;
    }

    private async Task<List<AreaCandidate>> LoadFuzzyEntityCandidatesAsync(
        string searchPhrase,
        string rawPattern,
        bool useUnaccent,
        CancellationToken cancellationToken)
    {
        var candidates = new List<AreaCandidate>();

        var places = _db.Places
            .AsNoTracking()
            .WherePublicPlaces()
            .Select(place => new
            {
                Candidate = new AreaCandidate
                {
                    Type = place.Stay == null ? "place" : "stay",
                    Id = place.Id,
                    Name = place.Name,
                    DisplayName = place.Name + ", " + place.Location.Destination.Name,
                    LocationText = place.Name,
                    ParentNames = new List<string> { place.Location.Name, place.Location.Destination.Name },
                    DestinationId = place.Location.DestinationId,
                    DestinationName = place.Location.Destination.Name,
                    Latitude = place.Latitude,
                    Longitude = place.Longitude
                },
                place.Name,
                place.SearchText,
                Similarity = EF.Functions.TrigramsSimilarity(place.SearchText, searchPhrase)
            });
        places = useUnaccent
            ? places.Where(row =>
                row.Similarity >= MinimumTrigramSimilarity ||
                EF.Functions.ILike(row.SearchText, rawPattern) ||
                EF.Functions.ILike(AgentPostgresDbFunctions.Unaccent(row.Name), AgentPostgresDbFunctions.Unaccent(rawPattern)))
            : places.Where(row =>
                row.Similarity >= MinimumTrigramSimilarity ||
                EF.Functions.ILike(row.SearchText, rawPattern) ||
                EF.Functions.ILike(row.Name, rawPattern));
        var placeRows = await places
            .OrderByDescending(row => row.Similarity)
            .ThenBy(row => row.Name)
            .Take(CandidateLimit)
            .ToListAsync(cancellationToken);
        candidates.AddRange(placeRows.Select(row => row.Candidate with { DatabaseScore = row.Similarity }));

        var events = _db.Events
            .AsNoTracking()
            .WherePublicEvents()
            .Select(evt => new
            {
                Candidate = new AreaCandidate
                {
                    Type = "event",
                    Id = evt.Id,
                    Name = evt.Name,
                    DisplayName = evt.Name + ", " + evt.Destination.Name,
                    LocationText = evt.Name,
                    ParentNames = new List<string>
                    {
                        evt.Place != null
                            ? evt.Place.Name
                            : evt.Location != null ? evt.Location.Name : evt.Destination.Name,
                        evt.Destination.Name
                    },
                    DestinationId = evt.DestinationId,
                    DestinationName = evt.Destination.Name,
                    Latitude = evt.Place != null ? evt.Place.Latitude : evt.Location != null ? evt.Location.Latitude : evt.Destination.Latitude,
                    Longitude = evt.Place != null ? evt.Place.Longitude : evt.Location != null ? evt.Location.Longitude : evt.Destination.Longitude
                },
                evt.Name,
                evt.SearchText,
                Similarity = EF.Functions.TrigramsSimilarity(evt.SearchText, searchPhrase)
            });
        events = useUnaccent
            ? events.Where(row =>
                row.Similarity >= MinimumTrigramSimilarity ||
                EF.Functions.ILike(row.SearchText, rawPattern) ||
                EF.Functions.ILike(AgentPostgresDbFunctions.Unaccent(row.Name), AgentPostgresDbFunctions.Unaccent(rawPattern)))
            : events.Where(row =>
                row.Similarity >= MinimumTrigramSimilarity ||
                EF.Functions.ILike(row.SearchText, rawPattern) ||
                EF.Functions.ILike(row.Name, rawPattern));
        var eventRows = await events
            .OrderByDescending(row => row.Similarity)
            .ThenBy(row => row.Name)
            .Take(CandidateLimit)
            .ToListAsync(cancellationToken);
        candidates.AddRange(eventRows.Select(row => row.Candidate with { DatabaseScore = row.Similarity }));

        return candidates;
    }

    private async Task<List<AreaCandidate>> LoadFallbackCandidatesAsync(
        bool includeEntities,
        CancellationToken cancellationToken)
    {
        var candidates = new List<AreaCandidate>();
        candidates.AddRange(await _db.Destinations
            .AsNoTracking()
            .WherePublicDestinations()
            .OrderBy(destination => destination.Name)
            .Take(CandidateLimit)
            .Select(destination => new AreaCandidate
            {
                Type = "destination",
                Id = destination.Id,
                Name = destination.Name,
                DisplayName = destination.Name,
                LocationText = destination.Name,
                DestinationId = destination.Id,
                DestinationName = destination.Name,
                Latitude = destination.Latitude,
                Longitude = destination.Longitude
            })
            .ToListAsync(cancellationToken));

        candidates.AddRange(await _db.Locations
            .AsNoTracking()
            .WherePublicLocations()
            .OrderBy(location => location.Name)
            .Take(CandidateLimit)
            .Select(location => new AreaCandidate
            {
                Type = "location",
                Id = location.Id,
                Name = location.Name,
                DisplayName = location.Name == location.Destination.Name
                    ? location.Name
                    : location.Name + ", " + location.Destination.Name,
                LocationText = location.Name,
                ParentNames = new List<string> { location.Destination.Name },
                DestinationId = location.DestinationId,
                DestinationName = location.Destination.Name,
                Latitude = location.Latitude,
                Longitude = location.Longitude
            })
            .ToListAsync(cancellationToken));

        candidates.AddRange(await LoadFallbackRegionAndTerritoryCandidatesAsync(cancellationToken));

        if (includeEntities)
        {
            candidates.AddRange(await _db.Places
                .AsNoTracking()
                .WherePublicPlaces()
                .OrderBy(place => place.Name)
                .Take(CandidateLimit)
                .Select(place => new AreaCandidate
                {
                    Type = place.Stay == null ? "place" : "stay",
                    Id = place.Id,
                    Name = place.Name,
                    DisplayName = place.Name + ", " + place.Location.Destination.Name,
                    LocationText = place.Name,
                    ParentNames = new List<string> { place.Location.Name, place.Location.Destination.Name },
                    DestinationId = place.Location.DestinationId,
                    DestinationName = place.Location.Destination.Name,
                    Latitude = place.Latitude,
                    Longitude = place.Longitude
                })
                .ToListAsync(cancellationToken));

            candidates.AddRange(await _db.Events
                .AsNoTracking()
                .WherePublicEvents()
                .OrderBy(evt => evt.Name)
                .Take(CandidateLimit)
                .Select(evt => new AreaCandidate
                {
                    Type = "event",
                    Id = evt.Id,
                    Name = evt.Name,
                    DisplayName = evt.Name + ", " + evt.Destination.Name,
                    LocationText = evt.Name,
                    ParentNames = new List<string>
                    {
                        evt.Place != null
                            ? evt.Place.Name
                            : evt.Location != null ? evt.Location.Name : evt.Destination.Name,
                        evt.Destination.Name
                    },
                    DestinationId = evt.DestinationId,
                    DestinationName = evt.Destination.Name,
                    Latitude = evt.Place != null ? evt.Place.Latitude : evt.Location != null ? evt.Location.Latitude : evt.Destination.Latitude,
                    Longitude = evt.Place != null ? evt.Place.Longitude : evt.Location != null ? evt.Location.Longitude : evt.Destination.Longitude
                })
                .ToListAsync(cancellationToken));
        }

        return candidates;
    }

    private async Task<List<AreaCandidate>> LoadFallbackRegionAndTerritoryCandidatesAsync(CancellationToken cancellationToken)
    {
        var candidates = new List<AreaCandidate>();
        candidates.AddRange(await _db.Regions
            .AsNoTracking()
            .Where(region => region.Destinations.Any(destination => destination.Status == PublicVisibilityQueryExtensions.PublishedStatus))
            .OrderBy(region => region.Name)
            .Take(CandidateLimit)
            .Select(region => new AreaCandidate
            {
                Type = "region",
                Id = region.Id,
                Name = region.Name,
                DisplayName = region.Name,
                LocationText = region.Name,
                ParentNames = new List<string> { region.Territory.Name }
            })
            .ToListAsync(cancellationToken));

        candidates.AddRange(await _db.Territories
            .AsNoTracking()
            .Where(territory => territory.Regions.Any(region =>
                region.Destinations.Any(destination => destination.Status == PublicVisibilityQueryExtensions.PublishedStatus)))
            .OrderBy(territory => territory.Name)
            .Take(CandidateLimit)
            .Select(territory => new AreaCandidate
            {
                Type = "territory",
                Id = territory.Id,
                Name = territory.Name,
                DisplayName = territory.Name,
                LocationText = territory.Name,
                ParentNames = new List<string> { territory.CountryCode ?? territory.CountryIso3 ?? string.Empty },
                Aliases = new List<string> { territory.CountryCode ?? string.Empty, territory.CountryIso3 ?? string.Empty }
            })
            .ToListAsync(cancellationToken));

        return candidates;
    }

    private static ScoredAreaCandidate ScoreAreaCandidate(
        AreaCandidate candidate,
        string normalizedText,
        IReadOnlySet<string> tokens)
    {
        var names = new[]
            {
                candidate.Name,
                candidate.DisplayName,
                candidate.DestinationName
            }
            .Concat(candidate.ParentNames)
            .Concat(candidate.Aliases)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var bestScore = candidate.DatabaseScore >= 0.35d
            ? (int)Math.Round(candidate.DatabaseScore * 180d)
            : 0;
        var reason = candidate.DatabaseScore >= 0.35d ? "trigram" : string.Empty;

        foreach (var name in names)
        {
            var normalizedName = AgentTextNormalizer.Normalize(name);
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                continue;
            }

            var nameTokens = AgentTextNormalizer.Tokenize(name).ToHashSet(StringComparer.Ordinal);
            int score;
            string candidateReason;
            if (normalizedText == normalizedName)
            {
                score = 500;
                candidateReason = "exact_name";
            }
            else if (NameMatchesTokens(name, tokens))
            {
                score = 430 + Math.Min(40, nameTokens.Count * 10);
                candidateReason = "token_match";
            }
            else if (AgentTextNormalizer.ContainsPhrase(normalizedText, normalizedName))
            {
                score = 390;
                candidateReason = "phrase_match";
            }
            else if (normalizedName.Length >= 3 && normalizedText.Contains(normalizedName, StringComparison.Ordinal))
            {
                score = 300;
                candidateReason = "contains_name";
            }
            else if (normalizedText.Length >= 3 && normalizedName.Contains(normalizedText, StringComparison.Ordinal))
            {
                score = 180;
                candidateReason = "contains_query";
            }
            else
            {
                score = 0;
                candidateReason = string.Empty;
            }

            if (score > bestScore)
            {
                bestScore = score;
                reason = candidateReason;
            }
        }

        return new ScoredAreaCandidate(candidate, bestScore, reason);
    }

    private static AgentAreaResolutionDto ToResolution(
        ScoredAreaCandidate match,
        bool ambiguous,
        IReadOnlyCollection<ScoredAreaCandidate> candidates)
    {
        var candidate = match.Item;
        return new AgentAreaResolutionDto
        {
            AreaType = candidate.Type,
            AreaId = candidate.Id,
            DisplayName = candidate.DisplayName,
            LocationText = candidate.LocationText,
            DestinationId = candidate.DestinationId,
            DestinationName = candidate.DestinationName,
            ParentNames = candidate.ParentNames
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToList(),
            Confidence = Math.Round(Math.Clamp(match.Score / 500d, 0d, 1d), 3),
            IsAmbiguous = ambiguous,
            NormalizedTokens = AgentTextNormalizer.Tokenize(candidate.LocationText).ToHashSet(StringComparer.Ordinal),
            Candidates = candidates
                .Select(item => new AgentAreaCandidateDto
                {
                    AreaType = item.Item.Type,
                    AreaId = item.Item.Id,
                    DisplayName = item.Item.DisplayName,
                    Confidence = Math.Round(Math.Clamp(item.Score / 500d, 0d, 1d), 3)
                })
                .Take(5)
                .ToList()
        };
    }

    private static string BuildDbSearchPhrase(string normalizedText, IReadOnlySet<string> tokens)
    {
        var locationLikeToken = tokens
            .Where(token => token.Length >= 4 && !AreaStopWords.Contains(token))
            .OrderByDescending(token => token.Length)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(locationLikeToken)
            ? normalizedText
            : locationLikeToken;
    }

    private static bool NameMatchesTokens(string name, IReadOnlySet<string> tokenSet)
    {
        var normalized = AgentTextNormalizer.Normalize(name);
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length > 0 &&
            words.All(word => AgentTextNormalizer.TokenVariants(word).Any(tokenSet.Contains));
    }

    private static int AreaTypeRank(string type) => type switch
    {
        "destination" => 0,
        "location" => 1,
        "region" => 2,
        "territory" => 3,
        "place" => 4,
        "stay" => 5,
        "event" => 6,
        _ => 9
    };

    private static string? CleanContextText(string? value)
    {
        var cleaned = value?.Trim();
        if (string.IsNullOrWhiteSpace(cleaned) ||
            string.Equals(cleaned, "Explore", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(cleaned, "Nearby", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(cleaned, "nearby location", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return cleaned.Length <= 100 ? cleaned : cleaned[..100];
    }

    private static string? NormalizeAreaType(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "destination" or "destinations" => "destination",
            "location" or "locations" => "location",
            "region" or "regions" => "region",
            "territory" or "territories" or "country" or "countries" => "territory",
            _ => null
        };
    }

    private static string LikePattern(string value) =>
        $"%{value.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";

    private sealed record AreaCandidate
    {
        public string Type { get; init; } = string.Empty;
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string LocationText { get; init; } = string.Empty;
        public List<string> ParentNames { get; init; } = new();
        public List<string> Aliases { get; init; } = new();
        public int? DestinationId { get; init; }
        public string? DestinationName { get; init; }
        public double? Latitude { get; init; }
        public double? Longitude { get; init; }
        public double DatabaseScore { get; init; }
    }

    private sealed record ScoredAreaCandidate(
        AreaCandidate Item,
        int Score,
        string Reason);
}

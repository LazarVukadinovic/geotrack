using Geotrack.Application.DTOs.Agent;
using Geotrack.Application.DTOs.GeoTrackTools;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services.Agent;

public class AgentQueryPlanner : IAgentQueryPlanner
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "all", "an", "and", "around", "at", "best", "by", "do", "find", "for", "give", "in", "me",
        "near", "nearby", "of", "on", "please", "recommend", "show", "the", "to", "top", "what",
        "where", "with", "u", "kod", "oko", "na", "za", "mi", "prikazi", "prikaži", "preporuci",
        "preporuči", "najbolje", "najbolji", "koji", "koje", "su", "dobri", "dobre", "dobro",
        "sta", "šta", "ima", "jos", "još", "nesto", "nešto", "da", "vidim", "videti", "vidjeti",
        "obici", "obići", "mozes", "možeš"
    };

    private static readonly HashSet<string> RankingTokens = new(StringComparer.Ordinal)
    {
        "best", "top", "popular", "recommended", "recommend", "najbolje", "najbolji", "preporuci", "preporuči"
    };

    private static readonly HashSet<string> SerbianTokens = new(StringComparer.Ordinal)
    {
        "plaza", "plaze", "restoran", "restorani", "dogadaj", "dogadjaj", "dogadaji", "dogadjaji",
        "smestaj", "smjestaj", "smeštaj", "kafic", "kafić", "znamenitost", "znamenitosti",
        "preporuci", "preporuči", "sta", "šta", "ima", "videti", "vidjeti", "obici", "obići"
    };

    private static readonly Dictionary<string, string[]> EntityAliases = new(StringComparer.Ordinal)
    {
        ["places"] = ["place", "places", "attraction", "attractions", "landmark", "landmarks", "restaurant", "restaurants", "cafe", "cafes", "bar", "bars", "beach", "beaches", "mesto", "mesta", "mjesto", "mjesta", "znamenitost", "znamenitosti", "restoran", "restorani", "plaza", "plaze", "kafic", "kafić", "muzej", "museum"],
        ["stays"] = ["stay", "stays", "hotel", "hotels", "hoteli", "accommodation", "accommodations", "lodging", "apartment", "apartments", "apartman", "apartmani", "smeštaj", "smestaj", "smještaj", "smjestaj"],
        ["events"] = ["event", "events", "festival", "festivals", "concert", "concerts", "koncert", "koncerti", "manifestacija", "manifestacije", "dogadaj", "dogadjaj", "dogadaji", "dogadjaji", "događaj", "događaji"]
    };

    private static readonly Dictionary<string, ConceptDefinition> Concepts = new(StringComparer.Ordinal)
    {
        ["beach"] = new(
            ["beach", "beaches", "plaza", "plaze", "plaža", "plaže", "kupanje", "more"],
            ["beaches"],
            ["Beach"],
            [],
            ["beach"]),
        ["restaurant"] = new(
            ["restaurant", "restaurants", "restoran", "restorani", "dining", "food", "hrana", "jesti", "ručak", "rucak", "večera", "vecera", "bar", "bars", "wine bar", "kafić", "kafic", "cafe", "cafes", "coffee"],
            ["food-drink", "fine-dining", "nightlife", "beach-bars", "food-market"],
            ["Restaurant", "Cafe", "Bar"],
            [],
            ["restaurants"]),
        ["cafe"] = new(
            ["cafe", "cafes", "café", "coffee", "kafa", "kafic", "kafić"],
            ["food-drink"],
            ["Cafe"],
            [],
            ["cafe"]),
        ["landmark"] = new(
            ["attraction", "attractions", "landmark", "landmarks", "znamenitost", "znamenitosti", "atrakcija", "atrakcije", "videti", "vidjeti", "obici", "obići", "museum", "muzej", "viewpoint", "nature", "hiking"],
            ["landmarks", "historic-sites", "culture", "history", "nature", "hiking", "viewpoints"],
            ["Landmark", "Museum", "Viewpoint", "Nature"],
            [],
            ["attractions"]),
        ["festival"] = new(
            ["festival", "festivals", "manifestacija", "manifestacije", "koncert", "koncerti"],
            ["festivals", "culture"],
            [],
            ["Festival"],
            ["festival"])
    };

    private readonly AppDbContext _db;

    public AgentQueryPlanner(AppDbContext db)
    {
        _db = db;
    }

    public async Task<GeoTrackAgentQueryPlanDto> PlanAsync(
        string message,
        AgentContextOptionsDto? contextOptions,
        CancellationToken cancellationToken = default)
    {
        var original = (message ?? string.Empty).Trim();
        var normalized = AgentTextNormalizer.Normalize(original);
        var tokens = AgentTextNormalizer.Tokenize(normalized);
        var tokenSet = tokens.ToHashSet(StringComparer.Ordinal);
        var vocabulary = await LoadCatalogVocabularyAsync(cancellationToken);

        var messageLocation = await ResolveLocationFromTextAsync(normalized, tokenSet, cancellationToken);
        var resolvedLocation = messageLocation ?? await ResolveLocationFromContextAsync(contextOptions, cancellationToken);
        var usesCurrentContext = messageLocation is null && resolvedLocation is not null;

        var entityTypes = DetectEntityTypes(tokenSet);
        var concepts = DetectConcepts(normalized, tokenSet);
        var category = ResolvePrimaryCategory(concepts, entityTypes);
        var strictCategory = IsStrictCategory(category);
        var followUpIntent = messageLocation is null && IsFollowUpIntent(tokenSet);
        var rankingIntent = tokenSet.Overlaps(RankingTokens) ? "top" : "all";

        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var placeTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stayTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var eventCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var conceptKey in concepts)
        {
            if (!Concepts.TryGetValue(conceptKey, out var concept))
            {
                continue;
            }

            categories.Add(conceptKey);
            AddMatchingTags(tags, vocabulary.Tags, concept.TagAliases);
            AddMatchingTypes(placeTypes, vocabulary.PlaceTypes, concept.PlaceTypeAliases);
            AddMatchingTypes(eventCategories, vocabulary.EventTypes, concept.EventTypeAliases);
        }

        if (entityTypes.Contains("stays"))
        {
            AddMatchingTypes(stayTypes, vocabulary.StayTypes, ["hotel", "apartment", "villa", "stay"]);
        }

        var genericTokens = BuildGenericTokenSet();
        var locationTokens = resolvedLocation?.NormalizedTokens ?? new HashSet<string>(StringComparer.Ordinal);
        var locationText = resolvedLocation?.LocationText;
        var queryTerms = tokens
            .Where(token => token.Length > 1)
            .Where(token => !StopWords.Contains(token))
            .Where(token => !genericTokens.Contains(token))
            .Where(token => !MatchesLocationToken(token, locationTokens))
            .Where(token => !MatchesLocationText(token, locationText))
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToList();

        if (entityTypes.Count == 0 && resolvedLocation is not null)
        {
            entityTypes.Add("all");
        }

        var hasCatalogIntent = entityTypes.Count > 0 ||
            concepts.Count > 0 ||
            rankingIntent == "top" ||
            resolvedLocation is not null;
        var genericDiscoveryWithoutSpecificTerm = hasCatalogIntent &&
            resolvedLocation is null &&
            queryTerms.Count == 0 &&
            (entityTypes.Count > 0 || concepts.Count > 0 || rankingIntent == "top");

        var language = DetectLanguage(tokenSet, contextOptions?.Language);
        var clarificationNeeded = genericDiscoveryWithoutSpecificTerm;

        return new GeoTrackAgentQueryPlanDto
        {
            Intent = clarificationNeeded
                ? "clarification_needed"
                : hasCatalogIntent ? "catalog_search" : "general_help",
            Language = language,
            OriginalMessage = original,
            NormalizedMessage = normalized,
            EntityTypes = entityTypes.Count == 0 ? [] : entityTypes.ToList(),
            LocationText = resolvedLocation?.LocationText,
            LocationUsed = resolvedLocation?.DisplayName,
            DestinationId = resolvedLocation?.DestinationId,
            DestinationName = resolvedLocation?.DestinationName,
            AreaType = resolvedLocation?.AreaType,
            AreaId = resolvedLocation?.AreaId,
            AreaName = resolvedLocation?.DisplayName,
            Category = category,
            StrictCategoryMatch = strictCategory,
            FallbackBehavior = strictCategory ? "no_unrelated_cards" : "broaden_if_general",
            FollowUpIntent = followUpIntent ? "reuse_previous_area" : null,
            Tags = tags.OrderBy(item => item).ToList(),
            Categories = categories.OrderBy(item => item).ToList(),
            PlaceTypes = placeTypes.OrderBy(item => item).ToList(),
            StayTypes = stayTypes.OrderBy(item => item).ToList(),
            EventCategories = eventCategories.OrderBy(item => item).ToList(),
            QueryTerms = queryTerms,
            QueryText = queryTerms.Count == 0 ? null : string.Join(' ', queryTerms),
            RankingIntent = rankingIntent,
            UsesCurrentContext = usesCurrentContext,
            SearchCatalog = hasCatalogIntent && !clarificationNeeded,
            ClarificationNeeded = clarificationNeeded,
            ClarificationQuestion = clarificationNeeded
                ? language == "sr"
                    ? "Koji grad ili region da pretražim?"
                    : "Which city or region should I search?"
                : null
        };
    }

    private async Task<CatalogVocabulary> LoadCatalogVocabularyAsync(CancellationToken cancellationToken)
    {
        var tags = await _db.Tags
            .AsNoTracking()
            .Select(tag => new VocabularyItem(tag.Name, tag.Slug))
            .ToListAsync(cancellationToken);

        var placeTypes = await _db.PlaceTypes
            .AsNoTracking()
            .Select(type => new VocabularyItem(type.Name, type.Name))
            .ToListAsync(cancellationToken);

        var stayTypes = await _db.StayTypes
            .AsNoTracking()
            .Select(type => new VocabularyItem(type.Name, type.Name))
            .ToListAsync(cancellationToken);

        var eventTypes = await _db.EventTypes
            .AsNoTracking()
            .Select(type => new VocabularyItem(type.Name, type.Name))
            .ToListAsync(cancellationToken);

        return new CatalogVocabulary(tags, placeTypes, stayTypes, eventTypes);
    }

    private async Task<ResolvedAgentLocation?> ResolveLocationFromContextAsync(
        AgentContextOptionsDto? contextOptions,
        CancellationToken cancellationToken)
    {
        if (contextOptions is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(contextOptions.ConversationAreaType) &&
            contextOptions.ConversationAreaId is > 0)
        {
            var resolvedArea = await ResolveLocationFromAreaAsync(
                contextOptions.ConversationAreaType,
                contextOptions.ConversationAreaId.Value,
                cancellationToken);
            if (resolvedArea is not null)
            {
                return resolvedArea;
            }
        }

        if (contextOptions.ConversationDestinationId is > 0)
        {
            var destinationFromConversation = await _db.Destinations
                .AsNoTracking()
                .WherePublicDestinations()
                .Where(item => item.Id == contextOptions.ConversationDestinationId.Value)
                .Select(item => new { item.Id, item.Name })
                .SingleOrDefaultAsync(cancellationToken);

            if (destinationFromConversation is not null)
            {
                return new ResolvedAgentLocation(
                    destinationFromConversation.Id,
                    destinationFromConversation.Name,
                    "destination",
                    destinationFromConversation.Id,
                    destinationFromConversation.Name,
                    destinationFromConversation.Name,
                    AgentTextNormalizer.Tokenize(destinationFromConversation.Name).ToHashSet(StringComparer.Ordinal));
            }
        }

        if (contextOptions.DestinationId is > 0)
        {
            var destination = await _db.Destinations
                .AsNoTracking()
                .WherePublicDestinations()
                .Where(item => item.Id == contextOptions.DestinationId.Value)
                .Select(item => new { item.Id, item.Name })
                .SingleOrDefaultAsync(cancellationToken);

            if (destination is not null)
            {
                return new ResolvedAgentLocation(
                    destination.Id,
                    destination.Name,
                    "destination",
                    destination.Id,
                    destination.Name,
                    destination.Name,
                    AgentTextNormalizer.Tokenize(destination.Name).ToHashSet(StringComparer.Ordinal));
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

            var resolved = await ResolveLocationFromTextAsync(
                AgentTextNormalizer.Normalize(value),
                AgentTextNormalizer.Tokenize(value).ToHashSet(StringComparer.Ordinal),
                cancellationToken);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        return null;
    }

    private async Task<ResolvedAgentLocation?> ResolveLocationFromAreaAsync(
        string areaType,
        int areaId,
        CancellationToken cancellationToken)
    {
        var normalizedType = NormalizeAreaType(areaType);
        if (normalizedType is null || areaId <= 0)
        {
            return null;
        }

        if (normalizedType == "destination")
        {
            var destination = await _db.Destinations
                .AsNoTracking()
                .WherePublicDestinations()
                .Where(item => item.Id == areaId)
                .Select(item => new { item.Id, item.Name })
                .SingleOrDefaultAsync(cancellationToken);
            return destination is null
                ? null
                : new ResolvedAgentLocation(
                    destination.Id,
                    destination.Name,
                    "destination",
                    destination.Id,
                    destination.Name,
                    destination.Name,
                    AgentTextNormalizer.Tokenize(destination.Name).ToHashSet(StringComparer.Ordinal));
        }

        if (normalizedType == "location")
        {
            var location = await _db.Locations
                .AsNoTracking()
                .WherePublicLocations()
                .Where(item => item.Id == areaId)
                .Select(item => new
                {
                    item.Id,
                    item.Name,
                    item.DestinationId,
                    DestinationName = item.Destination.Name
                })
                .SingleOrDefaultAsync(cancellationToken);
            return location is null
                ? null
                : new ResolvedAgentLocation(
                    location.DestinationId,
                    location.DestinationName,
                    "location",
                    location.Id,
                    location.Name == location.DestinationName ? location.Name : location.Name + ", " + location.DestinationName,
                    location.Name,
                    AgentTextNormalizer.Tokenize(location.Name).ToHashSet(StringComparer.Ordinal));
        }

        if (normalizedType == "region")
        {
            var region = await _db.Regions
                .AsNoTracking()
                .Where(item => item.Id == areaId &&
                    item.Destinations.Any(destination => destination.Status == PublicVisibilityQueryExtensions.PublishedStatus))
                .Select(item => new { item.Id, item.Name })
                .SingleOrDefaultAsync(cancellationToken);
            return region is null
                ? null
                : new ResolvedAgentLocation(
                    null,
                    null,
                    "region",
                    region.Id,
                    region.Name,
                    region.Name,
                    AgentTextNormalizer.Tokenize(region.Name).ToHashSet(StringComparer.Ordinal));
        }

        if (normalizedType == "territory")
        {
            var territory = await _db.Territories
                .AsNoTracking()
                .Where(item => item.Id == areaId &&
                    item.Regions.Any(region =>
                        region.Destinations.Any(destination => destination.Status == PublicVisibilityQueryExtensions.PublishedStatus)))
                .Select(item => new { item.Id, item.Name })
                .SingleOrDefaultAsync(cancellationToken);
            return territory is null
                ? null
                : new ResolvedAgentLocation(
                    null,
                    null,
                    "territory",
                    territory.Id,
                    territory.Name,
                    territory.Name,
                    AgentTextNormalizer.Tokenize(territory.Name).ToHashSet(StringComparer.Ordinal));
        }

        return null;
    }

    private async Task<ResolvedAgentLocation?> ResolveLocationFromTextAsync(
        string normalizedText,
        IReadOnlySet<string> tokenSet,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return null;
        }

        var destinations = await _db.Destinations
            .AsNoTracking()
            .WherePublicDestinations()
            .Select(destination => new LocationCandidate
            {
                DestinationId = destination.Id,
                DestinationName = destination.Name,
                AreaType = "destination",
                AreaId = destination.Id,
                DisplayName = destination.Name,
                LocationText = destination.Name
            })
            .ToListAsync(cancellationToken);

        var locations = await _db.Locations
            .AsNoTracking()
            .WherePublicLocations()
            .Select(location => new LocationCandidate
            {
                DestinationId = location.DestinationId,
                DestinationName = location.Destination.Name,
                AreaType = "location",
                AreaId = location.Id,
                DisplayName = location.Name == location.Destination.Name
                    ? location.Destination.Name
                    : location.Name + ", " + location.Destination.Name,
                LocationText = location.Name
            })
            .ToListAsync(cancellationToken);

        var regions = await _db.Regions
            .AsNoTracking()
            .Where(region => region.Destinations.Any(destination => destination.Status == PublicVisibilityQueryExtensions.PublishedStatus))
            .Select(region => new LocationCandidate
            {
                DestinationId = null,
                DestinationName = null,
                AreaType = "region",
                AreaId = region.Id,
                DisplayName = region.Name,
                LocationText = region.Name
            })
            .ToListAsync(cancellationToken);

        var territories = await _db.Territories
            .AsNoTracking()
            .Where(territory => territory.Regions.Any(region =>
                region.Destinations.Any(destination => destination.Status == PublicVisibilityQueryExtensions.PublishedStatus)))
            .Select(territory => new LocationCandidate
            {
                DestinationId = null,
                DestinationName = null,
                AreaType = "territory",
                AreaId = territory.Id,
                DisplayName = territory.Name,
                LocationText = territory.Name
            })
            .ToListAsync(cancellationToken);

        return destinations
            .Concat(locations)
            .Concat(regions)
            .Concat(territories)
            .Select(candidate => ScoreLocationCandidate(candidate, normalizedText, tokenSet))
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.MatchLength)
            .Select(candidate => candidate.Location)
            .FirstOrDefault();
    }

    private static ScoredLocationCandidate ScoreLocationCandidate(
        LocationCandidate candidate,
        string normalizedText,
        IReadOnlySet<string> tokenSet)
    {
        var names = new[]
        {
            candidate.LocationText,
            candidate.DestinationName,
            candidate.DisplayName
        }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToList();

        var bestScore = 0;
        var bestLength = 0;
        var bestTokens = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in names)
        {
            var normalizedName = AgentTextNormalizer.Normalize(name);
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                continue;
            }

            var nameTokens = AgentTextNormalizer.Tokenize(name).ToHashSet(StringComparer.Ordinal);
            var score = 0;
            if (normalizedText == normalizedName)
            {
                score = 400;
            }
            else if (AgentTextNormalizer.ContainsPhrase(normalizedText, normalizedName))
            {
                score = 320;
            }
            else if (NameMatchesTokens(name, tokenSet))
            {
                score = 240 + nameTokens.Count * 30;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestLength = normalizedName.Length;
                bestTokens = nameTokens;
            }
        }

        return new ScoredLocationCandidate(
            new ResolvedAgentLocation(
                candidate.DestinationId,
                candidate.DestinationName,
                candidate.AreaType,
                candidate.AreaId,
                candidate.DisplayName,
                candidate.LocationText,
                bestTokens),
            bestScore,
            bestLength);
    }

    private static bool NameMatchesTokens(string name, IReadOnlySet<string> tokenSet)
    {
        var normalized = AgentTextNormalizer.Normalize(name);
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length > 0 &&
            words.All(word => AgentTextNormalizer.TokenVariants(word).Any(tokenSet.Contains));
    }

    private static bool MatchesLocationToken(string token, IReadOnlySet<string> locationTokens) =>
        locationTokens.Contains(token) ||
        AgentTextNormalizer.TokenVariants(token).Any(variant =>
            locationTokens.Contains(variant) ||
            (variant.Length > 4 && locationTokens.Any(locationToken =>
                locationToken.StartsWith(variant, StringComparison.Ordinal) ||
                variant.StartsWith(locationToken, StringComparison.Ordinal))));

    private static bool MatchesLocationText(string token, string? locationText)
    {
        var normalizedLocation = AgentTextNormalizer.Normalize(locationText);
        if (string.IsNullOrWhiteSpace(normalizedLocation))
        {
            return false;
        }

        return AgentTextNormalizer.TokenVariants(token).Any(variant =>
            normalizedLocation == variant ||
            (variant.Length > 4 && normalizedLocation.StartsWith(variant, StringComparison.Ordinal)));
    }

    private static HashSet<string> DetectEntityTypes(IReadOnlySet<string> tokens)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (entityType, aliases) in EntityAliases)
        {
            if (aliases.SelectMany(AgentTextNormalizer.TermVariants).Any(tokens.Contains))
            {
                result.Add(entityType);
            }
        }

        return result;
    }

    private static HashSet<string> DetectConcepts(string normalized, IReadOnlySet<string> tokens)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, concept) in Concepts)
        {
            if (concept.QueryAliases.Any(alias =>
                    AgentTextNormalizer.ContainsPhrase(normalized, AgentTextNormalizer.Normalize(alias)) ||
                    AgentTextNormalizer.TermVariants(alias).Any(tokens.Contains)))
            {
                result.Add(key);
            }
        }

        return result;
    }

    private static string ResolvePrimaryCategory(
        IReadOnlySet<string> concepts,
        IReadOnlySet<string> entityTypes)
    {
        if (concepts.Contains("restaurant") || concepts.Contains("cafe"))
        {
            return "restaurants";
        }

        if (concepts.Contains("beach"))
        {
            return "beaches";
        }

        if (entityTypes.Contains("stays"))
        {
            return "stays";
        }

        if (entityTypes.Contains("events") || concepts.Contains("festival"))
        {
            return "events";
        }

        if (concepts.Contains("landmark"))
        {
            return "attractions";
        }

        return "general";
    }

    private static bool IsStrictCategory(string category) =>
        category is "restaurants" or "beaches" or "stays" or "events" or "attractions";

    private static bool IsFollowUpIntent(IReadOnlySet<string> tokens) =>
        tokens.Count <= 4 &&
        (tokens.Contains("a") ||
         tokens.Contains("jos") ||
         tokens.Contains("još") ||
         tokens.Contains("more") ||
         tokens.Contains("nesto") ||
         tokens.Contains("nešto") ||
         tokens.Contains("sta") ||
         tokens.Contains("šta"));

    private static void AddMatchingTags(
        ISet<string> target,
        IReadOnlyCollection<VocabularyItem> tags,
        IReadOnlyCollection<string> aliases)
    {
        var aliasTerms = aliases.SelectMany(AgentTextNormalizer.TermVariants).ToHashSet(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            var name = AgentTextNormalizer.Normalize(tag.Name);
            var slug = AgentTextNormalizer.Normalize(tag.Key);
            if (aliasTerms.Contains(name) || aliasTerms.Contains(slug))
            {
                target.Add(tag.Key);
            }
        }
    }

    private static void AddMatchingTypes(
        ISet<string> target,
        IReadOnlyCollection<VocabularyItem> types,
        IReadOnlyCollection<string> aliases)
    {
        var aliasTerms = aliases.SelectMany(AgentTextNormalizer.TermVariants).ToHashSet(StringComparer.Ordinal);
        foreach (var type in types)
        {
            var name = AgentTextNormalizer.Normalize(type.Name);
            if (aliasTerms.Contains(name))
            {
                target.Add(type.Name);
            }
        }
    }

    private static HashSet<string> BuildGenericTokenSet()
    {
        var terms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var alias in EntityAliases.Values.SelectMany(value => value))
        {
            foreach (var term in AgentTextNormalizer.TermVariants(alias))
            {
                terms.Add(term);
            }
        }

        foreach (var concept in Concepts.Values)
        {
            foreach (var alias in concept.QueryAliases)
            {
                foreach (var term in AgentTextNormalizer.TermVariants(alias))
                {
                    terms.Add(term);
                }
            }
        }

        foreach (var stopWord in StopWords)
        {
            foreach (var term in AgentTextNormalizer.TermVariants(stopWord))
            {
                terms.Add(term);
            }
        }

        return terms;
    }

    private static string DetectLanguage(IReadOnlySet<string> tokens, string? requestedLanguage)
    {
        if (!string.IsNullOrWhiteSpace(requestedLanguage) &&
            requestedLanguage.StartsWith("sr", StringComparison.OrdinalIgnoreCase))
        {
            return "sr";
        }

        return tokens.Overlaps(SerbianTokens) ? "sr" : "en";
    }

    private static string? CleanContextText(string? value)
    {
        var cleaned = value?.Trim();
        if (string.IsNullOrWhiteSpace(cleaned) ||
            string.Equals(cleaned, "Explore", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(cleaned, "Nearby", StringComparison.OrdinalIgnoreCase))
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

    private sealed record ConceptDefinition(
        string[] QueryAliases,
        string[] TagAliases,
        string[] PlaceTypeAliases,
        string[] EventTypeAliases,
        string[] CategoryAliases);

    private sealed record VocabularyItem(string Name, string Key);

    private sealed record CatalogVocabulary(
        List<VocabularyItem> Tags,
        List<VocabularyItem> PlaceTypes,
        List<VocabularyItem> StayTypes,
        List<VocabularyItem> EventTypes);

    private sealed class LocationCandidate
    {
        public int? DestinationId { get; init; }
        public string? DestinationName { get; init; }
        public string AreaType { get; init; } = "destination";
        public int AreaId { get; init; }
        public string DisplayName { get; init; } = string.Empty;
        public string LocationText { get; init; } = string.Empty;
    }

    private sealed record ResolvedAgentLocation(
        int? DestinationId,
        string? DestinationName,
        string AreaType,
        int AreaId,
        string DisplayName,
        string LocationText,
        HashSet<string> NormalizedTokens);

    private sealed record ScoredLocationCandidate(
        ResolvedAgentLocation Location,
        int Score,
        int MatchLength);
}

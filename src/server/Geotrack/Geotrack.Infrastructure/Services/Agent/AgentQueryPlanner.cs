using Geotrack.Application.DTOs.Agent;
using Geotrack.Application.DTOs.GeoTrackTools;
using Geotrack.Application.Interfaces.Service;
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
        "obici", "obići", "mozes", "možeš", "li", "imaju", "neki", "neka", "neku", "od",
        "zanimljivo", "zanimljive", "zanimljivosti", "vidi", "vidis", "vidiš", "raditi",
        "things", "see"
    };

    private static readonly HashSet<string> RankingTokens = new(StringComparer.Ordinal)
    {
        "best", "top", "popular", "recommended", "recommend", "najbolje", "najbolji", "preporuci", "preporuči"
    };

    private static readonly HashSet<string> SerbianTokens = new(StringComparer.Ordinal)
    {
        "plaza", "plaze", "restoran", "restorani", "restorana", "dogadjaj", "dogadjaji",
        "smestaj", "smjestaj", "kafic", "kafici", "znamenitost", "znamenitosti",
        "preporuci", "sta", "ima", "videti", "vidjeti", "vidi", "obici",
        "aktivnosti", "blizini", "mojoj", "moze", "vazi", "najbolje", "najbolji",
        "atrakcija", "atrakcije", "atrakciju"
    };

    private static readonly string[] CategoryPriority =
    [
        "FOOD_DRINK",
        "BEACHES",
        "STAYS",
        "EVENTS",
        "SIGHTS_ATTRACTIONS",
        "GENERAL_DISCOVERY"
    ];

    private static readonly string[] SpecialConceptIds =
    [
        "NEARBY",
        "FOLLOW_UP_ACCEPTANCE"
    ];

    private readonly AppDbContext _db;
    private readonly IAgentTaxonomyProvider _taxonomyProvider;
    private readonly IAgentAreaResolver _areaResolver;

    public AgentQueryPlanner(
        AppDbContext db,
        IAgentTaxonomyProvider taxonomyProvider,
        IAgentAreaResolver areaResolver)
    {
        _db = db;
        _taxonomyProvider = taxonomyProvider;
        _areaResolver = areaResolver;
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
        var taxonomy = await _taxonomyProvider.GetTaxonomyAsync(cancellationToken);
        var vocabulary = await LoadCatalogVocabularyAsync(cancellationToken);
        var matchedConcepts = MatchConcepts(taxonomy, normalized, tokenSet);

        var nearbyIntent = HasConcept(matchedConcepts, "NEARBY");
        var hasUserCoordinates = HasUsableCoordinates(contextOptions?.Latitude, contextOptions?.Longitude);
        var acceptanceIntent = IsAcceptanceIntent(matchedConcepts, normalized, tokenSet);
        var hasNewCategoryIntent = matchedConcepts.Any(concept => !IsSpecialConcept(concept.Id));

        var messageLocation = await _areaResolver.ResolveFromTextAsync(original, includeEntities: false, cancellationToken);
        var resolvedLocation = messageLocation ?? (nearbyIntent ? null : await _areaResolver.ResolveFromContextAsync(contextOptions, cancellationToken));
        var usesCurrentContext = messageLocation is null && resolvedLocation is not null;
        var usesUserLocation = nearbyIntent && messageLocation is null && hasUserCoordinates;
        var pendingConcept = ResolvePendingConcept(
            taxonomy,
            contextOptions,
            messageLocation,
            hasNewCategoryIntent,
            acceptanceIntent,
            nearbyIntent);
        var pendingCompletion = pendingConcept is not null;

        var followUpIntent = messageLocation is null && IsFollowUpIntent(tokenSet);
        var primaryConcept = pendingConcept ?? ResolvePrimaryConcept(matchedConcepts, contextOptions, followUpIntent, acceptanceIntent);
        var category = primaryConcept?.Category ?? "general";
        var strictCategory = primaryConcept?.Strict == true;
        var fallbackAllowed = primaryConcept?.FallbackAllowed ?? !strictCategory;
        if (pendingCompletion)
        {
            strictCategory = contextOptions?.PendingStrictCategory ?? strictCategory;
            fallbackAllowed = contextOptions?.PendingFallbackAllowed ?? fallbackAllowed;
        }
        var categoryGroup = primaryConcept is null || IsSpecialConcept(primaryConcept.Id)
            ? "GENERAL_DISCOVERY"
            : primaryConcept.Id;

        var entityTypes = ResolveEntityTypes(primaryConcept, matchedConcepts, resolvedLocation is not null || usesUserLocation);
        var rankingIntent = tokenSet.Overlaps(RankingTokens) ? "top" : "all";

        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var placeTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stayTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var eventCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (primaryConcept is not null && !IsSpecialConcept(primaryConcept.Id))
        {
            foreach (var categoryAlias in primaryConcept.CategoryAliases)
            {
                categories.Add(categoryAlias);
            }

            AddMatchingTags(tags, vocabulary.Tags, primaryConcept.Tags);
            AddMatchingTypes(placeTypes, vocabulary.PlaceTypes, primaryConcept.PlaceTypes);
            AddMatchingTypes(stayTypes, vocabulary.StayTypes, primaryConcept.StayTypes);
            AddMatchingTypes(eventCategories, vocabulary.EventTypes, primaryConcept.EventCategories);
        }

        var genericTokens = BuildGenericTokenSet(taxonomy);
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

        if (ShouldClearDiscoveryQuery(primaryConcept, queryTerms, resolvedLocation, nearbyIntent))
        {
            queryTerms.Clear();
        }

        if (acceptanceIntent && resolvedLocation is not null)
        {
            queryTerms.Clear();
            strictCategory = false;
            fallbackAllowed = true;
            category = "general";
            categoryGroup = "GENERAL_DISCOVERY";
            entityTypes = ["places", "events"];
        }

        var hasCatalogIntent = entityTypes.Count > 0 ||
            matchedConcepts.Any(concept => !IsSpecialConcept(concept.Id)) ||
            rankingIntent == "top" ||
            resolvedLocation is not null ||
            usesUserLocation;
        var genericDiscoveryWithoutSpecificTerm = hasCatalogIntent &&
            resolvedLocation is null &&
            !usesUserLocation &&
            queryTerms.Count == 0 &&
            (entityTypes.Count > 0 ||
             matchedConcepts.Any(concept => !IsSpecialConcept(concept.Id)) ||
             rankingIntent == "top");

        var language = DetectLanguage(tokenSet, contextOptions?.Language);
        var missingNearbyLocation = nearbyIntent && !hasUserCoordinates && messageLocation is null;
        var clarificationNeeded = genericDiscoveryWithoutSpecificTerm || missingNearbyLocation || (messageLocation?.IsAmbiguous == true);

        return new GeoTrackAgentQueryPlanDto
        {
            Intent = clarificationNeeded
                ? "clarification_needed"
                : hasCatalogIntent ? "catalog_search" : "general_help",
            Language = language,
            OriginalMessage = original,
            NormalizedMessage = normalized,
            CategoryGroup = categoryGroup,
            EntityTypes = entityTypes.Count == 0 ? [] : entityTypes.ToList(),
            RequestedEntityTypes = entityTypes.Count == 0 ? [] : entityTypes.ToList(),
            AreaPhrase = messageLocation?.LocationText,
            LocationText = resolvedLocation?.LocationText,
            LocationUsed = resolvedLocation?.DisplayName,
            DestinationId = resolvedLocation?.DestinationId,
            DestinationName = resolvedLocation?.DestinationName,
            AreaType = resolvedLocation?.AreaType,
            AreaId = resolvedLocation?.AreaId,
            AreaName = resolvedLocation?.DisplayName,
            Category = category,
            StrictCategoryMatch = strictCategory,
            FallbackBehavior = fallbackAllowed ? "broaden_if_general" : "no_unrelated_cards",
            FollowUpIntent = pendingCompletion
                ? "complete_pending_clarification"
                : acceptanceIntent ? "execute_previous_fallback" : followUpIntent ? "reuse_previous_area" : null,
            UsesUserLocation = usesUserLocation,
            NearbyIntent = nearbyIntent,
            HasUserCoordinates = hasUserCoordinates,
            FallbackAllowed = fallbackAllowed,
            ToolPlan = usesUserLocation
                ? "geotrack_find_nearby_public_content"
                : "taxonomy_area_catalog_search",
            MaxResults = 10,
            ExplanationForLogsOnly = BuildPlanExplanation(categoryGroup, resolvedLocation, usesCurrentContext, usesUserLocation, acceptanceIntent, pendingCompletion),
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
                ? BuildClarificationQuestion(language, missingNearbyLocation, messageLocation?.IsAmbiguous == true)
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

    private static List<AgentTaxonomyConceptDto> MatchConcepts(
        AgentTaxonomyDto taxonomy,
        string normalized,
        IReadOnlySet<string> tokens)
    {
        return taxonomy.Concepts
            .Where(concept => concept.Aliases.Any(alias =>
            {
                var normalizedAlias = AgentTextNormalizer.Normalize(alias);
                if (normalizedAlias.Contains(' ', StringComparison.Ordinal))
                {
                    var aliasTokens = AgentTextNormalizer.Tokenize(alias).ToHashSet(StringComparer.Ordinal);
                    return AgentTextNormalizer.ContainsPhrase(normalized, normalizedAlias) ||
                        (aliasTokens.Count > 0 && aliasTokens.All(tokens.Contains));
                }

                return AgentTextNormalizer.ContainsPhrase(normalized, normalizedAlias) ||
                    AgentTextNormalizer.TermVariants(alias).Any(tokens.Contains);
            }))
            .ToList();
    }

    private static AgentTaxonomyConceptDto? ResolvePrimaryConcept(
        IReadOnlyCollection<AgentTaxonomyConceptDto> matchedConcepts,
        AgentContextOptionsDto? contextOptions,
        bool followUpIntent,
        bool acceptanceIntent)
    {
        if (acceptanceIntent)
        {
            return matchedConcepts.FirstOrDefault(concept => concept.Id == "GENERAL_DISCOVERY") ??
                matchedConcepts.FirstOrDefault(concept => concept.Id == "FOLLOW_UP_ACCEPTANCE");
        }

        foreach (var id in CategoryPriority)
        {
            var concept = matchedConcepts.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (concept is not null)
            {
                return concept;
            }
        }

        if (followUpIntent && !string.IsNullOrWhiteSpace(contextOptions?.ConversationCategory))
        {
            var normalizedCategory = AgentTextNormalizer.Normalize(contextOptions.ConversationCategory);
            return matchedConcepts.FirstOrDefault(concept =>
                    string.Equals(AgentTextNormalizer.Normalize(concept.Category), normalizedCategory, StringComparison.Ordinal)) ??
                null;
        }

        return null;
    }

    private static AgentTaxonomyConceptDto? ResolvePendingConcept(
        AgentTaxonomyDto taxonomy,
        AgentContextOptionsDto? contextOptions,
        AgentAreaResolutionDto? messageLocation,
        bool hasNewCategoryIntent,
        bool acceptanceIntent,
        bool nearbyIntent)
    {
        if (contextOptions is null ||
            !string.Equals(contextOptions.PendingReason, "missing_area", StringComparison.OrdinalIgnoreCase) ||
            messageLocation is null ||
            hasNewCategoryIntent ||
            acceptanceIntent ||
            nearbyIntent ||
            string.IsNullOrWhiteSpace(contextOptions.PendingCategoryGroup))
        {
            return null;
        }

        return taxonomy.Concepts.FirstOrDefault(concept =>
            !IsSpecialConcept(concept.Id) &&
            string.Equals(concept.Id, contextOptions.PendingCategoryGroup, StringComparison.OrdinalIgnoreCase));
    }

    private static HashSet<string> ResolveEntityTypes(
        AgentTaxonomyConceptDto? primaryConcept,
        IReadOnlyCollection<AgentTaxonomyConceptDto> matchedConcepts,
        bool hasAreaOrUserLocation)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (primaryConcept is not null && !IsSpecialConcept(primaryConcept.Id))
        {
            result.UnionWith(primaryConcept.TargetEntityTypes
                .Select(NormalizeEntityType)
                .Where(value => value is not null)
                .Select(value => value!));
        }

        foreach (var concept in matchedConcepts.Where(concept => !IsSpecialConcept(concept.Id)))
        {
            result.UnionWith(concept.TargetEntityTypes
                .Select(NormalizeEntityType)
                .Where(value => value is not null)
                .Select(value => value!));
        }

        if (result.Count == 0 && hasAreaOrUserLocation)
        {
            result.Add("all");
        }

        return result;
    }

    private static string? NormalizeEntityType(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "all" => "all",
            "place" or "places" => "places",
            "stay" or "stays" => "stays",
            "event" or "events" => "events",
            _ => null
        };
    }

    private static bool HasConcept(IEnumerable<AgentTaxonomyConceptDto> concepts, string id) =>
        concepts.Any(concept => string.Equals(concept.Id, id, StringComparison.OrdinalIgnoreCase));

    private static bool IsSpecialConcept(string id) =>
        SpecialConceptIds.Contains(id, StringComparer.OrdinalIgnoreCase);

    private static bool IsAcceptanceIntent(
        IReadOnlyCollection<AgentTaxonomyConceptDto> matchedConcepts,
        string normalized,
        IReadOnlySet<string> tokens)
    {
        if (!HasConcept(matchedConcepts, "FOLLOW_UP_ACCEPTANCE"))
        {
            return false;
        }

        return normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length <= 3 ||
            tokens.All(token => StopWords.Contains(token) || token.Length <= 2 || token is "ajde" or "moze" or "vazi" or "ok");
    }

    private static bool IsFollowUpIntent(IReadOnlySet<string> tokens) =>
        tokens.Count <= 5 &&
        (tokens.Contains("a") ||
         tokens.Contains("jos") ||
         tokens.Contains("još") ||
         tokens.Contains("more") ||
         tokens.Contains("nesto") ||
         tokens.Contains("nešto") ||
         tokens.Contains("sta") ||
         tokens.Contains("šta") ||
         tokens.Contains("od"));

    private static bool ShouldClearDiscoveryQuery(
        AgentTaxonomyConceptDto? concept,
        IReadOnlyCollection<string> queryTerms,
        AgentAreaResolutionDto? resolvedLocation,
        bool nearbyIntent)
    {
        if (queryTerms.Count == 0 || (resolvedLocation is null && !nearbyIntent) || concept is null)
        {
            return false;
        }

        return concept.Id is "SIGHTS_ATTRACTIONS" or "GENERAL_DISCOVERY" &&
            queryTerms.All(IsDiscoveryOnlyTerm);
    }

    private static bool IsDiscoveryOnlyTerm(string term) =>
        StopWords.Contains(term) ||
        term is "aktivnost" or "aktivnosti" or "zanimljivo" or "zanimljive" or "zanimljivosti" or
            "vidi" or "vidim" or "videti" or "vidjeti" or "raditi" or "things" or "see";

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

    private static bool HasUsableCoordinates(double? latitude, double? longitude) =>
        latitude.HasValue &&
        longitude.HasValue &&
        !double.IsNaN(latitude.Value) &&
        !double.IsNaN(longitude.Value) &&
        !double.IsInfinity(latitude.Value) &&
        !double.IsInfinity(longitude.Value) &&
        latitude.Value >= -90 &&
        latitude.Value <= 90 &&
        longitude.Value >= -180 &&
        longitude.Value <= 180 &&
        (latitude.Value != 0d || longitude.Value != 0d);

    private static string BuildClarificationQuestion(string language, bool missingNearbyLocation, bool ambiguousArea) =>
        ambiguousArea
            ? language == "sr"
                ? "Na koju tačno oblast misliš?"
                : "Which exact area do you mean?"
            : missingNearbyLocation
                ? language == "sr"
                    ? "Uključi lokaciju ili izaberi destinaciju koju da pretražim."
                    : "Enable location or choose a destination I should search."
                : language == "sr"
                    ? "Koji grad ili region da pretražim?"
                    : "Which city or region should I search?";

    private static string BuildPlanExplanation(
        string categoryGroup,
        AgentAreaResolutionDto? resolvedLocation,
        bool usesCurrentContext,
        bool usesUserLocation,
        bool acceptanceIntent,
        bool pendingCompletion) =>
        usesUserLocation
            ? $"nearby:{categoryGroup}"
            : resolvedLocation is null
                ? $"category:{categoryGroup};area:missing;fallbackAccepted:{acceptanceIntent}"
                : $"category:{categoryGroup};area:{resolvedLocation.AreaType};context:{usesCurrentContext};fallbackAccepted:{acceptanceIntent};pendingCompleted:{pendingCompletion}";

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

    private static HashSet<string> BuildGenericTokenSet(AgentTaxonomyDto taxonomy)
    {
        var terms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var alias in taxonomy.Concepts.SelectMany(concept => concept.Aliases))
        {
            foreach (var term in AgentTextNormalizer.TermVariants(alias))
            {
                terms.Add(term);
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

    private sealed record VocabularyItem(string Name, string Key);

    private sealed record CatalogVocabulary(
        List<VocabularyItem> Tags,
        List<VocabularyItem> PlaceTypes,
        List<VocabularyItem> StayTypes,
        List<VocabularyItem> EventTypes);
}

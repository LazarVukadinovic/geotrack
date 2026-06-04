using Geotrack.Application.DTOs.GeoTrackTools;

namespace Geotrack.Application.DTOs.Agent;

public class AgentDatabaseCapabilitiesDto
{
    public bool SupportsPostgis { get; set; }
    public bool SupportsPgTrgm { get; set; }
    public bool SupportsUnaccent { get; set; }
    public bool SupportsVector { get; set; }
    public Dictionary<string, string> ExtensionVersions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class AgentTaxonomyDto
{
    public string Version { get; set; } = string.Empty;
    public List<AgentTaxonomyConceptDto> Concepts { get; set; } = new();
}

public class AgentTaxonomyConceptDto
{
    public string Id { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string DisplayLabel { get; set; } = string.Empty;
    public bool Strict { get; set; }
    public bool FallbackAllowed { get; set; } = true;
    public List<string> Aliases { get; set; } = new();
    public List<string> TargetEntityTypes { get; set; } = new();
    public List<string> CategoryAliases { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public List<string> NegativeTags { get; set; } = new();
    public List<string> PlaceTypes { get; set; } = new();
    public List<string> StayTypes { get; set; } = new();
    public List<string> EventCategories { get; set; } = new();
    public List<string> FallbackCategoryGroups { get; set; } = new();
    public AgentTaxonomyRankingWeightsDto RankingWeights { get; set; } = new();
}

public class AgentTaxonomyRankingWeightsDto
{
    public double TypeBoost { get; set; } = 1;
    public double TagBoost { get; set; } = 1;
    public double FreshnessBoost { get; set; } = 1;
    public double DistanceBoost { get; set; } = 1;
}

public class AgentAreaResolutionDto
{
    public string AreaType { get; set; } = string.Empty;
    public int AreaId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string LocationText { get; set; } = string.Empty;
    public int? DestinationId { get; set; }
    public string? DestinationName { get; set; }
    public List<string> ParentNames { get; set; } = new();
    public double Confidence { get; set; }
    public bool IsAmbiguous { get; set; }
    public List<AgentAreaCandidateDto> Candidates { get; set; } = new();
    public HashSet<string> NormalizedTokens { get; set; } = new(StringComparer.Ordinal);
}

public class AgentAreaCandidateDto
{
    public string AreaType { get; set; } = string.Empty;
    public int AreaId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public double Confidence { get; set; }
}

public class AgentSemanticSearchRequestDto
{
    public string Query { get; set; } = string.Empty;
    public string? AreaType { get; set; }
    public int? AreaId { get; set; }
    public string? CategoryGroup { get; set; }
    public int Limit { get; set; } = 10;
}

public class AgentSemanticSearchResponseDto
{
    public bool IsEnabled { get; set; }
    public List<GeoTrackToolItemDto> Results { get; set; } = new();
}

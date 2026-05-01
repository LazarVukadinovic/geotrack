using System.ComponentModel.DataAnnotations;
using Geotrack.Domain.Entities.Reviews;

namespace Geotrack.Application.DTOs.Moderation;

public sealed record ModerationTermRule(
    int Id,
    string NormalizedTerm,
    ModerationTermMatchType MatchType);

public class ModerationTermDto
{
    public int Id { get; set; }
    public string Term { get; set; } = string.Empty;
    public string NormalizedTerm { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string MatchType { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int Severity { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public int? UpdatedByUserId { get; set; }
}

public class GetModerationTermsResponseDto
{
    public List<ModerationTermDto> Terms { get; set; } = new();
}

public class CreateModerationTermRequestDto
{
    [Required]
    [MaxLength(160)]
    public string Term { get; set; } = string.Empty;

    [Required]
    [MaxLength(10)]
    public string LanguageCode { get; set; } = "en";

    [MaxLength(40)]
    public string Category { get; set; } = "Profanity";

    [MaxLength(40)]
    public string MatchType { get; set; } = "Exact";

    public bool IsActive { get; set; } = true;

    [Range(1, 5)]
    public int Severity { get; set; } = 2;
}

public class UpdateModerationTermRequestDto
{
    [Required]
    [MaxLength(160)]
    public string Term { get; set; } = string.Empty;

    [Required]
    [MaxLength(10)]
    public string LanguageCode { get; set; } = "en";

    [MaxLength(40)]
    public string Category { get; set; } = "Profanity";

    [MaxLength(40)]
    public string MatchType { get; set; } = "Exact";

    public bool IsActive { get; set; } = true;

    [Range(1, 5)]
    public int Severity { get; set; } = 2;
}

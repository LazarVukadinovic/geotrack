using System.ComponentModel.DataAnnotations;

namespace Geotrack.Domain.Entities.Core;

public class StayAmenity
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? IconKey { get; set; }

    public ICollection<StayStayAmenity> StayAmenities { get; set; } = new List<StayStayAmenity>();

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
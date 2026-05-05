using System.ComponentModel.DataAnnotations;

namespace Geotrack.Domain.Entities.Core;

public class StayType
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

    public ICollection<Stay> Stays { get; set; } = new List<Stay>();

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
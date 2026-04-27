using Geotrack.Domain.Entities.Core;

namespace Geotrack.Domain.Entities.Users;

public class StayContribution
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public StayContributionStatus Status { get; set; } = StayContributionStatus.PendingReview;

    public int? ReviewedByUserId { get; set; }
    public User? ReviewedByUser { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }

    public int? PublishedPlaceId { get; set; }
    public Place? PublishedPlace { get; set; }

    public string Name { get; set; } = string.Empty;
    public string StayType { get; set; } = "Hotel";
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public string? CoverImageUrl { get; set; }
    public string ImagePosition { get; set; } = "50% 50%";
    public string ImageSize { get; set; } = "cover";

    public string Description { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public decimal? PricePerNight { get; set; }

    public string TagsJson { get; set; } = "[]";
    public string AmenitiesJson { get; set; } = "[]";
    public string BookingPartnersJson { get; set; } = "[]";

    public DateTime SubmittedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

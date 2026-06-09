using Geotrack.Domain.Entities.Core;

namespace Geotrack.Domain.Entities.Users;

public class PlaceContribution
{
    public int Id { get; set; }

    public PlaceContributionType Type { get; set; } = PlaceContributionType.CreatePlace;
    public PlaceContributionStatus Status { get; set; } = PlaceContributionStatus.PendingReview;

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int? PlaceId { get; set; }
    public Place? Place { get; set; }

    public int? ReviewedByUserId { get; set; }
    public User? ReviewedByUser { get; set; }

    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }

    public string OriginalSnapshotJson { get; set; } = "{}";

    public int? PlaceTypeId { get; set; }
    public PlaceType? PlaceType { get; set; }

    public int? LocationId { get; set; }
    public Location? Location { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Subtitle { get; set; }

    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? Country { get; set; }
    public string? OpeningHours { get; set; }
    public bool OperatingHoursEnabled { get; set; }
    public string? OperatingHoursJson { get; set; }

    public string Description { get; set; } = string.Empty;

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public string? ContactEmail { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Website { get; set; }

    public int? RestaurantTypeId { get; set; }
    public RestaurantType? RestaurantType { get; set; }

    public int? StayTypeId { get; set; }
    public StayType? StayType { get; set; }

    public decimal? PricePerNight { get; set; }
    public string? CheckInTime { get; set; }
    public string? CheckOutTime { get; set; }

    public string TagsJson { get; set; } = "[]";
    public string AmenitiesJson { get; set; } = "[]";
    public string StayAmenitiesJson { get; set; } = "[]";
    public string BookingPartnersJson { get; set; } = "[]";

    public string? OwnershipProofDocumentUrl { get; set; }
    public string? OwnershipProofOriginalFileName { get; set; }
    public string? OwnershipProofContentType { get; set; }
    public DateTime? OwnershipProofUploadedAt { get; set; }

    public ICollection<PlaceContributionImage> Images { get; set; } = new List<PlaceContributionImage>();

    public DateTime SubmittedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
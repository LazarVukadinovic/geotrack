using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Tags;
using Geotrack.Domain.Entities.Users;

namespace Geotrack.Domain.Entities.Core;

public class Place
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? InternalReferenceId { get; set; }
    public string Status { get; set; } = "Published";
    public string? Subtitle { get; set; }
    public string? Address { get; set; }
    public string? OpeningHours { get; set; }
    public bool OperatingHoursEnabled { get; set; }
    public string? OperatingHoursJson { get; set; }

    public double? AverageRating { get; set; }
    public int ReviewCount { get; set; }

    public int PlaceTypeId { get; set; }
    public PlaceType PlaceType { get; set; } = null!;

    public int LocationId { get; set; }
    public Location Location { get; set; } = null!;

    public Stay? Stay { get; set; }
    public Restaurant? Restaurant { get; set; }

    public ICollection<Event> Events { get; set; } = new List<Event>();
    public ICollection<PlaceTag> PlaceTags { get; set; } = new List<PlaceTag>();
    public ICollection<PlaceReview> PlaceReviews { get; set; } = new List<PlaceReview>();
    public ICollection<PlaceAmenity> PlaceAmenities { get; set; } = new List<PlaceAmenity>();
    public ICollection<PlaceBookingPartner> PlaceBookingPartners { get; set; } = new List<PlaceBookingPartner>();
    public ICollection<UserPlace> UserPlaces { get; set; } = new List<UserPlace>();

    public string Description { get; set; } = string.Empty;

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    public string? ContactEmail { get; set; }
    public string? Number { get; set; }
    public string? Website { get; set; }
    public string? AdmissionPriceRange { get; set; }
    public string? BookingExternalLink { get; set; }
    public string? BestTimeToVisit { get; set; }
    public string? VisitorTips { get; set; }
    public string? AccessNotes { get; set; }
    public string? AccessibilityNotes { get; set; }

    public int? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }

    public ICollection<EntityImage> Images { get; set; } = new List<EntityImage>();
    public ICollection<PlaceContribution> PlaceContributions { get; set; } = new List<PlaceContribution>();
}

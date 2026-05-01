using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Tags;

namespace Geotrack.Domain.Entities.Core;

public class Place
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? ImageUrl { get; set; }
    public string? Address { get; set; }
    public string? OpeningHours { get; set; }
    public double? AverageRating { get; set; }
    public int ReviewCount { get; set; }

    // Hotel/Stay specific fields
    public decimal? PricePerNight { get; set; }

    public int PlaceTypeId { get; set; }
    public PlaceType PlaceType { get; set; } = null!;

    public int LocationId { get; set; }
    public Location Location { get; set; } = null!;

    public ICollection<Event> Events { get; set; } = new List<Event>();
    public ICollection<PlaceTag> PlaceTags { get; set; } = new List<PlaceTag>();
    public ICollection<PlaceReview> PlaceReviews { get; set; } = new List<PlaceReview>();
    public ICollection<PlaceAmenity> PlaceAmenities { get; set; } = new List<PlaceAmenity>();
    public ICollection<PlaceBookingPartner> PlaceBookingPartners { get; set; } = new List<PlaceBookingPartner>();

    public string Description { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }

    public string? ContactEmail { get; set; } = string.Empty;
    public string? Number { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

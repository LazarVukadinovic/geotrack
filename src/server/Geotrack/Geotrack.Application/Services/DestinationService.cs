using Geotrack.Application.DTOs.Destionation;
using Geotrack.Application.DTOs.Explore;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Core;

namespace Geotrack.Application.Services;

public class DestinationService : IDestinationService
{
    private readonly IDestinationRepository _destinationRepository;

    public DestinationService(IDestinationRepository destinationRepository)
    {
        _destinationRepository = destinationRepository;
    }

    public async Task<ExploreByLocationResponseDto> ExploreByLocationAsync(ExploreByLocationQueryDto query)
    {
        var destinations = await _destinationRepository.GetAllWithDetailsAsync();

        var nearest = destinations
            .Select(d => new
            {
                Destination = d,
                Distance = HaversineDistance(
                    query.Latitude,
                    query.Longitude,
                    d.Latitude,
                    d.Longitude)
            })
            .OrderBy(x => x.Distance)
            .FirstOrDefault();

        if (nearest == null)
        {
            return new ExploreByLocationResponseDto
            {
                SelectedDestination = null,
                Sections = new List<ExploreSectionDto>()
            };
        }

        var destination = nearest.Destination;

        var destinationDto = new ExploreDestinationDto
        {
            Id = destination.Id,
            Name = destination.Name,
            Latitude = destination.Latitude,
            Longitude = destination.Longitude
        };

        var sectionKeys = destination.DestinationTags?
            .Where(x => x.Tag != null && !string.IsNullOrWhiteSpace(x.Tag.Name))
            .Select(x => x.Tag!.Name.Trim().ToLower())
            .Distinct()
            .Take(3)
            .ToList() ?? new List<string>();

        if (!sectionKeys.Any())
        {
            sectionKeys = new List<string> { "nature", "history", "culture" };
        }

        var allLocations = destination.Locations?.ToList() ?? new List<Location>();
        var allPlaces = allLocations
            .SelectMany(l => l.Places ?? new List<Place>())
            .ToList();

        var sections = new List<ExploreSectionDto>();

        foreach (var key in sectionKeys)
        {
            sections.Add(new ExploreSectionDto
            {
                Key = key,
                Title = ToTitle(key),
                Locations = allLocations
                    .Take(4)
                    .Select(l => new ExploreLocationItemDto
                    {
                        Id = l.Id,
                        Name = l.Name,
                        Latitude = l.Latitude,
                        Longitude = l.Longitude
                    })
                    .ToList(),
                Places = allPlaces
                    .Take(6)
                    .Select(p => new ExplorePlaceItemDto
                    {
                        Id = p.Id,
                        Name = p.Name,
                        LocationId = p.LocationId
                    })
                    .ToList()
            });
        }

        return new ExploreByLocationResponseDto
        {
            SelectedDestination = destinationDto,
            Sections = sections
        };
    }

    private static string ToTitle(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return "Explore";

        return char.ToUpper(key[0]) + key[1..];
    }

    private static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusKm = 6371.0;

        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);

        var a =
            Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
            Math.Cos(DegreesToRadians(lat1)) *
            Math.Cos(DegreesToRadians(lat2)) *
            Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return earthRadiusKm * c;
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }

    public async Task<GetDestinationResponseDto> GetDestinationDetailsAsync(int destinationId)
    {
        var destination = await _destinationRepository.GetWithDetailsAsync(destinationId);

        if (destination == null)
            throw new Exception("Destinacija nije pronadjena");


        var reviewcount = destination.DestinationReviews?.Count ?? 0;
        var avgrating = reviewcount > 0 ? destination.DestinationReviews!.Average(r => r.Rating) : 0.0;


        var savedCollectionIds = new List<int>();

        var isSaved = false;// ne postoji jos model kolekcija

        var allLocations = destination.Locations?.ToList() ?? new List<Location>();

        var allPlaces = allLocations.SelectMany(l => l.Places ?? new List<Place>()).ToList();

        var locationSection = new DestinationSectionPreviewDto
        {
            Key = "locations",
            Title = "Locations",
            Locations = allLocations.Take(5).Select(l => new DestinationSectionLocationItemDto { Id = l.Id, Name = l.Name, Latitude = l.Latitude, Longitude = l.Longitude }).ToList(),
            Places = allPlaces.Take(5).Select(p => new DestinationSectionPlaceItemDto { Id = p.Id, Name = p.Name, LocationId = p.LocationId }).ToList()
        };


        return new GetDestinationResponseDto
        {
            Id = destination.Id,
            Name = destination.Name,
            Description = destination.Description,
            Latitude = destination.Latitude, Longitude = destination.Longitude,
            DestinationTypeId = destination.DestinationTypeId,
            RegionId = destination.RegionId,
            CreatedAt = destination.CreatedAt,
            UpdatedAt = destination.UpdatedAt ?? DateTime.MinValue,
            AverageRating = avgrating,
            ReviewsCount = reviewcount,
            IsSaved = isSaved,
            SavedCollectionIds = savedCollectionIds,
            Sections = new List<DestinationSectionPreviewDto> { locationSection }

        };


    }

    public async Task<GetDestinationSectionResponseDto> GetDestinationSectionAsync(int destinationId, string sectionKey)
    {
        var destination = await _destinationRepository.GetWithDetailsAsync(destinationId);

        if (destination == null)
        {
            throw new Exception("Destinacija nije pronadjena");
        }

        var allLocations = destination.Locations?.ToList() ?? new List<Location>();
        var allPlaces = allLocations.SelectMany(l => l.Places ?? new List<Place>()).ToList();

        var (filteredLocations, filteredPlaces, title) = sectionKey.ToLower() switch
        {
            "locations" => (
                allLocations,
                allPlaces,
                "Locations"
            ),
            _ => throw new Exception($"Nepoznata sekcija: {sectionKey}")
        };


        return new GetDestinationSectionResponseDto
        {
            Id = destinationId,
            SectionKey = sectionKey,
            SectionTitle = title,
            Locations = filteredLocations.Select(l => new DestinationSectionLocationItemDto
            {
                Id = l.Id,
                Name = l.Name,
                Latitude = l.Latitude,
                Longitude = l.Longitude
            }).ToList(),
            Places = filteredPlaces.Select(p => new DestinationSectionPlaceItemDto
            {
                Id = p.Id,
                Name = p.Name,
                LocationId = p.LocationId
            }).ToList()
        };
    }

    public async Task<GetDestinationReviewsResponseDto> GetDestinationReviewsAsync(int destinationId)
    {
        var reviews = await _destinationRepository.GetReviewsAsync(destinationId);

        var reviewsList = reviews.ToList();
        var reviewsCount = reviewsList.Count;
        var averageRating = reviewsCount > 0 ? reviewsList.Average(r => r.Rating) : 0.0;

        return new GetDestinationReviewsResponseDto
        {
            DestinationId = destinationId,
            AverageRating = averageRating,
            ReviewsCount = reviewsCount,
            Reviews = reviewsList.Select(r => new DestinationReviewItemDto
            {
                DestinationId = r.DestinationId,
                UserId = r.UserId,
                Rating = r.Rating,
                Comment = r.Comment,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt
            }).ToList()
        };
    }
}
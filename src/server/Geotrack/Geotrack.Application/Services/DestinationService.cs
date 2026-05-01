using Geotrack.Application.DTOs.Destinations;
using Geotrack.Application.DTOs.Explore;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Tags;

namespace Geotrack.Application.Services;

public class DestinationService : IDestinationService
{
    private const int SectionPreviewLimit = 5;

    private readonly IDestinationRepository _destinationRepository;
    private readonly IUserRepository _userRepository;
    private static readonly HashSet<string> AllTypes = new() { "location", "place", "event" };

    public DestinationService(IDestinationRepository destinationRepository, IUserRepository userRepository)
    {
        _destinationRepository = destinationRepository;
        _userRepository = userRepository;
    }

    public async Task<ExploreByLocationResponseDto> ExploreByLocationAsync(ExploreByLocationQueryDto query)
    {
        var destinations = await _destinationRepository.GetAllWithExploreDataAsync();

        var nearest = destinations
            .Select(d => new
            {
                Destination = d,
                Distance = HaversineDistance(query.Latitude, query.Longitude, d.Latitude, d.Longitude)
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
        var userContext = await BuildUserContextAsync(query.UserId);

        var destinationDto = new ExploreDestinationDto
        {
            Id = destination.Id,
            Name = destination.Name,
            Description = destination.Description,
            Latitude = destination.Latitude,
            Longitude = destination.Longitude
        };

        var destinationTags = destination.DestinationTags
            .Where(x => x.Tag != null)
            .Select(x => x.Tag)
            .GroupBy(x => x.Id)
            .Select(g => g.First())
            .OrderByDescending(t => userContext.InterestTagIds.Contains(t.Id))
            .ThenBy(t => t.Name)
            .ToList();

        var sections = new List<ExploreSectionDto>();

        foreach (var tag in destinationTags)
        {
            var section = BuildTagSection(destination, tag, userContext);

            if (section.Items.Any())
            {
                sections.Add(section);
            }
        }

        sections.Add(BuildEventsSection(destination, userContext));

        return new ExploreByLocationResponseDto
        {
            SelectedDestination = destinationDto,
            Sections = sections
        };
    }

    public async Task<GetDestinationDetailsResponseDto> GetDestinationDetailsAsync(int destinationId, int? userId)
    {
        var destination = await _destinationRepository.GetWithDetailsAsync(destinationId)
            ?? throw new KeyNotFoundException("Destination not found");

        var reviews = destination.DestinationReviews.ToList();
        var averageRating = reviews.Any() ? Math.Round(reviews.Average(r => r.Rating), 2) : 0;
        var reviewsCount = reviews.Count;

        var isSaved = false;
        var savedCollectionIds = new List<int>();

        if (userId.HasValue)
        {
            isSaved = await _destinationRepository.IsDestinationSavedByUserAsync(destinationId, userId.Value);
            savedCollectionIds = await _destinationRepository.GetSavedCollectionIdsByUserAsync(destinationId, userId.Value);
        }

        return new GetDestinationDetailsResponseDto
        {
            Id = destination.Id,
            Name = destination.Name,
            Description = destination.Description,
            Latitude = destination.Latitude,
            Longitude = destination.Longitude,
            DestinationTypeId = destination.DestinationTypeId,
            RegionId = destination.RegionId,
            CreatedAt = destination.CreatedAt,
            UpdatedAt = destination.UpdatedAt,
            AverageRating = averageRating,
            ReviewsCount = reviewsCount,
            IsSaved = isSaved,
            SavedCollectionIds = savedCollectionIds,
            Sections = BuildDestinationPreviewSections(destination)
        };
    }

    public async Task<GetDestinationSectionResponseDto> GetDestinationSectionAsync(int destinationId, string sectionKey)
    {
        var destination = await _destinationRepository.GetWithDetailsAsync(destinationId)
            ?? throw new KeyNotFoundException("Destination not found");

        var normalizedKey = sectionKey.Trim().ToLowerInvariant();

        var tag = destination.DestinationTags
            .Where(x => x.Tag != null)
            .Select(x => x.Tag)
            .FirstOrDefault(t => t.Slug.ToLower() == normalizedKey);

        List<Location> locations;
        List<Place> places;
        string title;

        if (tag != null)
        {
            locations = destination.Locations
                .Where(l => l.LocationTags.Any(lt => lt.TagId == tag.Id))
                .OrderBy(l => l.Name)
                .ToList();

            places = destination.Locations
                .SelectMany(l => l.Places)
                .Where(p => p.PlaceTags.Any(pt => pt.TagId == tag.Id))
                .OrderBy(p => p.Name)
                .ToList();

            title = tag.Name;
        }
        else if (normalizedKey == "locations")
        {
            locations = destination.Locations
                .OrderBy(l => l.Name)
                .ToList();

            places = destination.Locations
                .SelectMany(l => l.Places)
                .OrderBy(p => p.Name)
                .ToList();

            title = "Locations";
        }
        else
        {
            throw new KeyNotFoundException("Section not found");
        }

        return new GetDestinationSectionResponseDto
        {
            DestinationId = destinationId,
            SectionKey = normalizedKey,
            SectionTitle = title,
            Locations = locations.Select(MapDestinationLocationItem).ToList(),
            Places = places.Select(MapDestinationPlaceItem).ToList()
        };
    }

    public async Task<GetDestinationReviewsResponseDto> GetDestinationReviewsAsync(int destinationId)
    {
        var destination = await _destinationRepository.GetByIdAsync(destinationId);
        if (destination == null)
            throw new KeyNotFoundException("Destination not found");

        var reviews = await _destinationRepository.GetReviewsByDestinationIdAsync(destinationId);
        var averageRating = reviews.Any() ? Math.Round(reviews.Average(r => r.Rating), 2) : 0;

        return new GetDestinationReviewsResponseDto
        {
            DestinationId = destinationId,
            AverageRating = averageRating,
            ReviewsCount = reviews.Count,
            Reviews = reviews.Select(r => new DestinationReviewItemDto
            {
                Id = r.Id,
                UserId = r.UserId,
                Rating = r.Rating,
                Comment = r.Comment,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt
            }).ToList()
        };
    }

    public async Task<CreateDestinationReviewResponseDto> CreateDestinationReviewAsync(int destinationId, int userId, CreateDestinationReviewRequestDto request)
    {
        if (request.Rating < 1 || request.Rating > 5)
            throw new InvalidOperationException("Rating must be between 1 and 5");

        if ((request.Comment ?? string.Empty).Length > 1000)
            throw new InvalidOperationException("Comment is too long");

        var destination = await _destinationRepository.GetByIdAsync(destinationId);
        if (destination == null)
            throw new KeyNotFoundException("Destination not found");

        var alreadyReviewed = await _destinationRepository.HasUserReviewedDestinationAsync(destinationId, userId);
        if (alreadyReviewed)
            throw new InvalidOperationException("User has already reviewed this destination");

        var review = new DestinationReview
        {
            UserId = userId,
            DestinationId = destinationId,
            Rating = request.Rating,
            Comment = request.Comment?.Trim() ?? string.Empty,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _destinationRepository.AddReviewAsync(review);
        await _destinationRepository.SaveChangesAsync();

        return new CreateDestinationReviewResponseDto
        {
            Id = review.Id,
            UserId = review.UserId,
            DestinationId = review.DestinationId,
            Rating = review.Rating,
            Comment = review.Comment,
            CreatedAt = review.CreatedAt,
            UpdatedAt = review.UpdatedAt
        };
    }

    private List<DestinationSectionPreviewDto> BuildDestinationPreviewSections(Destination destination)
    {
        var sections = new List<DestinationSectionPreviewDto>();

        var tags = destination.DestinationTags
            .Where(x => x.Tag != null)
            .Select(x => x.Tag)
            .GroupBy(x => x.Id)
            .Select(g => g.First())
            .OrderBy(t => t.Name)
            .ToList();

        foreach (var tag in tags)
        {
            var locations = destination.Locations
                .Where(l => l.LocationTags.Any(lt => lt.TagId == tag.Id))
                .OrderBy(l => l.Name)
                .Take(SectionPreviewLimit)
                .Select(MapDestinationLocationItem)
                .ToList();

            var places = destination.Locations
                .SelectMany(l => l.Places)
                .Where(p => p.PlaceTags.Any(pt => pt.TagId == tag.Id))
                .OrderBy(p => p.Name)
                .Take(SectionPreviewLimit)
                .Select(MapDestinationPlaceItem)
                .ToList();

            if (locations.Any() || places.Any())
            {
                sections.Add(new DestinationSectionPreviewDto
                {
                    Key = tag.Slug,
                    Title = tag.Name,
                    Locations = locations,
                    Places = places
                });
            }
        }

        if (!sections.Any())
        {
            sections.Add(new DestinationSectionPreviewDto
            {
                Key = "locations",
                Title = "Locations",
                Locations = destination.Locations
                    .OrderBy(l => l.Name)
                    .Take(SectionPreviewLimit)
                    .Select(MapDestinationLocationItem)
                    .ToList(),
                Places = destination.Locations
                    .SelectMany(l => l.Places)
                    .OrderBy(p => p.Name)
                    .Take(SectionPreviewLimit)
                    .Select(MapDestinationPlaceItem)
                    .ToList()
            });
        }

        return sections;
    }

    private static DestinationSectionLocationItemDto MapDestinationLocationItem(Location location)
    {
        return new DestinationSectionLocationItemDto
        {
            Id = location.Id,
            Name = location.Name,
            Latitude = location.Latitude,
            Longitude = location.Longitude
        };
    }

    private static DestinationSectionPlaceItemDto MapDestinationPlaceItem(Place place)
    {
        return new DestinationSectionPlaceItemDto
        {
            Id = place.Id,
            Name = place.Name,
            LocationId = place.LocationId
        };
    }

    private ExploreSectionDto BuildTagSection(Destination destination, Tag tag, UserExploreContext userContext)
    {
        var scoredItems = new List<ScoredPreviewItem>();

        foreach (var location in destination.Locations.Where(l => l.LocationTags.Any(lt => lt.TagId == tag.Id)))
        {
            scoredItems.Add(BuildLocationPreview(location, userContext));
        }

        foreach (var place in destination.Locations.SelectMany(l => l.Places).Where(p => p.PlaceTags.Any(pt => pt.TagId == tag.Id)))
        {
            scoredItems.Add(BuildPlacePreview(place, userContext));
        }

        foreach (var entry in destination.Locations.SelectMany(l => l.Events.Select(e => new { Event = e, ParentLocation = l })).Where(x => x.Event.EventTags.Any(et => et.TagId == tag.Id)))
        {
            scoredItems.Add(BuildEventPreview(entry.Event, entry.ParentLocation, userContext));
        }

        return new ExploreSectionDto
        {
            Key = tag.Slug,
            Title = tag.Name,
            Items = scoredItems
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Item.Name)
                .Take(8)
                .Select(x => x.Item)
                .ToList()
        };
    }

    private ExploreSectionDto BuildEventsSection(Destination destination, UserExploreContext userContext)
    {
        var scoredItems = destination.Locations
            .SelectMany(l => l.Events.Select(e => BuildEventPreview(e, l, userContext)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Item.Name)
            .Take(8)
            .Select(x => x.Item)
            .ToList();

        return new ExploreSectionDto
        {
            Key = "events",
            Title = "Events",
            Items = scoredItems
        };
    }

    private ScoredPreviewItem BuildLocationPreview(Location location, UserExploreContext userContext)
    {
        var avgRating = CalculateAverageRating(location.LocationReviews);
        var reviewCount = location.LocationReviews.Count;
        var isSaved = userContext.SavedLocationIds.Contains(location.Id);
        var userRating = userContext.LocationRatings.TryGetValue(location.Id, out var rating) ? rating : (double?)null;
        var matchesInterests = location.LocationTags.Any(x => userContext.InterestTagIds.Contains(x.TagId));

        var item = new ExplorePreviewItemDto
        {
            EntityType = "location",
            EntityId = location.Id,
            Name = location.Name,
            Description = string.Empty,
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            AverageRating = avgRating,
            ReviewCount = reviewCount,
            IsSaved = isSaved,
            UserRating = userRating
        };

        return new ScoredPreviewItem
        {
            Item = item,
            Score = CalculateScore(avgRating, reviewCount, isSaved, userRating, matchesInterests, userContext.IsLoggedIn)
        };
    }

    private ScoredPreviewItem BuildPlacePreview(Place place, UserExploreContext userContext)
    {
        var avgRating = CalculateAverageRating(place.PlaceReviews);
        var reviewCount = place.PlaceReviews.Count;
        var isSaved = userContext.SavedPlaceIds.Contains(place.Id);
        var userRating = userContext.PlaceRatings.TryGetValue(place.Id, out var rating) ? rating : (double?)null;
        var matchesInterests = place.PlaceTags.Any(x => userContext.InterestTagIds.Contains(x.TagId));

        var item = new ExplorePreviewItemDto
        {
            EntityType = "place",
            EntityId = place.Id,
            Name = place.Name,
            Description = place.Description,
            Latitude = place.Latitude,
            Longitude = place.Longitude,
            AverageRating = avgRating,
            ReviewCount = reviewCount,
            IsSaved = isSaved,
            UserRating = userRating
        };

        return new ScoredPreviewItem
        {
            Item = item,
            Score = CalculateScore(avgRating, reviewCount, isSaved, userRating, matchesInterests, userContext.IsLoggedIn)
        };
    }

    private ScoredPreviewItem BuildEventPreview(Event @event, Location parentLocation, UserExploreContext userContext)
    {
        var avgRating = CalculateAverageRating(@event.EventReviews);
        var reviewCount = @event.EventReviews.Count;
        var isSaved = userContext.SavedEventIds.Contains(@event.Id);
        var userRating = userContext.EventRatings.TryGetValue(@event.Id, out var rating) ? rating : (double?)null;
        var matchesInterests = @event.EventTags.Any(x => userContext.InterestTagIds.Contains(x.TagId));

        var item = new ExplorePreviewItemDto
        {
            EntityType = "event",
            EntityId = @event.Id,
            Name = @event.Name,
            Description = @event.Description,
            Latitude = @event.Place?.Latitude ?? parentLocation.Latitude,
            Longitude = @event.Place?.Longitude ?? parentLocation.Longitude,
            AverageRating = avgRating,
            ReviewCount = reviewCount,
            IsSaved = isSaved,
            UserRating = userRating,
            StartDate = @event.StartDate,
            EndDate = @event.EndDate
        };

        return new ScoredPreviewItem
        {
            Item = item,
            Score = CalculateScore(avgRating, reviewCount, isSaved, userRating, matchesInterests, userContext.IsLoggedIn)
        };
    }

    private async Task<UserExploreContext> BuildUserContextAsync(int? userId)
    {
        if (!userId.HasValue)
        {
            return new UserExploreContext();
        }

        var user = await _userRepository.GetByIdWithExploreDataAsync(userId.Value);
        if (user == null)
        {
            return new UserExploreContext();
        }

        var locationRatings = await _userRepository.GetLocationRatingsByUserIdAsync(userId.Value);
        var placeRatings = await _userRepository.GetPlaceRatingsByUserIdAsync(userId.Value);
        var eventRatings = await _userRepository.GetEventRatingsByUserIdAsync(userId.Value);

        return new UserExploreContext
        {
            IsLoggedIn = true,
            InterestTagIds = user.UserTags.Select(x => x.TagId).ToHashSet(),
            SavedLocationIds = user.Collections.SelectMany(c => c.CollectionItems).Where(ci => ci.LocationId.HasValue).Select(ci => ci.LocationId!.Value).ToHashSet(),
            SavedPlaceIds = user.Collections.SelectMany(c => c.CollectionItems).Where(ci => ci.PlaceId.HasValue).Select(ci => ci.PlaceId!.Value).ToHashSet(),
            SavedEventIds = user.Collections.SelectMany(c => c.CollectionItems).Where(ci => ci.EventId.HasValue).Select(ci => ci.EventId!.Value).ToHashSet(),
            LocationRatings = locationRatings,
            PlaceRatings = placeRatings,
            EventRatings = eventRatings
        };
    }

    private static double CalculateAverageRating<TReview>(ICollection<TReview> reviews) where TReview : class
    {
        if (reviews.Count == 0)
            return 0;

        return typeof(TReview).Name switch
        {
            nameof(LocationReview) => Math.Round(reviews.Cast<LocationReview>().Average(r => r.Rating), 2),
            nameof(PlaceReview) => Math.Round(reviews.Cast<PlaceReview>().Average(r => r.Rating), 2),
            nameof(EventReview) => Math.Round(reviews.Cast<EventReview>().Average(r => r.Rating), 2),
            _ => 0
        };
    }

    private static double CalculateScore(double averageRating, int reviewCount, bool isSaved, double? userRating, bool matchesInterests, bool isLoggedIn)
    {
        var popularityScore = (averageRating * 10) + reviewCount;

        if (!isLoggedIn)
            return popularityScore;

        var personalizedScore = popularityScore;

        if (matchesInterests)
            personalizedScore += 40;

        if (userRating.HasValue)
            personalizedScore += 60 + (userRating.Value * 10);

        if (isSaved)
            personalizedScore += 100;

        return personalizedScore;
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

    public async Task<List<DestinationSearchResultDto>> SearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<DestinationSearchResultDto>();

        var destinations = await _destinationRepository.SearchByNameAsync(query);

        return destinations.Select(d => new DestinationSearchResultDto
        {
            Id = d.Id,
            Name = d.Name,
            Description = d.Description,
            Latitude = d.Latitude,
            Longitude = d.Longitude,
        }).ToList();
    }

    public async Task<List<DestinationSearchItemDto>> SearchWithinDestinationAsync(int destinationId, string query, IEnumerable<string>? types)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<DestinationSearchItemDto>();

        var exists = await _destinationRepository.ExistsAsync(destinationId);

        if (!exists)
            throw new KeyNotFoundException($"Destinacija {destinationId} nije pronadjena.");

        var typesToSearch = types?.Select(t => t.ToLower().Trim()).Where(t => AllTypes.Contains(t)).ToHashSet();

        if (typesToSearch == null || typesToSearch.Count == 0)
            typesToSearch = AllTypes;

        var results = new List<DestinationSearchItemDto>();

        if (typesToSearch.Contains("location"))
        {
            var locations = await _destinationRepository.SearchLocationsAsync(destinationId, query);
            results.AddRange(locations.Select(l => new DestinationSearchItemDto
            {
                Id = l.Id,
                Type = "location",
                Name = l.Name,
                Latitude = l.Latitude,
                Longitude = l.Longitude,
                ParentName = null
            }));
        }

        if (typesToSearch.Contains("place"))
        {
            var places = await _destinationRepository.SearchPlacesAsync(destinationId, query);
            results.AddRange(places.Select(p => new DestinationSearchItemDto
            {
                Id = p.Id,
                Type = "place",
                Name = p.Name,
                Description = p.Description,
                Latitude = p.Latitude,
                Longitude = p.Longitude,
                ParentName = p.Location?.Name
            }));
        }

        if (typesToSearch.Contains("event"))
        {
            var events = await _destinationRepository.SearchEventsAsync(destinationId, query);
            results.AddRange(events.Select(e => new DestinationSearchItemDto
            {
                Id = e.Id,
                Type = "event",
                Name = e.Name,
                Description = e.Description,
                Latitude = e.Place?.Latitude ?? e.Location.Latitude,
                Longitude = e.Place?.Longitude ?? e.Location.Longitude,
                ParentName = e.Place?.Name ?? e.Location.Name
            }));
        }

        return results;
    }

    private sealed class UserExploreContext
    {
        public bool IsLoggedIn { get; set; }
        public HashSet<int> InterestTagIds { get; set; } = new();
        public HashSet<int> SavedLocationIds { get; set; } = new();
        public HashSet<int> SavedPlaceIds { get; set; } = new();
        public HashSet<int> SavedEventIds { get; set; } = new();
        public Dictionary<int, double> LocationRatings { get; set; } = new();
        public Dictionary<int, double> PlaceRatings { get; set; } = new();
        public Dictionary<int, double> EventRatings { get; set; } = new();
    }

    private sealed class ScoredPreviewItem
    {
        public ExplorePreviewItemDto Item { get; set; } = new();
        public double Score { get; set; }
    }
}

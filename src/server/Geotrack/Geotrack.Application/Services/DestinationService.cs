using Geotrack.Application.DTOs.Explore;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Tags;

namespace Geotrack.Application.Services;

public class DestinationService : IDestinationService
{
    private const int SectionPreviewLimit = 8;

    private readonly IDestinationRepository _destinationRepository;
    private readonly IUserRepository _userRepository;

    public DestinationService(
        IDestinationRepository destinationRepository,
        IUserRepository userRepository)
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

    private ExploreSectionDto BuildTagSection(
        Destination destination,
        Tag tag,
        UserExploreContext userContext)
    {
        var scoredItems = new List<ScoredPreviewItem>();

        foreach (var location in destination.Locations
                     .Where(l => l.LocationTags.Any(lt => lt.TagId == tag.Id)))
        {
            scoredItems.Add(BuildLocationPreview(location, userContext));
        }

        foreach (var place in destination.Locations
                     .SelectMany(l => l.Places)
                     .Where(p => p.PlaceTags.Any(pt => pt.TagId == tag.Id)))
        {
            scoredItems.Add(BuildPlacePreview(place, userContext));
        }

        foreach (var entry in destination.Locations
                     .SelectMany(l => l.Events.Select(e => new { Event = e, ParentLocation = l }))
                     .Where(x => x.Event.EventTags.Any(et => et.TagId == tag.Id)))
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
                .Take(SectionPreviewLimit)
                .Select(x => x.Item)
                .ToList()
        };
    }

    private ExploreSectionDto BuildEventsSection(
        Destination destination,
        UserExploreContext userContext)
    {
        var scoredItems = destination.Locations
            .SelectMany(l => l.Events.Select(e => BuildEventPreview(e, l, userContext)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Item.Name)
            .Take(SectionPreviewLimit)
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
        var userRating = userContext.LocationRatings.TryGetValue(location.Id, out var rating)
            ? rating
            : (int?)null;
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
        var userRating = userContext.PlaceRatings.TryGetValue(place.Id, out var rating)
            ? rating
            : (int?)null;
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
        var userRating = userContext.EventRatings.TryGetValue(@event.Id, out var rating)
            ? rating
            : (int?)null;
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
            SavedLocationIds = user.Collections
                .SelectMany(c => c.CollectionItems)
                .Where(ci => ci.LocationId.HasValue)
                .Select(ci => ci.LocationId!.Value)
                .ToHashSet(),
            SavedPlaceIds = user.Collections
                .SelectMany(c => c.CollectionItems)
                .Where(ci => ci.PlaceId.HasValue)
                .Select(ci => ci.PlaceId!.Value)
                .ToHashSet(),
            SavedEventIds = user.Collections
                .SelectMany(c => c.CollectionItems)
                .Where(ci => ci.EventId.HasValue)
                .Select(ci => ci.EventId!.Value)
                .ToHashSet(),
            LocationRatings = locationRatings,
            PlaceRatings = placeRatings,
            EventRatings = eventRatings
        };
    }

    private static double CalculateAverageRating<TReview>(ICollection<TReview> reviews)
        where TReview : class
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

    private static double CalculateScore(
        double averageRating,
        int reviewCount,
        bool isSaved,
        int? userRating,
        bool matchesInterests,
        bool isLoggedIn)
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

    private sealed class UserExploreContext
    {
        public bool IsLoggedIn { get; set; }
        public HashSet<int> InterestTagIds { get; set; } = new();
        public HashSet<int> SavedLocationIds { get; set; } = new();
        public HashSet<int> SavedPlaceIds { get; set; } = new();
        public HashSet<int> SavedEventIds { get; set; } = new();

        public Dictionary<int, int> LocationRatings { get; set; } = new();
        public Dictionary<int, int> PlaceRatings { get; set; } = new();
        public Dictionary<int, int> EventRatings { get; set; } = new();
    }

    private sealed class ScoredPreviewItem
    {
        public ExplorePreviewItemDto Item { get; set; } = new();
        public double Score { get; set; }
    }
}
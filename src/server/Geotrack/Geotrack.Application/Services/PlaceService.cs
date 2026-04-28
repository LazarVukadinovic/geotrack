using Geotrack.Application.DTOs.Places;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;

namespace Geotrack.Application.Services;

public class PlaceService : IPlaceService
{
    private readonly IPlaceRepository _placeRepository;

    public PlaceService(IPlaceRepository placeRepository)
    {
        _placeRepository = placeRepository;
    }

    public async Task<GetPlaceDetailsResponseDto> GetPlaceDetailsAsync(int placeId, int? userId)
    {
        var place = await _placeRepository.GetWithDetailsAsync(placeId)
            ?? throw new KeyNotFoundException("Places not found");

        var reviews = place.PlaceReviews.ToList();
        var averageRating = reviews.Any() ? Math.Round(reviews.Average(r => r.Rating), 2) : 0;
        var reviewsCount = reviews.Count;

        var isSaved = false;
        var savedCollectionIds = new List<int>();

        if (userId.HasValue)
        {
            isSaved = await _placeRepository.IsPlaceSavedByUserAsync(placeId, userId.Value);
            savedCollectionIds = await _placeRepository.GetSavedCollectionIdsByUserAsync(placeId, userId.Value);
        }

        return new GetPlaceDetailsResponseDto
        {
            Id = place.Id,
            Name = place.Name,
            PlaceTypeId = place.PlaceTypeId,
            LocationId = place.LocationId,
            Description = place.Description,
            Latitude = place.Latitude,
            Longitude = place.Longitude,
            ContactEmail = place.ContactEmail,
            Number = place.Number,
            CreatedAt = place.CreatedAt,
            UpdatedAt = place.UpdatedAt,
            AverageRating = averageRating,
            ReviewsCount = reviewsCount,
            IsSaved = isSaved,
            SavedCollectionIds = savedCollectionIds,
            Events = place.Events.OrderBy(e => e.StartDate).Select(MapEventItem).ToList()
        };
    }

    public async Task<GetPlaceEventsResponseDto> GetPlaceEventsAsync(int placeId)
    {
        var place = await _placeRepository.GetByIdAsync(placeId);
        if (place == null)
            throw new KeyNotFoundException("Places not found");

        var events = await _placeRepository.GetEventsByPlaceIdAsync(placeId);

        return new GetPlaceEventsResponseDto
        {
            PlaceId = placeId,
            Events = events.Select(MapEventItem).ToList()
        };
    }

    public async Task<GetPlaceReviewsResponseDto> GetPlaceReviewsAsync(int placeId)
    {
        var place = await _placeRepository.GetByIdAsync(placeId);
        if (place == null)
            throw new KeyNotFoundException("Places not found");

        var reviews = await _placeRepository.GetReviewsByPlaceIdAsync(placeId);
        var averageRating = reviews.Any() ? Math.Round(reviews.Average(r => r.Rating), 2) : 0;

        return new GetPlaceReviewsResponseDto
        {
            PlaceId = placeId,
            AverageRating = averageRating,
            ReviewsCount = reviews.Count,
            Reviews = reviews.Select(r => new PlaceReviewItemDto
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

    public async Task<CreatePlaceReviewResponseDto> CreatePlaceReviewAsync(int placeId, int userId, CreatePlaceReviewRequestDto request)
    {
        if (request.Rating < 1 || request.Rating > 5)
            throw new InvalidOperationException("Rating must be between 1 and 5");

        if ((request.Comment ?? string.Empty).Length > 1000)
            throw new InvalidOperationException("Comment is too long");

        var place = await _placeRepository.GetByIdAsync(placeId);
        if (place == null)
            throw new KeyNotFoundException("Places not found");

        var alreadyReviewed = await _placeRepository.HasUserReviewedPlaceAsync(placeId, userId);
        if (alreadyReviewed)
            throw new InvalidOperationException("User has already reviewed this place");

        var review = new PlaceReview
        {
            UserId = userId,
            PlaceId = placeId,
            Rating = request.Rating,
            Comment = request.Comment?.Trim() ?? string.Empty,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _placeRepository.AddReviewAsync(review);
        await _placeRepository.SaveChangesAsync();

        return new CreatePlaceReviewResponseDto
        {
            Id = review.Id,
            UserId = review.UserId,
            PlaceId = review.PlaceId,
            Rating = review.Rating,
            Comment = review.Comment,
            CreatedAt = review.CreatedAt,
            UpdatedAt = review.UpdatedAt
        };
    }

    private static PlaceEventItemDto MapEventItem(Event @event)
    {
        return new PlaceEventItemDto
        {
            Id = @event.Id,
            Name = @event.Name,
            LocationId = @event.LocationId,
            PlaceId = @event.PlaceId ?? 0,
            Description = @event.Description,
            StartDate = @event.StartDate,
            EndDate = @event.EndDate,
            CreatedAt = @event.CreatedAt,
            UpdatedAt = @event.UpdatedAt
        };
    }
}
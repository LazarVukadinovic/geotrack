using Geotrack.Application.DTOs.Events;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Reviews;

namespace Geotrack.Application.Services;

public class EventService : IEventService
{
    private readonly IEventRepository _eventRepository;

    public EventService(IEventRepository eventRepository)
    {
        _eventRepository = eventRepository;
    }

    public async Task<GetEventDetailsResponseDto> GetEventDetailsAsync(int eventId, int? userId)
    {
        var @event = await _eventRepository.GetWithDetailsAsync(eventId)
            ?? throw new KeyNotFoundException("Events not found");

        var reviews = @event.EventReviews.ToList();
        var averageRating = reviews.Any() ? Math.Round(reviews.Average(r => r.Rating), 2) : 0;
        var reviewsCount = reviews.Count;

        var isSaved = false;
        var savedCollectionIds = new List<int>();

        if (userId.HasValue)
        {
            isSaved = await _eventRepository.IsEventSavedByUserAsync(eventId, userId.Value);
            savedCollectionIds = await _eventRepository.GetSavedCollectionIdsByUserAsync(eventId, userId.Value);
        }

        var sections = new List<EventSectionPreviewDto>();

        if (@event.EventReviews.Any())
        {
            sections.Add(new EventSectionPreviewDto { Key = "reviews", Title = "Reviews" });
        }

        if (@event.EventTags.Any())
        {
            sections.Add(new EventSectionPreviewDto { Key = "tags", Title = "Tags" });
        }

        return new GetEventDetailsResponseDto
        {
            Id = @event.Id,
            Name = @event.Name,
            EventTypeId = @event.EventTypeId,
            LocationId = @event.LocationId,
            PlaceId = @event.PlaceId,
            Description = @event.Description,
            StartDate = @event.StartDate,
            EndDate = @event.EndDate,
            CreatedAt = @event.CreatedAt,
            UpdatedAt = @event.UpdatedAt,
            AverageRating = averageRating,
            ReviewsCount = reviewsCount,
            IsSaved = isSaved,
            SavedCollectionIds = savedCollectionIds,
            Sections = sections
        };
    }

    public async Task<GetEventSectionResponseDto> GetEventSectionAsync(int eventId, string sectionKey)
    {
        var @event = await _eventRepository.GetWithDetailsAsync(eventId)
            ?? throw new KeyNotFoundException("Events not found");

        var normalizedKey = sectionKey.Trim().ToLowerInvariant();

        if (normalizedKey == "reviews")
        {
            return new GetEventSectionResponseDto
            {
                EventId = eventId,
                SectionKey = "reviews",
                SectionTitle = "Reviews",
                Reviews = @event.EventReviews
                    .OrderByDescending(r => r.CreatedAt)
                    .Select(r => new EventReviewItemDto
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

        if (normalizedKey == "tags")
        {
            return new GetEventSectionResponseDto
            {
                EventId = eventId,
                SectionKey = "tags",
                SectionTitle = "Tags",
                Tags = @event.EventTags
                    .Where(t => t.Tag != null)
                    .Select(t => new EventTagItemDto
                    {
                        Id = t.TagId,
                        Name = t.Tag.Name
                    }).ToList()
            };
        }

        throw new KeyNotFoundException("Section not found");
    }

    public async Task<GetEventReviewsResponseDto> GetEventReviewsAsync(int eventId)
    {
        var @event = await _eventRepository.GetByIdAsync(eventId);
        if (@event == null)
            throw new KeyNotFoundException("Events not found");

        var reviews = await _eventRepository.GetReviewsByEventIdAsync(eventId);
        var averageRating = reviews.Any() ? Math.Round(reviews.Average(r => r.Rating), 2) : 0;

        return new GetEventReviewsResponseDto
        {
            EventId = eventId,
            AverageRating = averageRating,
            ReviewsCount = reviews.Count,
            Reviews = reviews.Select(r => new EventReviewItemDto
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

    public async Task<CreateEventReviewResponseDto> CreateEventReviewAsync(int eventId, int userId, CreateEventReviewRequestDto request)
    {
        if (request.Rating < 1 || request.Rating > 5)
            throw new InvalidOperationException("Rating must be between 1 and 5");

        if ((request.Comment ?? string.Empty).Length > 1000)
            throw new InvalidOperationException("Comment is too long");

        var @event = await _eventRepository.GetByIdAsync(eventId);
        if (@event == null)
            throw new KeyNotFoundException("Events not found");

        var alreadyReviewed = await _eventRepository.HasUserReviewedEventAsync(eventId, userId);
        if (alreadyReviewed)
            throw new InvalidOperationException("User has already reviewed this event");

        var review = new EventReview
        {
            UserId = userId,
            EventId = eventId,
            Rating = request.Rating,
            Comment = request.Comment?.Trim() ?? string.Empty,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _eventRepository.AddReviewAsync(review);
        await _eventRepository.SaveChangesAsync();

        return new CreateEventReviewResponseDto
        {
            Id = review.Id,
            UserId = review.UserId,
            EventId = review.EventId,
            Rating = review.Rating,
            Comment = review.Comment,
            CreatedAt = review.CreatedAt,
            UpdatedAt = review.UpdatedAt
        };
    }
}
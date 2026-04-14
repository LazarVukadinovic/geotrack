using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Application.DTOs.Event;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Tags;

namespace Geotrack.Application.Services
{
    public class EventService : IEventService
    {
        private readonly IEventRepository _eventRepository;

        public EventService(IEventRepository eventRepository)
        {
            _eventRepository = eventRepository;
        }

        public async Task<GetEventDetailsResponseDto> GetEventDetailsAsync(int eventId, int? userId)
        {
            var ev = await _eventRepository.GetWithDetailsAsync(eventId);
            if (ev == null)
                throw new KeyNotFoundException($"Event sa id-em {eventId} nije pronađen.");

            var reviews = ev.EventReviews ?? new List<EventReview>();
            var averageRating = reviews.Any() ? reviews.Average(r => r.Rating) : 0;

            var isSaved = false;
            var savedCollectionIds = new List<int>();

            if (userId.HasValue)
            {
                isSaved = await _eventRepository.IsEventSavedByUserAsync(eventId, userId.Value);
                savedCollectionIds = await _eventRepository.GetSavedCollectionIdsByUserAsync(eventId, userId.Value);
            }

            var sections = BuildSections(ev);

            return new GetEventDetailsResponseDto
            {
                Id = ev.Id,
                Name = ev.Name,
                EventTypeId = ev.EventTypeId,
                LocationId = ev.LocationId,
                PlaceId = ev.PlaceId,
                Description = ev.Description,
                StartDate = ev.StartDate,
                EndDate = ev.EndDate,
                CreatedAt = ev.CreatedAt,
                UpdatedAt = ev.UpdatedAt,
                AverageRating = Math.Round(averageRating, 1),
                ReviewsCount = reviews.Count,
                IsSaved = isSaved,
                SavedCollectionIds = savedCollectionIds,
                Sections = sections
            };
        }

        private List<EventSectionPreviewDto> BuildSections(Event ev)
        {
            var sections = new List<EventSectionPreviewDto>();

            if (ev.EventReviews != null && ev.EventReviews.Any())
            {
                sections.Add(new EventSectionPreviewDto
                {
                    Key = "reviews",
                    Title = "Reviews"
                });
            }

            if (ev.EventTags != null && ev.EventTags.Any())
            {
                sections.Add(new EventSectionPreviewDto
                {
                    Key = "tags",
                    Title = "Tags"
                });
            }

            return sections;
        }

        public async Task<GetEventSectionResponseDto> GetEventSectionAsync(int eventId, string sectionKey)
        {
            var ev = await _eventRepository.GetByIDAsync(eventId);
            if (ev == null)
                throw new KeyNotFoundException($"Event sa id-em {eventId} nije pronađen.");

            var sectionTitle = sectionKey switch
            {
                "reviews" => "Reviews",
                "tags" => "Tags",
                _ => throw new KeyNotFoundException($"Nepoznati sectionKey: {sectionKey}")
            };

            var reviews = new List<EventReviewItemDto>();
            var tags = new List<EventTagItemDto>();

            if (sectionKey == "reviews")
            {
                reviews = (ev.EventReviews ?? new List<EventReview>())
                    .Select(r => new EventReviewItemDto
                    {
                        Id = r.Id,
                        UserId = r.UserId,
                        Rating = r.Rating,
                        Comment = r.Comment ?? string.Empty,
                        CreatedAt = r.CreatedAt,
                        UpdatedAt = r.UpdatedAt
                    }).ToList();
            }

            if (sectionKey == "tags")
            {
                tags = (ev.EventTags ?? new List<EventTag>())
                    .Select(t => new EventTagItemDto
                    {
                        Id = t.EventId,
                        
                    }).ToList();
            }

            return new GetEventSectionResponseDto
            {
                EventId = eventId,
                SectionKey = sectionKey,
                SectionTitle = sectionTitle,
                Reviews = reviews,
                Tags = tags
            };
        }

    }
}


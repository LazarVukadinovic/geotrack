using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Application.DTOs.Place;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;

namespace Geotrack.Application.Services
{
    public class PlaceService : IPlaceService
    {
        private readonly IPlaceRepository _placeRepository;

        public PlaceService(IPlaceRepository placeRepository)
        {
            _placeRepository = placeRepository;
        }
        public async Task<GetPlaceDetailsResponseDto> GetPlaceDetailsAsync(int placeId, int? userId)
        {
            var place = await _placeRepository.GetWithDetailsAsync(placeId);
            if (place == null)
                throw new KeyNotFoundException($"Place sa id-em {placeId} nije pronađen.");

            var reviews = place.PlaceReviews ?? new List<PlaceReview>();
            var averageRating = reviews.Any() ? reviews.Average(r => r.Rating) : 0;

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
                AverageRating = Math.Round(averageRating, 1),
                ReviewsCount = reviews.Count,
                IsSaved = isSaved,
                SavedCollectionIds = savedCollectionIds,
                Events = (place.Events ?? new List<Event>()).Select(e => new PlaceEventItemDto
                {
                    Id = e.Id,
                    Name = e.Name,
                    LocationId = e.LocationId,
                    PlaceId = (int)e.PlaceId,
                    Description = e.Description,
                    StartDate = e.StartDate,
                    EndDate = e.EndDate,
                    CreatedAt = e.CreatedAt,
                    UpdatedAt = e.UpdatedAt
                }).ToList()
            };
        }

        public async Task<GetPlaceEventsResponseDto> GetPlaceEventsAsync(int placeId)
        {
            var events = await _placeRepository.GetEventsByPlaceIdAsync(placeId);

            return new GetPlaceEventsResponseDto
            {
                PlaceId = placeId,
                Events = events.Select(e => new PlaceEventItemDto
                {
                    Id = e.Id,
                    Name = e.Name,
                    LocationId = e.LocationId,
                    PlaceId = e.PlaceId ?? 0, // Sigurnost zbog nullable tipa
                    Description = e.Description ?? string.Empty,
                    StartDate = e.StartDate,
                    EndDate = e.EndDate,
                    CreatedAt = e.CreatedAt,
                    UpdatedAt = e.UpdatedAt
                }).ToList()
            };
        }

        public async Task<GetPlaceReviewsresponseDto> GetPlaceReviewsAsync(int placeId)
        {
            var reviews = await _placeRepository.GetReviewsByPlaceIdAsync(placeId);

            var averageRating = reviews.Any() ? reviews.Average(r => r.Rating) : 0;

            return new GetPlaceReviewsresponseDto
            {
                PlaceId = placeId,
                AverageRating = Math.Round(averageRating, 1),
                ReviewsCount = reviews.Count,
                Reviews = reviews.Select(r => new PlaceReviewItemDto
                {
                    Id = r.Id,
                    UserId = r.UserId,
                    Rating = r.Rating,
                    Comment = r.Comment ?? string.Empty,
                    CreatedAt = r.CreatedAt,
                    UpdatedAt = r.UpdatedAt
                }).ToList()
            };
        }
    }
}


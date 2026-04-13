using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Application.DTOs.Destionation;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Reviews;

namespace Geotrack.Application.Services
{
    public class DestinationReviewService : IDestinationReviewService
    {
        private readonly IDestinationReviewRepository _reviewRepository;
        private readonly IDestinationRepository _destinationRepository;

        public DestinationReviewService(IDestinationReviewRepository reviewRepository,IDestinationRepository destinationRepository)
        {
            _reviewRepository = reviewRepository;
            _destinationRepository = destinationRepository;
        }

        public async Task<CreateDestinationReviewResponseDto> CreateReviewAsync(int destinationId, int userId, CreateDestinationReviewRequestDto dto)
        {
            var destination = await _destinationRepository.GetByIDAsync(destinationId);
            if (destination == null)
                throw new KeyNotFoundException($"Destinacija sa id {destinationId} nije pronadjena.");

            var alreadyReviewed = await _reviewRepository.ExistsByDestinationAndUserAsync(destinationId,userId);
            if (alreadyReviewed)
                throw new InvalidOperationException("Vec si trazio review za tu destinaciju.");

            var review = new DestinationReview
            {
                UserId = userId,
                DestinationId = destinationId,
                Rating = dto.Rating,
                Comment = dto.Comment,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            var created = await _reviewRepository.CreateAsync(review);

            return new CreateDestinationReviewResponseDto
            {
                Id = created.Id,
                UserId = created.UserId,
                DestinationId = created.DestinationId,
                Rating = created.Rating,
                Comment = created.Comment,
                CreatedAt = created.CreatedAt,
                UpdatedAt = created.UpdatedAt
            };

        }
    }
}

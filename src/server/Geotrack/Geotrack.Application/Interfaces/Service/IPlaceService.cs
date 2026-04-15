using Geotrack.Application.DTOs.Places;
namespace Geotrack.Application.Interfaces.Service;
public interface IPlaceService
{
    Task<GetPlaceDetailsResponseDto> GetPlaceDetailsAsync(int placeId, int? userId);
    Task<GetPlaceEventsResponseDto> GetPlaceEventsAsync(int placeId);
    Task<GetPlaceReviewsResponseDto> GetPlaceReviewsAsync(int placeId);
    Task<CreatePlaceReviewResponseDto> CreatePlaceReviewAsync(int placeId, int userId, CreatePlaceReviewRequestDto request);
}

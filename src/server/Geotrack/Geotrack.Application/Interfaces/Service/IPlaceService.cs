using Geotrack.Application.DTOs.Destinations;
using Geotrack.Application.DTOs.Places;
using Geotrack.Application.DTOs.Reviews;
namespace Geotrack.Application.Interfaces.Service;
public interface IPlaceService
{
    // Frontend-facing methods (existing API shape)
    Task<DestinationScopedResult<List<PlaceListItemDto>>> GetPlacesAsync(
        int? destinationId,
        string? type = null,
        string? tags = null,
        string? category = null,
        int? userId = null,
        double? latitude = null,
        double? longitude = null);
    Task<PlaceDetailDto?> GetPlaceDetailAsync(
        int id,
        int? userId = null,
        double? latitude = null,
        double? longitude = null);
    Task<HotelDetailDto?> GetHotelDetailAsync(
        int id,
        int? userId = null,
        double? latitude = null,
        double? longitude = null);
    Task<DestinationScopedResult<List<PlaceListItemDto>>> GetHotelsAsync(
        int? destinationId,
        string? tag = null,
        int? userId = null,
        double? latitude = null,
        double? longitude = null);

    // Master detail/review methods
    Task<GetPlaceDetailsResponseDto> GetPlaceDetailsAsync(int placeId, int? userId);
    Task<GetPlaceEventsResponseDto> GetPlaceEventsAsync(int placeId);
    Task<GetPlaceReviewsResponseDto> GetPlaceReviewsAsync(int placeId);
    Task<CreatePlaceReviewResponseDto> CreatePlaceReviewAsync(int placeId, int userId, CreatePlaceReviewRequestDto request);
}

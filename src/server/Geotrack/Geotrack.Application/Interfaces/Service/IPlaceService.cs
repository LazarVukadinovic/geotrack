using Geotrack.Application.DTOs.Destinations;
using Geotrack.Application.DTOs.Places;
using Geotrack.Application.DTOs.Reviews;
namespace Geotrack.Application.Interfaces.Service;
public interface IPlaceService
{
    // Frontend-facing methods (existing API shape)
    Task<DestinationScopedResult<PlaceListingResponseDto>> GetPlacesAsync(
        int? destinationId,
        string? type = null,
        string? tags = null,
        string? category = null,
        int? userId = null,
        double? latitude = null,
        double? longitude = null,
        int skip = 0,
        int? take = null);
    Task<PlaceDetailDto?> GetPlaceDetailAsync(
        int id,
        int? userId = null,
        double? latitude = null,
        double? longitude = null,
        bool useCurrentLocationDistance = false);
    Task<HotelDetailDto?> GetHotelDetailAsync(
        int id,
        int? userId = null,
        double? latitude = null,
        double? longitude = null,
        bool useCurrentLocationDistance = false);
    Task<DestinationScopedResult<PlaceListingResponseDto>> GetHotelsAsync(
        int? destinationId,
        string? tag = null,
        int? userId = null,
        double? latitude = null,
        double? longitude = null,
        int skip = 0,
        int? take = null);

    // Master detail/review methods
    Task<GetPlaceDetailsResponseDto> GetPlaceDetailsAsync(int placeId, int? userId);
    Task<GetPlaceEventsResponseDto> GetPlaceEventsAsync(int placeId);
    Task<GetPlaceReviewsResponseDto> GetPlaceReviewsAsync(int placeId, int skip = 0, int take = 10);
    Task<CreatePlaceReviewResponseDto> CreatePlaceReviewAsync(int placeId, int userId, CreatePlaceReviewRequestDto request);
}

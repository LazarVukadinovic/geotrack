using Geotrack.Application.DTOs.Locations;
namespace Geotrack.Application.Interfaces.Service;
public interface ILocationService
{
    Task<GetLocationDetailsResponseDto> GetLocationDetailsAsync(int locationId, int? userId);
    Task<GetLocationSectionResponseDto> GetLocationSectionAsync(int locationId, string sectionKey);
    Task<GetLocationReviewsResponseDto> GetLocationReviewsAsync(int locationId, int skip = 0, int take = 10);
    Task<CreateLocationReviewResponseDto> CreateLocationReviewAsync(int locationId, int userId, CreateLocationReviewRequestDto request);
}

using Geotrack.Application.DTOs.Destinations;
using Geotrack.Application.DTOs.Explore;
namespace Geotrack.Application.Interfaces.Service;
using Geotrack.Application.DTOs;
public interface IDestinationService
{
    Task<ExploreByLocationResponseDto> ExploreByLocationAsync(ExploreByLocationQueryDto query);
    Task<GetDestinationDetailsResponseDto> GetDestinationDetailsAsync(int destinationId, int? userId);
    Task<GetDestinationSectionResponseDto> GetDestinationSectionAsync(int destinationId, string sectionKey);
    Task<GetDestinationReviewsResponseDto> GetDestinationReviewsAsync(int destinationId);
    Task<CreateDestinationReviewResponseDto> CreateDestinationReviewAsync(int destinationId, int userId, CreateDestinationReviewRequestDto request);
    Task<List<DestinationSearchResultDto>> SearchAsync(string query);
    Task<List<DestinationSearchItemDto>> SearchWithinDestinationAsync(int destinationId, string query, IEnumerable<string>? types);
}

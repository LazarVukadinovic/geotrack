using Geotrack.Application.DTOs.Destinations;

namespace Geotrack.Application.Interfaces.Service;

public interface IDestinationService
{
    Task<GetDestinationDetailsResponseDto> GetDestinationDetailsAsync(int destinationId, int? userId);
    Task<GetDestinationSectionResponseDto> GetDestinationSectionAsync(int destinationId, string sectionKey);
    Task<GetDestinationReviewsResponseDto> GetDestinationReviewsAsync(int destinationId, int skip = 0, int take = 10);
    Task<CreateDestinationReviewResponseDto> CreateDestinationReviewAsync(int destinationId, int userId, CreateDestinationReviewRequestDto request);
    Task<List<DestinationSearchResultDto>> SearchAsync(string query);
    Task<List<DestinationSearchItemDto>> SearchWithinDestinationAsync(int destinationId, string query, IEnumerable<string>? types);
}

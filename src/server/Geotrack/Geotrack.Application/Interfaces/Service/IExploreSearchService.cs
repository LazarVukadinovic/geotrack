using Geotrack.Application.DTOs.Explore;

namespace Geotrack.Application.Interfaces.Service;

public interface IExploreSearchService
{
    Task<ExploreSearchResponseDto> SearchAsync(
        int destinationId,
        string? query,
        int skip = 0,
        int take = 20,
        CancellationToken cancellationToken = default);
}

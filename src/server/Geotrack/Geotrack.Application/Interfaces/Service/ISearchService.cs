using Geotrack.Application.DTOs.Search;

namespace Geotrack.Application.Interfaces.Service;

public interface ISearchService
{
    Task<GlobalSearchResponseDto> SearchAsync(
        string? query,
        string? domains = null,
        double? latitude = null,
        double? longitude = null,
        string? language = null,
        int take = 6,
        CancellationToken cancellationToken = default);

    Task<SearchMetadataDto> GetMetadataAsync(
        string? language = null,
        CancellationToken cancellationToken = default);
}

using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Application.Interfaces.Service;

public interface IEntityProfileService
{
    Task<EntityProfileDto> GetRegionProfileAsync(int userId, int regionId, CancellationToken ct = default);
    Task<EntityProfileDto> UpdateRegionProfileAsync(int userId, int regionId, EntityProfileUpdateRequestDto request, CancellationToken ct = default);
    Task<List<EntityProfileTagDto>> GetRegionTagsAsync(int userId, int regionId, CancellationToken ct = default);
    Task<RegionalImageUploadResponseDto> UploadRegionImagesAsync(int userId, int regionId, IReadOnlyCollection<RegionalImageUploadFileDto> files, CancellationToken ct = default);

    Task<EntityProfileDto> GetTerritoryProfileAsync(int userId, int territoryId, CancellationToken ct = default);
    Task<EntityProfileDto> UpdateTerritoryProfileAsync(int userId, int territoryId, EntityProfileUpdateRequestDto request, CancellationToken ct = default);
    Task<List<EntityProfileTagDto>> GetTerritoryTagsAsync(int userId, int territoryId, CancellationToken ct = default);
    Task<RegionalImageUploadResponseDto> UploadTerritoryImagesAsync(int userId, int territoryId, IReadOnlyCollection<RegionalImageUploadFileDto> files, CancellationToken ct = default);
}

using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Application.Interfaces.Service;

public interface IRegionalScopeValidationService
{
    Task<RegionalScopeValidationResultDto> ValidatePointAsync(
        int userId,
        int regionId,
        string entityType,
        double latitude,
        double longitude,
        string operationType,
        int? destinationId = null,
        int? locationId = null,
        CancellationToken cancellationToken = default);

    Task EnsurePointAllowedAsync(
        int userId,
        int regionId,
        string entityType,
        double latitude,
        double longitude,
        string operationType,
        int? destinationId = null,
        int? locationId = null,
        CancellationToken cancellationToken = default);
}

using Geotrack.Application.DTOs.Locations;

namespace Geotrack.Application.Interfaces.Service;

public interface IClientLocationResolverService
{
    Task<ApproximateLocationDto?> ResolveApproximateLocationAsync(string? clientIpAddress, CancellationToken cancellationToken = default);
}

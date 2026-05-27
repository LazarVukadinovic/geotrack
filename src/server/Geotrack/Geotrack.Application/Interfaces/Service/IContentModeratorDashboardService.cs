using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Application.Interfaces.Service;

public interface IContentModeratorDashboardService
{
    Task<ContentModeratorDashboardDto> GetDashboardAsync(
        int currentUserId,
        CancellationToken cancellationToken = default);
}

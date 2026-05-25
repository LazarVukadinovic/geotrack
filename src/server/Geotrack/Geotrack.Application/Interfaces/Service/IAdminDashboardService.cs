using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Application.Interfaces.Service;

public interface IAdminDashboardService
{
    Task<AdminDashboardDto> GetDashboardAsync(int currentUserId, CancellationToken cancellationToken = default);
}

using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Application.Interfaces.Service;

public interface IRegionContentAssignmentService
{
    Task<RegionContentAssignmentSummaryDto> AssignContentByBoundaryAsync(
        int territoryId,
        bool dryRun = true,
        CancellationToken cancellationToken = default);
}

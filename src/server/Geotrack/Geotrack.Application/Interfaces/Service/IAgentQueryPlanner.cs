using Geotrack.Application.DTOs.Agent;
using Geotrack.Application.DTOs.GeoTrackTools;

namespace Geotrack.Application.Interfaces.Service;

public interface IAgentQueryPlanner
{
    Task<GeoTrackAgentQueryPlanDto> PlanAsync(
        string message,
        AgentContextOptionsDto? contextOptions,
        CancellationToken cancellationToken = default);
}

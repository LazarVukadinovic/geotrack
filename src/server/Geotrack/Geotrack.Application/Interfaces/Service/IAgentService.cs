using Geotrack.Application.DTOs.Agent;

namespace Geotrack.Application.Interfaces.Service;

public interface IAgentService
{
    Task<AgentResponseDto> ProcessMessageAsync(string text, int? userId = null);
}

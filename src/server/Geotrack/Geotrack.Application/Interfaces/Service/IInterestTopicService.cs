using Geotrack.Application.DTOs.Interests;

namespace Geotrack.Application.Interfaces.Service;

public interface IInterestTopicService
{
    Task<List<InterestTopicGroupDto>> GetOnboardingTopicsAsync();
}

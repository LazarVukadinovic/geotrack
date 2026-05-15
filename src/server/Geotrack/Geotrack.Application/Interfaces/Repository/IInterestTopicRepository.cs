using Geotrack.Domain.Entities.Interests;

namespace Geotrack.Application.Interfaces.Repository;

public interface IInterestTopicRepository
{
    Task<List<InterestTopic>> GetOnboardingTopicsAsync();
}

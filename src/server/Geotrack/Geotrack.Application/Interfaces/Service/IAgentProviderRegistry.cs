namespace Geotrack.Application.Interfaces.Service;

public interface IAgentProviderRegistry
{
    IReadOnlyCollection<IAgentProvider> GetAll();
    IAgentProvider? Get(string providerId);
}

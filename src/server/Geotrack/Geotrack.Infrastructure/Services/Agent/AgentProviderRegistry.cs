using Geotrack.Application.Interfaces.Service;

namespace Geotrack.Infrastructure.Services.Agent;

public class AgentProviderRegistry : IAgentProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IAgentProvider> _providers;

    public AgentProviderRegistry(IEnumerable<IAgentProvider> providers)
    {
        _providers = providers
            .GroupBy(provider => provider.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<IAgentProvider> GetAll() => _providers.Values.ToList();

    public IAgentProvider? Get(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        return _providers.TryGetValue(providerId.Trim(), out var provider)
            ? provider
            : null;
    }
}

using Geotrack.Application.DTOs.Agent;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Options;
using Geotrack.Application.Services;
using Microsoft.Extensions.Options;

namespace Geotrack.Infrastructure.Services.Agent;

public class ConfiguredUnavailableAgentProvider : IAgentProvider
{
    private readonly AgentOptions _options;

    public ConfiguredUnavailableAgentProvider(string id, IOptions<AgentOptions> options)
    {
        Id = id;
        _options = options.Value;
    }

    public string Id { get; }

    public AgentProviderMetadataDto GetMetadata(int? userId)
    {
        var providerOptions = GetOptions();
        return new AgentProviderMetadataDto
        {
            Id = Id,
            DisplayName = string.IsNullOrWhiteSpace(providerOptions?.DisplayName)
                ? Id
                : providerOptions.DisplayName,
            Enabled = false,
            Models = BuildModels(providerOptions),
            DefaultModel = providerOptions?.DefaultModel
        };
    }

    public Task<AgentProviderChatResult> GenerateAsync(
        AgentProviderChatRequest request,
        CancellationToken cancellationToken = default)
    {
        throw new AgentException("provider_not_configured", "This provider is not available yet.", 400);
    }

    private AgentProviderOptions? GetOptions() =>
        _options.Providers.TryGetValue(Id, out var providerOptions) ? providerOptions : null;

    private static List<AgentProviderModelDto> BuildModels(AgentProviderOptions? providerOptions)
    {
        if (providerOptions is null)
        {
            return new List<AgentProviderModelDto>();
        }

        if (providerOptions.Models.Count > 0)
        {
            return providerOptions.Models
                .Where(model => !string.IsNullOrWhiteSpace(model.Id))
                .Select(model => new AgentProviderModelDto
                {
                    Id = model.Id,
                    DisplayName = string.IsNullOrWhiteSpace(model.DisplayName) ? model.Id : model.DisplayName,
                    IsDefault = model.IsDefault || string.Equals(model.Id, providerOptions.DefaultModel, StringComparison.OrdinalIgnoreCase)
                })
                .ToList();
        }

        return string.IsNullOrWhiteSpace(providerOptions.DefaultModel)
            ? new List<AgentProviderModelDto>()
            : new List<AgentProviderModelDto>
            {
                new()
                {
                    Id = providerOptions.DefaultModel,
                    DisplayName = providerOptions.DefaultModel,
                    IsDefault = true
                }
            };
    }
}

using Geotrack.Application.DTOs.Common;
using Geotrack.Application.DTOs.Tags;
using Geotrack.Application.DTOs.Territories;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Core;

namespace Geotrack.Application.Services;

public class TerritoryService : ITerritoryService
{
    private readonly ITerritoryRepository _territoryRepository;
    private readonly IRegionRepository _regionRepository;

    public TerritoryService(
        ITerritoryRepository territoryRepository,
        IRegionRepository regionRepository)
    {
        _territoryRepository = territoryRepository;
        _regionRepository = regionRepository;
    }

    public async Task<GetTerritoriesResponseDto> GetTerritoriesAsync()
    {
        var territories = await _territoryRepository.GetAllAsync();

        return new GetTerritoriesResponseDto
        {
            Territories = territories.Select(t => new TerritoryItemDto
            {
                Id = t.Id,
                Name = t.Name
            }).ToList()
        };
    }

    public async Task<GetTerritoryDetailsResponseDto> GetTerritoryDetailsAsync(int territoryId)
    {
        var territory = await _territoryRepository.GetByIdWithRegionsAsync(territoryId);

        if (territory == null)
            throw new KeyNotFoundException($"Territory with id '{territoryId}' was not found.");

        return new GetTerritoryDetailsResponseDto
        {
            Id = territory.Id,
            Name = territory.Name,
            Description = territory.Description,
            Images = territory.Images.ToPublicEntityImageDtos(),
            Tags = territory.TerritoryTags
                .Select(tt => new TagDto { Id = tt.Tag.Id, Name = tt.Tag.Name, Slug = tt.Tag.Slug })
                .OrderBy(t => t.Name)
                .ToList(),
            Regions = territory.Regions
                .Where(r => r.Destinations.Any(d => d.Status == "Published"))
                .OrderBy(r => r.Name)
                .Select(r => new TerritoryRegionItemDto
                {
                    Id = r.Id,
                    Name = r.Name,
                    Description = r.Description,
                    Image = r.Images
                        .OrderBy(i => i.ImageRole == "card" ? 0 : i.ImageRole == "cover" ? 1 : 2)
                        .ThenBy(i => i.SortOrder)
                        .Select(i => i.Url)
                        .FirstOrDefault()
                }).ToList()
        };
    }

    public async Task<CreateTerritoryResponseDto> CreateTerritoryAsync(CreateTerritoryRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new InvalidOperationException("Territory name cannot be empty.");

        var name = request.Name.Trim();

        var exists = await _territoryRepository.ExistsAsync(name);

        if (exists)
            throw new InvalidOperationException($"Territory with name '{name}' already exists.");

        var territory = new Territory
        {
            Name = name,
            CreatedAt = DateTime.UtcNow
        };

        await _territoryRepository.AddAsync(territory);
        await _territoryRepository.SaveChangesAsync();

        return new CreateTerritoryResponseDto
        {
            Id = territory.Id,
            Name = territory.Name
        };
    }

    public async Task DeleteTerritoryAsync(int territoryId)
    {
        var territory = await _territoryRepository.GetByIdWithRegionsAsync(territoryId);

        if (territory == null)
            throw new KeyNotFoundException($"Territory with id '{territoryId}' was not found.");

        if (territory.Regions.Any())
            throw new InvalidOperationException("Cannot delete territory because it has existing regions.");

        await _territoryRepository.DeleteAsync(territory);
        await _territoryRepository.SaveChangesAsync();
    }

    public async Task<CreateRegionInTerritoryResponseDto> CreateRegionAsync(
        int territoryId,
        CreateRegionInTerritoryRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new InvalidOperationException("Region name cannot be empty.");

        var territory = await _territoryRepository.GetByIdAsync(territoryId);

        if (territory == null)
            throw new KeyNotFoundException($"Territory with id '{territoryId}' was not found.");

        var name = request.Name.Trim();

        var exists = await _regionRepository.ExistsInTerritoryAsync(territoryId, name);

        if (exists)
            throw new InvalidOperationException($"Region with name '{name}' already exists in this territory.");

        var region = new Region
        {
            Name = name,
            TerritoryId = territoryId,
            CreatedAt = DateTime.UtcNow
        };

        await _regionRepository.AddAsync(region);
        await _regionRepository.SaveChangesAsync();

        return new CreateRegionInTerritoryResponseDto
        {
            Id = region.Id,
            Name = region.Name,
            TerritoryId = region.TerritoryId
        };
    }

    public async Task DeleteRegionAsync(int territoryId, int regionId)
    {
        var region = await _regionRepository.GetByIdAsync(regionId);

        if (region == null)
            throw new KeyNotFoundException($"Region with id '{regionId}' was not found.");

        if (region.TerritoryId != territoryId)
            throw new InvalidOperationException("Region does not belong to this territory.");

        var hasDestinations = await _regionRepository.HasDestinationsAsync(regionId);

        if (hasDestinations)
            throw new InvalidOperationException("Cannot delete region because it has existing destinations.");

        await _regionRepository.DeleteAsync(region);
        await _regionRepository.SaveChangesAsync();
    }
}
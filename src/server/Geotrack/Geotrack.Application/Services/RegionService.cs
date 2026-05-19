using Geotrack.Application.DTOs.Region;
using Geotrack.Application.DTOs.Users;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Core;

namespace Geotrack.Application.Services
{
    public class RegionService : IRegionService
    {
        private readonly IRegionRepository _regionRepository;
        private readonly ITerritoryRepository _territoryRepository;

        public RegionService(IRegionRepository regionRepository, ITerritoryRepository territoryRepository)
        {
            _regionRepository = regionRepository;
            _territoryRepository = territoryRepository;
        }

        public async Task<GetRegionsResponseDto> GetRegionsAsync()
        {
            var regions = await _regionRepository.GetAllAsync();

            return new GetRegionsResponseDto
            {
                Regions = regions.Select(r => new RegionItemDto
                {
                    Id = r.Id,
                    Name = r.Name
                }).ToList()
            };
        }

        public async Task<GetRegionDetailsResponseDto> GetRegionDetailsAsync(int regionId)
        {
            var region = await _regionRepository.GetByIdWithManagersAsync(regionId);

            if (region == null)
                throw new KeyNotFoundException($"Region with id '{regionId}' was not found.");

            return new GetRegionDetailsResponseDto
            {
                Id = region.Id,
                Name = region.Name,
                TerritoryId = region.TerritoryId,
                TerritoryName = region.Territory?.Name,
                Managers = region.RegionalManagerRegions
                    .Where(ror => ror.IsActive)
                    .Select(ror => new UserItemDto
                    {
                        Id = ror.User.Id,
                        Email = ror.User.Email
                    })
                    .ToList()
            };
        }

        public async Task<CreateRegionResponseDto> CreateRegionAsync(CreateRegionRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                throw new InvalidOperationException("Region name cannot be empty.");

            if (request.TerritoryId <= 0)
                throw new InvalidOperationException("A valid territory id is required.");

            var name = request.Name.Trim();
            var territory = await _territoryRepository.GetByIdAsync(request.TerritoryId);
            if (territory == null)
                throw new KeyNotFoundException($"Territory with id '{request.TerritoryId}' was not found.");

            var exists = await _regionRepository.ExistsInTerritoryAsync(request.TerritoryId, name);

            if (exists)
                throw new InvalidOperationException($"Region with name '{name}' already exists in this territory.");

            var region = new Region
            {
                Name = name,
                TerritoryId = request.TerritoryId,
                CreatedAt = DateTime.UtcNow
            };

            await _regionRepository.AddAsync(region);
            await _regionRepository.SaveChangesAsync();

            return new CreateRegionResponseDto
            {
                Id = region.Id,
                Name = region.Name
            };
        }

        public async Task DeleteRegionAsync(int regionId)
        {
            var region = await _regionRepository.GetByIdAsync(regionId);

            if (region == null)
                throw new KeyNotFoundException($"Region with id '{regionId}' was not found.");

            var hasDestinations = await _regionRepository.HasDestinationsAsync(regionId);

            if (hasDestinations)
                throw new InvalidOperationException("Cannot delete region because it has existing destinations.");

            await _regionRepository.DeleteAsync(region);
            await _regionRepository.SaveChangesAsync();
        }
    }
}

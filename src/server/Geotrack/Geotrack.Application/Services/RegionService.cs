using Geotrack.Application.DTOs.Destinations;
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
                Description = region.Description,
                TerritoryId = region.TerritoryId,
                TerritoryName = region.Territory?.Name,
                Managers = region.RegionalManagerRegions
                    .Where(ror => ror.IsActive)
                    .Select(ror => new UserItemDto
                    {
                        Id = ror.User.Id,
                        Email = ror.User.Email
                    })
                    .ToList(),
                Destinations = region.Destinations
                    .Where(d => d.Status == "Published")
                    .OrderBy(d => d.Name)
                    .Select(d => new DestinationSummaryDto
                    {
                        Id = d.Id,
                        Name = d.Name,
                        Description = string.IsNullOrWhiteSpace(d.Summary) ? d.Description : d.Summary,
                        Image = d.Images
                            .OrderBy(image => image.ImageRole == "card" ? 0 : image.ImageRole == "cover" ? 1 : 2)
                            .ThenBy(image => image.SortOrder)
                            .Select(image => image.Url)
                            .FirstOrDefault(),
                        AverageRating = d.DestinationReviews
                            .Select(review => (double?)review.Rating)
                            .Average() ?? 0
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

        public async Task<GetRegionDetailsResponseDto> UpdateRegionAsync(int regionId, UpdateRegionRequestDto request)
        {
            var region = await _regionRepository.GetByIdAsync(regionId);

            if (region == null)
                throw new KeyNotFoundException($"Region with id '{regionId}' was not found.");

            if (request.Name is not null)
            {
                var name = request.Name.Trim();
                if (name.Length == 0)
                    throw new InvalidOperationException("Region name cannot be empty.");

                var exists = await _regionRepository.ExistsInTerritoryAsync(region.TerritoryId, name);
                if (exists && !string.Equals(region.Name, name, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Region with name '{name}' already exists in this territory.");

                region.Name = name;
            }

            await _regionRepository.SaveChangesAsync();
            return await GetRegionDetailsAsync(regionId);
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

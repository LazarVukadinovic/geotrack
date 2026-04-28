using Geotrack.Application.DTOs.Region;
using Geotrack.Application.DTOs.Users;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Application.Interfaces.Service;

namespace Geotrack.Application.Services
{
    public class RegionService : IRegionService
    {
        private readonly IRegionRepository _regionRepository;

        public RegionService(IRegionRepository regionRepository)
        {
            _regionRepository = regionRepository;
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
                Managers = region.RegionalOrganizationRegions
                    .Select(ror => new UserItemDto
                    {
                        Id = ror.RegionalOrganization.User.Id,
                        Email = ror.RegionalOrganization.User.Email
                    })
                    .ToList()
            };
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

        public async Task<CreateRegionResponseDto> CreateRegionAsync(CreateRegionRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                throw new InvalidOperationException("Region name cannot be empty.");

            bool exists = await _regionRepository.ExistsAsync(request.Name);

            if (exists)
                throw new InvalidOperationException($"Region with name '{request.Name}' already exists.");

            var region = new Region
            {
                Name = request.Name
            };

            await _regionRepository.AddAsync(region);
            await _regionRepository.SaveChangesAsync();

            return new CreateRegionResponseDto
            {
                Id = region.Id,
                Name = region.Name
            };
        }
    }
}

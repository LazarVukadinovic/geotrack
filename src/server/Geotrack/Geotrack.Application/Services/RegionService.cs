using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
    }
}

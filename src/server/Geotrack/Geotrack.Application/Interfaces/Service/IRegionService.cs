using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Application.DTOs.Region;
using Geotrack.Application.DTOs.Users;

namespace Geotrack.Application.Interfaces.Service
{
    public interface IRegionService
    {
        Task<GetRegionsResponseDto> GetRegionsAsync();
        Task<GetRegionDetailsResponseDto> GetRegionDetailsAsync(int regionId);

        Task<CreateRegionResponseDto> CreateRegionAsync(CreateRegionRequestDto request);
    }
}

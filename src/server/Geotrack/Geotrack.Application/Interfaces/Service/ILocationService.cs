using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Application.DTOs.Location;

namespace Geotrack.Application.Interfaces.Service
{
    public interface ILocationService
    {
        Task<GetLocationDetailsResponseDto> GetLocationDetailsAsync(int locationId, int? userId);
        Task<GetLocationSectionResponseDto> GetLocationSectionAsync(int locationId, string sectionKey);
    }
}

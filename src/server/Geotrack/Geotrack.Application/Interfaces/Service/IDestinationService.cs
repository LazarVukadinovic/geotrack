using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Application.DTOs.Explore;

namespace Geotrack.Application.Interfaces.Service
{
    public interface IDestinationService
    {
        Task<ExploreByLocationResponseDto> ExploreByLocationAsync(ExploreByLocationQueryDto query);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Application.DTOs.Map;

namespace Geotrack.Application.Interfaces.Service
{
    public interface IMapService
    {
        Task<List<MapSearchResultDto>> SearchAsync(string query, IEnumerable<string>? types);
    }
}

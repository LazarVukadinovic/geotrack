using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Domain.Entities.Core;

namespace Geotrack.Application.Interfaces.Repository
{
    public interface IRegionRepository : IRepository<Region>
    {
        
        Task<Region?> GetByIdWithManagersAsync(int regionId);
        Task<bool> ExistsAsync(string name);
        
    }
}

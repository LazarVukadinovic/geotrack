using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Domain.Entities.Core;

namespace Geotrack.Infrastructure.Repositories
{
    public class PlaceRepository : Repository<Place>, IPlaceRepository
    {
        public PlaceRepository(AppDbContext context) : base(context){}
    }
}

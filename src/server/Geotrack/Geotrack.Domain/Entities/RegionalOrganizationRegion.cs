using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Domain.Entities
{
    public class RegionalOrganizationRegion
    {
        public int UserId { get; set; }
        public RegionalOrganization RegionalOrganization { get; set; } = null!; // NAVIGATIONAL
        public int RegionId { get; set; }
        public Region Region { get; set; } = null!; // NAVIGATIONAL

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}

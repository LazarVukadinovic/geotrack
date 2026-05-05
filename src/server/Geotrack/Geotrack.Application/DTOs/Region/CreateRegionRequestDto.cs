using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.DTOs.Region
{
    public class CreateRegionRequestDto
    {
        public string Name { get; set; } = string.Empty;
        public int TerritoryId { get; set; }
    }
}

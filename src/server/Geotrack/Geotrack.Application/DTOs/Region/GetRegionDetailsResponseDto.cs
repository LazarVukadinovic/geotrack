using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Application.DTOs.Destinations;

namespace Geotrack.Application.DTOs.Region
{
    public class GetRegionDetailsResponseDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int TerritoryId { get; set; }
        public string? TerritoryName { get; set; }

        public List<DestinationSummaryDto> Destinations { get; set; } = new();
        public List<UserItemDto> Managers { get; set; } = new();
    }
}

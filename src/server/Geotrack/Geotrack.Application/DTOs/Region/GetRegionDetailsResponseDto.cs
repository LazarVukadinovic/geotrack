using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Application.DTOs.Common;
using Geotrack.Application.DTOs.Destinations;
using Geotrack.Application.DTOs.Tags;

namespace Geotrack.Application.DTOs.Region
{
    public class GetRegionDetailsResponseDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int TerritoryId { get; set; }
        public string? TerritoryName { get; set; }

        public List<PublicEntityImageDto> Images { get; set; } = new();
        public List<TagDto> Tags { get; set; } = new();
        public List<DestinationSummaryDto> Destinations { get; set; } = new();
        public List<UserItemDto> Managers { get; set; } = new();
    }
}

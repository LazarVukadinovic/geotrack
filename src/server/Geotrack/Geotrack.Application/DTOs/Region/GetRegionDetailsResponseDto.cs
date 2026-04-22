using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.DTOs.Region
{
    public class GetRegionDetailsResponseDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<UserItemDto> Managers { get; set; } = new();
    }
}

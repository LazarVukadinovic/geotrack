using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.DTOs.Users
{
    public class UpdateUserRegionsResponseDto
    {
        public int UserId { get; set; }
        public List<int> RegionIds { get; set; } = new();
    }
}

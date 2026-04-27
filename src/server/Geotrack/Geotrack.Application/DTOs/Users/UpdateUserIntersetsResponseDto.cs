using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geotrack.Application.DTOs.Users;

public class UpdateUserInterestsResponseDto
{
    public List<InterestItemDto> ExplicitTags { get; set; } = new();
    public List<InterestItemDto> Tags { get; set; } = new();
}

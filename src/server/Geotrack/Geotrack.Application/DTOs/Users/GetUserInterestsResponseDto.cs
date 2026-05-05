using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace Geotrack.Application.DTOs.Users;

public class GetUserInterestsResponseDto
{
    public List<InterestItemDto> ExplicitTags { get; set; } = new();
    public List<InferredInterestItemDto> InferredTags { get; set; } = new();

    // Backwards-compatible alias used by existing clients for explicit interests.
    public List<InterestItemDto> Tags { get; set; } = new();
}

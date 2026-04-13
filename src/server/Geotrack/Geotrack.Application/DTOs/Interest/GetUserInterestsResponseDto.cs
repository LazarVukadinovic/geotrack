namespace Geotrack.Application.DTOs.Interest;

public class GetUserInterestsResponseDto
{
    public List<InterestItemDto> Tags { get; set; } = new();
}
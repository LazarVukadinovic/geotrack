namespace Geotrack.Application.DTOs.Interest;

public class UpdateUserInterestsResponseDto
{
    public List<InterestItemDto> Tags { get; set; } = new();
}
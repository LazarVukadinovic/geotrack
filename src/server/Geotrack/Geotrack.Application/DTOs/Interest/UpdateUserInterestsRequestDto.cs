namespace Geotrack.Application.DTOs.Interest;

public class UpdateUserInterestsRequestDto
{
    public List<int> TagIds { get; set; } = new();
}
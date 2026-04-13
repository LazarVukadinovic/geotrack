namespace Geotrack.Application.DTOs.Collection;

public class CreateCollectionRequestDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
namespace Geotrack.Server.Requests;

public class CreateCollectionRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
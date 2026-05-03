namespace Geotrack.Server.Requests;

public class PlaceContributionFormRequest
{
    public string Payload { get; set; } = string.Empty;
    public IFormFile? MainImage { get; set; }
}

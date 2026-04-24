namespace Geotrack.Server.Requests;

public class StayContributionFormRequest
{
    public string Payload { get; set; } = string.Empty;
    public IFormFile? CoverImage { get; set; }
}

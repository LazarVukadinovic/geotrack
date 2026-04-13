namespace Geotrack.Server.Responses;

public class GetUserInterestsResponse
{
    public List<InterestItemResponse> Tags { get; set; } = new();
}
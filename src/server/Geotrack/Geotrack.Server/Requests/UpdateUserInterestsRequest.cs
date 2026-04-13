namespace Geotrack.Server.Requests;

public class UpdateUserInterestsRequest
{
    public List<int> TagIds { get; set; } = new();
}
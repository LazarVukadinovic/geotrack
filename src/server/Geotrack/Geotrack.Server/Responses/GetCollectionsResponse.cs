namespace Geotrack.Server.Responses;

public class GetCollectionsResponse
{
    public List<CollectionListItemResponse> Collections { get; set; } = new();
}
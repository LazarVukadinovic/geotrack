using Geotrack.Domain.Entities;

namespace Geotrack.Server.Responses
{
    public class ExploreResponse
    {
        Destination? SelectedDestination { get; set; }
        IEnumerable<Section>? Sections { get; set; }
    }
}

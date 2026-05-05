namespace Geotrack.Application.DTOs.Destinations;

public enum DestinationScopedStatus
{
    Success,
    InvalidDestination,
    DestinationNotFound,
}

public class DestinationScopedResult<T>
{
    public DestinationScopedStatus Status { get; set; }
    public T? Data { get; set; }
}

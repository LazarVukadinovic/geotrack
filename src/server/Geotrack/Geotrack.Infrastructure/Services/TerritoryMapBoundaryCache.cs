using Microsoft.Extensions.Caching.Memory;

namespace Geotrack.Infrastructure.Services;

internal static class TerritoryMapBoundaryCache
{
    public const string BoundariesKey = "admin-territory-map-boundaries-v1";

    public static void Invalidate(IMemoryCache? cache)
    {
        cache?.Remove(BoundariesKey);
    }
}

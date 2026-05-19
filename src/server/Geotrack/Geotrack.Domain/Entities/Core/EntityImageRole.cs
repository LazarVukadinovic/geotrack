namespace Geotrack.Domain.Entities.Core;

public static class EntityImageRole
{
    public const string Card = "card";
    public const string Cover = "cover";
    public const string Gallery = "gallery";

    public static string Normalize(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is Card or Cover or Gallery ? normalized : Gallery;
    }
}

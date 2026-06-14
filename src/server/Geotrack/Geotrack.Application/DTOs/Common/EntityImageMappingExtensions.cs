using Geotrack.Domain.Entities.Core;

namespace Geotrack.Application.DTOs.Common;

public static class EntityImageMappingExtensions
{
    public static List<PublicEntityImageDto> ToPublicEntityImageDtos(this IEnumerable<EntityImage>? images)
    {
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return (images ?? Enumerable.Empty<EntityImage>())
            .Where(image => !string.IsNullOrWhiteSpace(image.Url))
            .OrderBy(image => ImageRoleSort(image.ImageRole))
            .ThenBy(image => image.SortOrder)
            .ThenBy(image => image.Id)
            .Where(image => seenUrls.Add(image.Url.Trim()))
            .Select(image => new PublicEntityImageDto
            {
                Id = image.Id,
                Url = image.Url.Trim(),
                AltText = image.AltText,
                Caption = image.Caption,
                IsMain = image.IsMain,
                ImageRole = image.ImageRole,
                SortOrder = image.SortOrder,
                ImagePosition = string.IsNullOrWhiteSpace(image.ImagePosition) ? "50% 50%" : image.ImagePosition,
                ImageSize = string.IsNullOrWhiteSpace(image.ImageSize) ? "cover" : image.ImageSize
            })
            .ToList();
    }

    private static int ImageRoleSort(string? role)
    {
        return role?.Trim().ToLowerInvariant() switch
        {
            "cover" => 0,
            "card" => 1,
            "gallery" => 2,
            _ => 3
        };
    }
}

using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Infrastructure.Services;

internal static class RegionalEntityImageRequestNormalizer
{
    private const int MaxImages = 12;
    private const string ManagedRegionalMediaPrefix = "/media/uploads/regional/";
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".webp"
    };

    public static List<PreparedRegionalEntityImage> Normalize(
        IEnumerable<RegionalEntityImageInputDto>? images,
        string fallbackAltText,
        ISet<string>? existingEventImageUrls = null)
    {
        var inputs = (images ?? Enumerable.Empty<RegionalEntityImageInputDto>())
            .Select((image, index) => new { Image = image, Index = index })
            .Where(item => !string.IsNullOrWhiteSpace(item.Image.Url))
            .Take(MaxImages + 1)
            .ToList();

        if (inputs.Count > MaxImages)
            throw new InvalidOperationException($"A maximum of {MaxImages} images can be attached.");

        if (inputs.Count == 0)
            return new List<PreparedRegionalEntityImage>();

        var mainIndex = inputs.FindIndex(item => item.Image.IsMain);
        if (mainIndex < 0)
            mainIndex = 0;

        return inputs
            .Select((item, normalizedIndex) =>
            {
                var image = item.Image;
                return new PreparedRegionalEntityImage(
                    NormalizeManagedRegionalImageUrl(image.Url, existingEventImageUrls),
                    NormalizeOptionalText(image.AltText, 300, "Image alt text is too long.") ?? fallbackAltText,
                    NormalizeOptionalText(image.Caption, 500, "Image caption is too long."),
                    normalizedIndex == mainIndex,
                    image.SortOrder < 0 ? item.Index : image.SortOrder);
            })
            .ToList();
    }

    private static string NormalizeManagedRegionalImageUrl(string? value, ISet<string>? existingEventImageUrls)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > 1000)
            throw new InvalidOperationException("Image URL is invalid.");

        if (existingEventImageUrls?.Contains(normalized) == true)
            return normalized;

        if (!normalized.StartsWith(ManagedRegionalMediaPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Image URL must come from the regional image upload endpoint.");

        if (normalized.Contains("..", StringComparison.Ordinal) ||
            normalized.Contains('\\', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Image URL is invalid.");
        }

        var fileName = Path.GetFileName(normalized);
        if (string.IsNullOrWhiteSpace(fileName) ||
            !string.Equals(ManagedRegionalMediaPrefix + fileName, normalized, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Image URL is invalid.");
        }

        var extension = Path.GetExtension(fileName);
        if (!AllowedExtensions.Contains(extension))
            throw new InvalidOperationException("Only PNG, JPG/JPEG, or WEBP images are supported.");

        return ManagedRegionalMediaPrefix + fileName;
    }

    private static string? NormalizeOptionalText(string? value, int maxLength, string tooLongMessage)
    {
        if (value is null)
            return null;

        var normalized = value.Trim();
        if (normalized.Length == 0)
            return null;
        if (normalized.Length > maxLength)
            throw new InvalidOperationException(tooLongMessage);

        return normalized;
    }
}

internal sealed record PreparedRegionalEntityImage(
    string Url,
    string? AltText,
    string? Caption,
    bool IsMain,
    int SortOrder);

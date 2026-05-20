using Geotrack.Application.Interfaces.Service;

namespace Geotrack.Server.Services;

public sealed class LocalRegionalEntityImageStorageService : IRegionalEntityImageStorageService
{
    private const int MaxImageBytes = 8 * 1024 * 1024;
    private const string RegionalUrlPrefix = "/media/uploads/regional/";

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/jpg",
        "image/webp"
    };

    private readonly string _mediaDirectory;

    public LocalRegionalEntityImageStorageService(IWebHostEnvironment environment)
    {
        _mediaDirectory = Path.Combine(environment.ContentRootPath, "media", "uploads", "regional");
    }

    public async Task<StoredRegionalEntityImageFile> StoreImageAsync(
        int userId,
        byte[] fileBytes,
        string? originalFileName,
        string? contentType,
        CancellationToken cancellationToken = default)
    {
        if (fileBytes.Length == 0)
            throw new InvalidOperationException("Please select an image to upload.");

        if (fileBytes.Length > MaxImageBytes)
            throw new InvalidOperationException("Regional image must be 8 MB or smaller.");

        if (!string.IsNullOrWhiteSpace(contentType) && !AllowedContentTypes.Contains(contentType.Trim()))
            throw new InvalidOperationException("Only PNG, JPG/JPEG, or WEBP images are supported.");

        var detectedImage = DetectImageType(fileBytes)
            ?? throw new InvalidOperationException("Only PNG, JPG/JPEG, or WEBP images are supported.");

        Directory.CreateDirectory(_mediaDirectory);

        var fileName = $"user-{userId}-regional-{Guid.NewGuid():N}{detectedImage.Extension}";
        var filePath = Path.Combine(_mediaDirectory, fileName);

        await File.WriteAllBytesAsync(filePath, fileBytes, cancellationToken);

        return new StoredRegionalEntityImageFile
        {
            MediaUrl = $"{RegionalUrlPrefix}{fileName}",
            SizeBytes = fileBytes.LongLength
        };
    }

    private static DetectedImage? DetectImageType(ReadOnlySpan<byte> fileBytes)
    {
        if (fileBytes.Length >= 8 &&
            fileBytes[0] == 0x89 &&
            fileBytes[1] == 0x50 &&
            fileBytes[2] == 0x4E &&
            fileBytes[3] == 0x47 &&
            fileBytes[4] == 0x0D &&
            fileBytes[5] == 0x0A &&
            fileBytes[6] == 0x1A &&
            fileBytes[7] == 0x0A)
        {
            return new DetectedImage(".png");
        }

        if (fileBytes.Length >= 3 &&
            fileBytes[0] == 0xFF &&
            fileBytes[1] == 0xD8 &&
            fileBytes[2] == 0xFF)
        {
            return new DetectedImage(".jpg");
        }

        if (fileBytes.Length >= 12 &&
            fileBytes[0] == 0x52 &&
            fileBytes[1] == 0x49 &&
            fileBytes[2] == 0x46 &&
            fileBytes[3] == 0x46 &&
            fileBytes[8] == 0x57 &&
            fileBytes[9] == 0x45 &&
            fileBytes[10] == 0x42 &&
            fileBytes[11] == 0x50)
        {
            return new DetectedImage(".webp");
        }

        return null;
    }

    private sealed record DetectedImage(string Extension);
}

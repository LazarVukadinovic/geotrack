using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Options;
using Microsoft.Extensions.Options;

namespace Geotrack.Server.Services;

public sealed class LocalRegionalEntityImageStorageService : IRegionalEntityImageStorageService
{
    private const string RegionalUrlPrefix = "/media/uploads/regional/";

    private readonly string _mediaDirectory;
    private readonly UploadLimitsOptions _uploadLimits;

    public LocalRegionalEntityImageStorageService(
        IWebHostEnvironment environment,
        IOptions<UploadLimitsOptions> uploadLimits)
    {
        _mediaDirectory = Path.Combine(environment.ContentRootPath, "media", "uploads", "regional");
        _uploadLimits = uploadLimits.Value;
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

        if (fileBytes.Length > _uploadLimits.Images.MaxFileSizeBytes)
            throw new InvalidOperationException($"Regional image must be {FormatBytes(_uploadLimits.Images.MaxFileSizeBytes)} or smaller.");

        var extension = Path.GetExtension(originalFileName ?? string.Empty).ToLowerInvariant();
        var normalizedContentType = NormalizeContentType(contentType);
        if (!ContainsOrdinalIgnoreCase(_uploadLimits.Images.RegionalAllowedExtensions, extension) ||
            !ContainsOrdinalIgnoreCase(_uploadLimits.Images.RegionalAllowedContentTypes, normalizedContentType))
        {
            throw new InvalidOperationException("Only PNG, JPG/JPEG, or WEBP images are supported.");
        }

        var detectedImage = DetectImageType(fileBytes)
            ?? throw new InvalidOperationException("Only PNG, JPG/JPEG, or WEBP images are supported.");

        if (!MatchesDeclaredImage(detectedImage, extension, normalizedContentType))
            throw new InvalidOperationException("Only PNG, JPG/JPEG, or WEBP images are supported.");

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

    private static bool MatchesDeclaredImage(
        DetectedImage detectedImage,
        string extension,
        string contentType)
    {
        if (detectedImage.Extension == ".jpg")
        {
            return (extension == ".jpg" || extension == ".jpeg") &&
                (contentType == "image/jpeg" || contentType == "image/jpg");
        }

        return detectedImage.Extension == extension &&
            detectedImage.Extension switch
            {
                ".png" => contentType == "image/png",
                ".webp" => contentType == "image/webp",
                _ => false
            };
    }

    private static string NormalizeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return string.Empty;
        }

        return contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
    }

    private static bool ContainsOrdinalIgnoreCase(IEnumerable<string> values, string value) =>
        values.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));

    private static string FormatBytes(long bytes)
    {
        const long oneMegabyte = 1024L * 1024L;
        if (bytes % oneMegabyte == 0)
        {
            return $"{bytes / oneMegabyte} MB";
        }

        return $"{bytes} bytes";
    }

    private sealed record DetectedImage(string Extension);
}

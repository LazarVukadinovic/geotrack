using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Hosting;

namespace Geotrack.Server.Services;

public sealed class LocalContributionMediaStorageService : IContributionMediaStorageService
{
    private const int MaxImageBytes = 8 * 1024 * 1024;
    private const int MaxOwnershipProofBytes = 10 * 1024 * 1024;
    private const string ContributionUrlPrefix = "/media/uploads/contributions/";

    private readonly string _mediaDirectory;
    private readonly string _mediaDirectoryFullPath;

    public LocalContributionMediaStorageService(IWebHostEnvironment environment)
    {
        _mediaDirectory = Path.Combine(environment.ContentRootPath, "media", "uploads", "contributions");
        _mediaDirectoryFullPath = Path.GetFullPath(_mediaDirectory);
    }

    public async Task<StoredContributionMediaFile> StoreAsync(int userId, byte[] fileBytes)
    {
        if (fileBytes.Length == 0)
            throw new InvalidOperationException("Please select an image to upload.");

        if (fileBytes.Length > MaxImageBytes)
            throw new InvalidOperationException("Contribution image must be 8 MB or smaller.");

        var detectedImage = DetectImageType(fileBytes)
            ?? throw new InvalidOperationException("Only PNG, JPG, GIF, or WEBP images are supported.");

        Directory.CreateDirectory(_mediaDirectory);

        var fileName = $"user-{userId}-stay-{Guid.NewGuid():N}{detectedImage.Extension}";
        var filePath = Path.Combine(_mediaDirectory, fileName);

        await File.WriteAllBytesAsync(filePath, fileBytes);

        return new StoredContributionMediaFile
        {
            MediaUrl = $"{ContributionUrlPrefix}{fileName}",
        };
    }

    public async Task<StoredContributionMediaFile> StoreOwnershipProofAsync(
        int userId,
        byte[] fileBytes,
        string? originalFileName,
        string? contentType)
    {
        if (fileBytes.Length == 0)
            throw new InvalidOperationException("Please select an ownership proof document to upload.");

        if (fileBytes.Length > MaxOwnershipProofBytes)
            throw new InvalidOperationException("Ownership proof document must be 10 MB or smaller.");

        var detectedFile = DetectOwnershipProofFile(fileBytes, originalFileName, contentType)
            ?? throw new InvalidOperationException("Ownership proof document must be a PDF file.");

        Directory.CreateDirectory(_mediaDirectory);

        var fileName = $"user-{userId}-ownership-proof-{Guid.NewGuid():N}{detectedFile.Extension}";
        var filePath = Path.Combine(_mediaDirectory, fileName);

        await File.WriteAllBytesAsync(filePath, fileBytes);

        return new StoredContributionMediaFile
        {
            MediaUrl = $"{ContributionUrlPrefix}{fileName}",
        };
    }

    public Task DeleteAsync(string mediaUrl)
    {
        if (!TryResolveManagedPath(mediaUrl, out var filePath))
            return Task.CompletedTask;

        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return Task.CompletedTask;
    }

    public bool IsManagedContributionMediaUrl(string? mediaUrl) =>
        TryResolveManagedPath(mediaUrl, out _);

    private bool TryResolveManagedPath(string? mediaUrl, out string filePath)
    {
        filePath = string.Empty;

        if (string.IsNullOrWhiteSpace(mediaUrl) ||
            !mediaUrl.StartsWith(ContributionUrlPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = Path.GetFileName(mediaUrl);
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        var resolvedPath = Path.GetFullPath(Path.Combine(_mediaDirectory, fileName));
        if (!resolvedPath.StartsWith(_mediaDirectoryFullPath, StringComparison.OrdinalIgnoreCase))
            return false;

        filePath = resolvedPath;
        return true;
    }

    private static DetectedUploadFile? DetectOwnershipProofFile(
        ReadOnlySpan<byte> fileBytes,
        string? originalFileName,
        string? contentType)
    {
        var extension = Path.GetExtension(originalFileName ?? string.Empty).ToLowerInvariant();
        var normalizedContentType = contentType?.Trim().ToLowerInvariant();

        if (fileBytes.Length >= 5 &&
            fileBytes[0] == 0x25 &&
            fileBytes[1] == 0x50 &&
            fileBytes[2] == 0x44 &&
            fileBytes[3] == 0x46 &&
            fileBytes[4] == 0x2D &&
            extension == ".pdf" &&
            normalizedContentType == "application/pdf")
        {
            return new DetectedUploadFile(".pdf");
        }

        return null;
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

        if (fileBytes.Length >= 6 &&
            fileBytes[0] == 0x47 &&
            fileBytes[1] == 0x49 &&
            fileBytes[2] == 0x46 &&
            fileBytes[3] == 0x38 &&
            (fileBytes[4] == 0x37 || fileBytes[4] == 0x39) &&
            fileBytes[5] == 0x61)
        {
            return new DetectedImage(".gif");
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
    private sealed record DetectedUploadFile(string Extension);
}

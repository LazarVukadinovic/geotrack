using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Hosting;

namespace Geotrack.Server.Services;

public sealed class LocalUserAvatarStorageService : IUserAvatarStorageService
{
    private const int MaxAvatarBytes = 5 * 1024 * 1024;
    private const string AvatarUrlPrefix = "/media/uploads/avatars/";

    private readonly string _avatarDirectory;
    private readonly string _avatarDirectoryFullPath;

    public LocalUserAvatarStorageService(IWebHostEnvironment environment)
    {
        _avatarDirectory = Path.Combine(environment.ContentRootPath, "media", "uploads", "avatars");
        _avatarDirectoryFullPath = Path.GetFullPath(_avatarDirectory);
    }

    public async Task<StoredAvatarFile> StoreAsync(int userId, byte[] fileBytes)
    {
        if (fileBytes.Length == 0)
            throw new InvalidOperationException("Please select an image to upload.");

        if (fileBytes.Length > MaxAvatarBytes)
            throw new InvalidOperationException("Profile image must be 5 MB or smaller.");

        var detectedImage = DetectImageType(fileBytes)
            ?? throw new InvalidOperationException("Only PNG, JPG, GIF, or WEBP images are supported.");

        Directory.CreateDirectory(_avatarDirectory);

        var fileName = $"user-{userId}-{Guid.NewGuid():N}{detectedImage.Extension}";
        var filePath = Path.Combine(_avatarDirectory, fileName);

        await File.WriteAllBytesAsync(filePath, fileBytes);

        return new StoredAvatarFile
        {
            AvatarUrl = $"{AvatarUrlPrefix}{fileName}",
        };
    }

    public Task DeleteAsync(string avatarUrl)
    {
        if (!TryResolveManagedPath(avatarUrl, out var filePath))
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

    public bool IsManagedAvatarUrl(string? avatarUrl) => TryResolveManagedPath(avatarUrl, out _);

    private bool TryResolveManagedPath(string? avatarUrl, out string filePath)
    {
        filePath = string.Empty;

        if (string.IsNullOrWhiteSpace(avatarUrl) ||
            !avatarUrl.StartsWith(AvatarUrlPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = Path.GetFileName(avatarUrl);
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        var resolvedPath = Path.GetFullPath(Path.Combine(_avatarDirectory, fileName));
        if (!resolvedPath.StartsWith(_avatarDirectoryFullPath, StringComparison.OrdinalIgnoreCase))
            return false;

        filePath = resolvedPath;
        return true;
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
}

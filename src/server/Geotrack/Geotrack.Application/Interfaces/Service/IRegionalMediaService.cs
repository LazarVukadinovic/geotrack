using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Application.Interfaces.Service;

public interface IRegionalMediaService
{
    Task<RegionalImageUploadResponseDto> UploadImagesAsync(
        int userId,
        int regionId,
        IReadOnlyCollection<RegionalImageUploadFileDto> files,
        CancellationToken cancellationToken = default);
}

public interface IRegionalEntityImageStorageService
{
    Task<StoredRegionalEntityImageFile> StoreImageAsync(
        int userId,
        byte[] fileBytes,
        string? originalFileName,
        string? contentType,
        CancellationToken cancellationToken = default);
}

public sealed class StoredRegionalEntityImageFile
{
    public string MediaUrl { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}

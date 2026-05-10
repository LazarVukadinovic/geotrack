namespace Geotrack.Application.Interfaces.Service;

public interface IContributionMediaStorageService
{
    Task<StoredContributionMediaFile> StoreAsync(int userId, byte[] fileBytes);

    Task<StoredContributionMediaFile> StoreOwnershipProofAsync(
        int userId,
        byte[] fileBytes,
        string? originalFileName,
        string? contentType);

    Task DeleteAsync(string mediaUrl);
    bool IsManagedContributionMediaUrl(string? mediaUrl);
}

public sealed class StoredContributionMediaFile
{
    public string MediaUrl { get; set; } = string.Empty;
}
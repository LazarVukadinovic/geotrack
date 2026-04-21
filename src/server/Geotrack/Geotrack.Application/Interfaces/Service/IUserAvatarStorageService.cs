namespace Geotrack.Application.Interfaces.Service;

public interface IUserAvatarStorageService
{
    Task<StoredAvatarFile> StoreAsync(int userId, byte[] fileBytes);
    Task DeleteAsync(string avatarUrl);
    bool IsManagedAvatarUrl(string? avatarUrl);
}

public sealed class StoredAvatarFile
{
    public string AvatarUrl { get; init; } = string.Empty;
}

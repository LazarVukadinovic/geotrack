using Geotrack.Application.DTOs.Users;

namespace Geotrack.Application.Interfaces.Service;

public interface IUserService
{
    Task<GetMeResponseDto> GetMeAsync(int userId);
    Task<UpdateMeResponseDto> UpdateMeAsync(int userId, UpdateMeRequestDto request);
    Task<UpdateAvatarResponseDto> UpdateAvatarAsync(int userId, byte[] fileBytes);
    Task<ChangePasswordResponseDto> ChangePasswordAsync(int userId, ChangePasswordRequestDto request);
    Task<GetNotificationSettingsResponseDto> GetNotificationSettingsAsync(int userId);
    Task<UpdateNotificationSettingsResponseDto> UpdateNotificationSettingsAsync(int userId, UpdateNotificationSettingsRequestDto request);

    Task<GetUserInterestsResponseDto> GetUserInterestsAsync(int userId);
    Task<UpdateUserInterestsResponseDto> UpdateUserInterestsAsync(int userId, UpdateUserInterestsRequestDto request);
    Task<GetSupportRequestsResponseDto> GetSupportRequestsAsync(int userId);
    Task<CreateSupportRequestResponseDto> CreateSupportRequestAsync(int userId, CreateSupportRequestRequestDto request);

    Task<GetCollectionsResponseDto> GetCollectionsAsync(int userId);
    Task<CreateCollectionResponseDto> CreateCollectionAsync(int userId, CreateCollectionRequestDto request);
    Task<GetCollectionDetailsResponseDto> GetCollectionDetailsAsync(int userId, int collectionId);
    Task<UpdateCollectionResponseDto> UpdateCollectionAsync(int userId, int collectionId, UpdateCollectionRequestDto request);
    Task DeleteCollectionAsync(int userId, int collectionId);

    Task<AddCollectionItemResponseDto> AddCollectionItemAsync(int userId, int collectionId, AddCollectionItemRequestDto request);
    Task DeleteCollectionItemAsync(int userId, int collectionId, int itemId);

    Task<UpdateUserRoleResponseDtocs> AssignRegionalManagerRoleAsync(int userId);
    Task<UpdateUserRoleResponseDtocs> RemoveRegionalManagerRoleAsync(int userId);
    Task<GetUserRegionsResponseDto> GetUserRegionsAsync(int userId);
    Task<UpdateUserRegionsResponseDto> UpdateUserRegionsAsync(int userId, UpdateUserRegionsRequestDto request);
    Task<AddUserRegionResponseDto> AddUserRegionAsync(int userId, int regionId);
    Task<DeleteUserRegionResponseDto> DeleteUserRegionAsync(int userId, int regionId);
}

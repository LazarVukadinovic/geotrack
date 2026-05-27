using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Application.Interfaces.Service;

public interface ITerritoryUserAdminService
{
    Task<TerritoryUserListResponseDto> GetUsersAsync(
        int currentUserId,
        int? territoryId,
        string? search,
        string? sort,
        int skip,
        int take);

    Task<TerritoryUserListItemDto> GetUserAsync(
        int currentUserId,
        int targetUserId,
        int? territoryId);

    Task<TerritoryUserListItemDto> UpdateUserAsync(
        int currentUserId,
        int targetUserId,
        TerritoryUserUpdateRequestDto request);

    Task<TerritoryUserAvailableRegionsResponseDto> GetAvailableRegionsAsync(
        int currentUserId,
        int targetUserId,
        int? territoryId,
        string? search,
        int take);

    Task<TerritoryAssignableUserSearchResponseDto> SearchAssignableUsersAsync(
        int currentUserId,
        int? territoryId,
        string? query,
        int take,
        bool excludeAlreadyRegionalManager,
        bool excludeAlreadyTerritoryAssociate);

    Task<TerritoryUserListItemDto> AddRegionAsync(
        int currentUserId,
        int targetUserId,
        TerritoryUserAssignRegionRequestDto request);

    Task<TerritoryUserListItemDto> RemoveRegionAsync(
        int currentUserId,
        int targetUserId,
        int regionId,
        int? territoryId);

    Task<TerritoryUserListItemDto> AddRegionalManagerAsync(
        int currentUserId,
        int targetUserId,
        TerritoryUserAddRegionalManagerRequestDto request);

    Task<TerritoryUserListItemDto> AddTerritoryAssociateAsync(
        int currentUserId,
        int targetUserId,
        TerritoryUserAddTerritoryAssociateRequestDto request);

    Task<TerritoryUserListItemDto> RemoveManagerRoleAsync(
        int currentUserId,
        int targetUserId,
        TerritoryUserRemoveManagerRoleRequestDto request);

    Task<TerritoryUserListItemDto> RemoveTerritoryAssociateAsync(
        int currentUserId,
        int targetUserId,
        TerritoryUserRemoveTerritoryAssociateRequestDto request);
}

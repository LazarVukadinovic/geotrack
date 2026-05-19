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

    Task<TerritoryUserListItemDto> AddRegionAsync(
        int currentUserId,
        int targetUserId,
        TerritoryUserAssignRegionRequestDto request);

    Task<TerritoryUserListItemDto> RemoveManagerRoleAsync(
        int currentUserId,
        int targetUserId,
        TerritoryUserRemoveManagerRoleRequestDto request);
}

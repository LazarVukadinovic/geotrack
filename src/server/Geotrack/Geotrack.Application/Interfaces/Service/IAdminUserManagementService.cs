using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Application.Interfaces.Service;

public interface IAdminUserManagementService
{
    Task<AdminUserListResponseDto> GetUsersAsync(
        int currentUserId,
        AdminUserListRequestDto request,
        CancellationToken cancellationToken = default);

    Task<AdminUserListItemDto> GetUserAsync(
        int currentUserId,
        int targetUserId,
        CancellationToken cancellationToken = default);

    Task<AdminUserPreviewDto> GetUserPreviewAsync(
        int currentUserId,
        int targetUserId,
        CancellationToken cancellationToken = default);

    Task<AdminUserListItemDto> SuspendUserAsync(
        int currentUserId,
        int targetUserId,
        CancellationToken cancellationToken = default);

    Task<AdminUserListItemDto> UnsuspendUserAsync(
        int currentUserId,
        int targetUserId,
        CancellationToken cancellationToken = default);

    Task DeleteUserAsync(
        int currentUserId,
        int targetUserId,
        CancellationToken cancellationToken = default);

    Task<AdminUserListItemDto> AssignTerritoryManagerAsync(
        int currentUserId,
        int targetUserId,
        AdminUserAssignTerritoryManagerRequestDto request,
        CancellationToken cancellationToken = default);
}

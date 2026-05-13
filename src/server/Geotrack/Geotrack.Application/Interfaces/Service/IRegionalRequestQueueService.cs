using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Application.Interfaces.Service;

public interface IRegionalRequestQueueService
{
    Task<RegionalRequestQueueResponseDto> GetQueueAsync(
        int userId,
        int regionId,
        string? type,
        string? status,
        string? search,
        string? sort,
        int skip,
        int take);

    Task<RegionalRequestQueueDetailDto> GetRequestAsync(
        int userId,
        int regionId,
        string source,
        int requestId);

    Task<RegionalRequestActionResultDto> ApproveAsync(
        int userId,
        int regionId,
        string source,
        int requestId,
        string? note);

    Task<RegionalRequestActionResultDto> RejectAsync(
        int userId,
        int regionId,
        string source,
        int requestId,
        string? reason,
        string? note);

    Task<RegionalRequestBulkActionResponseDto> BulkApproveAsync(
        int userId,
        RegionalRequestBulkActionRequestDto request);

    Task<RegionalRequestBulkActionResponseDto> BulkRejectAsync(
        int userId,
        RegionalRequestBulkActionRequestDto request);
}

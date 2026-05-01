using Geotrack.Application.DTOs.Moderation;

namespace Geotrack.Application.Interfaces.Service;

public interface IModerationTermService
{
    Task<GetModerationTermsResponseDto> GetTermsAsync(
        string? languageCode,
        string? category,
        bool includeInactive);

    Task<ModerationTermDto> CreateTermAsync(int adminUserId, CreateModerationTermRequestDto request);
    Task<ModerationTermDto> UpdateTermAsync(int adminUserId, int termId, UpdateModerationTermRequestDto request);
    Task<ModerationTermDto> SetActiveAsync(int adminUserId, int termId, bool isActive);
    Task DeleteTermAsync(int termId);
}

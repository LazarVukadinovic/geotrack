using Geotrack.Application.DTOs.Moderation;

namespace Geotrack.Application.Interfaces.Service;

public interface IModerationTermCache
{
    Task<IReadOnlyList<ModerationTermRule>> GetActiveRulesAsync();
    void Invalidate();
}

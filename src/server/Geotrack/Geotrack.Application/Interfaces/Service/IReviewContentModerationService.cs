namespace Geotrack.Application.Interfaces.Service;

public interface IReviewContentModerationService
{
    Task EnsureAllowedReviewTextAsync(string? text);
}

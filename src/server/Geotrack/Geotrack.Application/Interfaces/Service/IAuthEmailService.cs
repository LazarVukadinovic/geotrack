using Geotrack.Domain.Entities.Users;

namespace Geotrack.Application.Interfaces.Service;

public interface IAuthEmailService
{
    Task SendEmailVerificationAsync(User user, string token, CancellationToken cancellationToken = default);
    Task SendPasswordResetAsync(User user, string token, CancellationToken cancellationToken = default);
}

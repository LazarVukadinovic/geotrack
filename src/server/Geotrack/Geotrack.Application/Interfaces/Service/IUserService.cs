using Geotrack.Application.DTOs.Users;

namespace Geotrack.Application.Interfaces.Service;

public interface IUserService
{
    Task<UserProfileDto?> GetProfileAsync(int userId);
    Task UpdateProfileAsync(int userId, UpdateUserProfileDto dto);
}

using Geotrack.Application.DTOs.Admin;

namespace Geotrack.Application.Interfaces.Service;

public interface IAdminProfileService
{
    Task<AdminProfileResponseDto> GetProfileAsync(int userId);
    Task<AdminProfileResponseDto> UpdateProfileAsync(int userId, UpdateAdminProfileRequestDto request);
}

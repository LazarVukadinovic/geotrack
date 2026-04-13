using Geotrack.Application.DTOs.Users;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public class UserService : IUserService
{
    private readonly AppDbContext _db;

    public UserService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<UserProfileDto?> GetProfileAsync(int userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return null;

        return new UserProfileDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            FullName = string.IsNullOrEmpty(user.FirstName) ? null : $"{user.FirstName} {user.LastName}".Trim(),
            DisplayName = user.DisplayName,
            AvatarUrl = user.AvatarUrl,
            Location = user.Location,
            PhoneNumber = user.PhoneNumber,
            IsVerified = true
        };
    }

    public async Task UpdateProfileAsync(int userId, UpdateUserProfileDto dto)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null) throw new Exception("User not found");

        if (dto.FullName != null)
        {
            var parts = dto.FullName.Split(' ', 2);
            user.FirstName = parts[0];
            user.LastName = parts.Length > 1 ? parts[1] : string.Empty;
        }

        if (dto.DisplayName != null) user.DisplayName = dto.DisplayName;
        if (dto.Location != null) user.Location = dto.Location;
        if (dto.PhoneNumber != null) user.PhoneNumber = dto.PhoneNumber;
        if (dto.AvatarUrl != null) user.AvatarUrl = dto.AvatarUrl;
        user.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
    }
}

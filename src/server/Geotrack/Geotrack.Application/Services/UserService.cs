using Geotrack.Application.Interfaces;
using Geotrack.Domain.Entities;

namespace Geotrack.Application.Services;

public class UserService
{
    private readonly IUserRepository _userRepository;

    public UserService(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<List<User>> GetAllAsync()
    {
        return await _userRepository.GetAllAsync();
    }

    public async Task<User?> GetByIdAsync(int id)
    {
        return await _userRepository.GetByIdAsync(id);
    }

    public async Task<User> AddAsync(string username, string email, List<int>? roleIds = null)
    {
        var user = new User
        {
            Username = username,
            Email = email,
            UserRoles = new List<UserRole>()
        };

        if (roleIds is not null)
        {
            foreach (var roleId in roleIds.Distinct())
            {
                user.UserRoles.Add(new UserRole
                {
                    User = user,
                    RoleId = roleId
                });
            }
        }

        await _userRepository.AddAsync(user);
        await _userRepository.SaveChangesAsync();

        return user;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var user = await _userRepository.GetByIdAsync(id);

        if (user is null)
            return false;

        _userRepository.Delete(user);
        await _userRepository.SaveChangesAsync();

        return true;
    }
}
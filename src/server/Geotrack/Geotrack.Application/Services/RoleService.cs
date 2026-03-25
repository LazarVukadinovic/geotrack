using Geotrack.Application.Interfaces;
using Geotrack.Domain.Entities;

namespace Geotrack.Application.Services;

public class RoleService
{
    private readonly IRoleRepository _roleRepository;

    public RoleService(IRoleRepository roleRepository)
    {
        _roleRepository = roleRepository;
    }

    public async Task<List<Role>> GetAllAsync()
    {
        return await _roleRepository.GetAllAsync();
    }

    public async Task<Role> CreateAsync(string name)
    {
        var role = new Role
        {
            Name = name
        };

        await _roleRepository.AddAsync(role);
        await _roleRepository.SaveChangesAsync();

        return role;
    }
}
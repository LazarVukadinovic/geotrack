using Geotrack.Application.Interfaces;
using Geotrack.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Repositories;

public class RoleRepository : IRoleRepository
{
    private readonly AppDbContext _dbContext;

    public RoleRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<Role>> GetAllAsync()
    {
        return await _dbContext.Roles.ToListAsync();
    }

    public async Task<Role?> GetByIdAsync(int id)
    {
        return await _dbContext.Roles.FindAsync(id);
    }

    public async Task AddAsync(Role role)
    {
        await _dbContext.Roles.AddAsync(role);
    }

    public void Delete(Role role)
    {
        _dbContext.Roles.Remove(role);
    }

    public async Task SaveChangesAsync()
    {
        await _dbContext.SaveChangesAsync();
    }
}
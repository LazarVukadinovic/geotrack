using Geotrack.Application.Interfaces;
using Geotrack.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Repositories;

public class DestinationRepository : IDestinationRepository
{
    private readonly AppDbContext _dbContext;

    public DestinationRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<Destination>> GetAllAsync()
    {
        return await _dbContext.Destinations
            .Include(d => d.Locations)
            .ToListAsync();
    }

    public async Task<Destination?> GetByIdAsync(int id)
    {
        return await _dbContext.Destinations
            .Include(d => d.Locations)
            .FirstOrDefaultAsync(d => d.Id == id);
    }

    public async Task AddAsync(Destination destination)
    {
        await _dbContext.Destinations.AddAsync(destination);
    }

    public void Delete(Destination destination)
    {
        _dbContext.Destinations.Remove(destination);
    }

    public async Task SaveChangesAsync()
    {
        await _dbContext.SaveChangesAsync();
    }
}
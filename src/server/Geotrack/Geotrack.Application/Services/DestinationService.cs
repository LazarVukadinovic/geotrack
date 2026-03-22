using Geotrack.Application.Interfaces;
using Geotrack.Domain.Entities;

namespace Geotrack.Application.Services;

public class DestinationService
{
    private readonly IDestinationRepository _destinationRepository;

    public DestinationService(IDestinationRepository destinationRepository)
    {
        _destinationRepository = destinationRepository;
    }

    public async Task<List<Destination>> GetAllAsync()
    {
        return await _destinationRepository.GetAllAsync();
    }

    public async Task<Destination?> GetByIdAsync(int id)
    {
        return await _destinationRepository.GetByIdAsync(id);
    }

    public async Task<Destination> CreateAsync(string name)
    {
        var destination = new Destination
        {
            Name = name
        };

        await _destinationRepository.AddAsync(destination);
        await _destinationRepository.SaveChangesAsync();

        return destination;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var destination = await _destinationRepository.GetByIdAsync(id);
        if (destination is null)
        {
            return false;
        }

        _destinationRepository.Delete(destination);
        await _destinationRepository.SaveChangesAsync();

        return true;
    }
}
using Geotrack.Application.Interfaces;
using Geotrack.Application.Services;
using Geotrack.Server.Requests;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.API.Controllers;

[ApiController]
[Route("api/destinations")]
public class DestinationsController : ControllerBase
{
    private readonly DestinationService _destinationService;

    public DestinationsController(DestinationService destinationService)
    {
        _destinationService = destinationService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var destinations = await _destinationService.GetAllAsync();
        return Ok(destinations);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var destination = await _destinationService.GetByIdAsync(id);

        if (destination is null)
        {
            return NotFound();
        }

        return Ok(destination);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateDestinationRequest request)
    {
        var destination = await _destinationService.CreateAsync(request.Name);

        return CreatedAtAction(
            nameof(GetById),
            new { id = destination.Id },
            destination);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _destinationService.DeleteAsync(id);

        if (!deleted)
        {
            return NotFound();
        }

        return NoContent();
    }
}
using Geotrack.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.API.Controllers;

[ApiController]
[Route("api/roles")]
public class RoleController : ControllerBase
{
    private readonly RoleService _roleService;

    public RoleController(RoleService roleService)
    {
        _roleService = roleService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var roles = await _roleService.GetAllAsync();
        return Ok(roles);
    }

    [HttpPost]
    public async Task<IActionResult> Create(string name)
    {
        var role = await _roleService.CreateAsync(name);
        return Ok(role);
    }
}
using Geotrack.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.API.Controllers;

[ApiController]
[Route("api/roles")]
public class RolesController : ControllerBase
{
    private readonly RoleService _roleService;

    public RolesController(RoleService roleService)
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
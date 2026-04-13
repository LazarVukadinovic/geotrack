using Geotrack.Application.DTOs.Tags;
using Geotrack.Application.DTOs.Users;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/user")]
[Authorize]
public class UserController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ITagService _tagService;

    public UserController(IUserService userService, ITagService tagService)
    {
        _userService = userService;
        _tagService = tagService;
    }

    [HttpGet("me")]
    public async Task<ActionResult> GetProfile()
    {
        var userId = GetRequiredUserId();
        if (userId == null) return Unauthorized();

        var profile = await _userService.GetProfileAsync(userId.Value);
        if (profile == null) return NotFound();
        return Ok(profile);
    }

    [HttpPut("me")]
    public async Task<ActionResult> UpdateProfile([FromBody] UpdateUserProfileDto dto)
    {
        var userId = GetRequiredUserId();
        if (userId == null) return Unauthorized();

        try
        {
            await _userService.UpdateProfileAsync(userId.Value, dto);
            return Ok(new { message = "Profile updated successfully" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("tags")]
    public async Task<ActionResult> SaveTags([FromBody] SaveUserTagsDto dto)
    {
        var userId = GetRequiredUserId();
        if (userId == null) return Unauthorized();

        await _tagService.SaveUserTagsAsync(userId.Value, dto.TagIds);
        return Ok(new { message = "Tags saved successfully" });
    }

    private int? GetRequiredUserId()
    {
        var claim = User.FindFirst("userId")?.Value;
        return int.TryParse(claim, out var id) ? id : null;
    }
}

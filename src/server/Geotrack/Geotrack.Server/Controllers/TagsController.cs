using Geotrack.Application.DTOs.Tags;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/tags")]
public class TagsController : ControllerBase
{
    private readonly ITagService _tagService;

    public TagsController(ITagService tagService)
    {
        _tagService = tagService;
    }

    [HttpGet]
    public async Task<ActionResult> GetTags([FromQuery] bool? forUser = null)
    {
        var tags = await _tagService.GetTagsAsync(forUser);
        return Ok(tags);
    }
}

/// <summary>
/// Handles POST /api/user/tags — the endpoint the frontend uses to save user interests.
/// Separated from UserController to avoid requiring [Authorize] on the entire class
/// since newly registered users call this immediately after getting their token.
/// </summary>
[ApiController]
[Route("api/user")]
[Authorize]
public class UserTagsController : ControllerBase
{
    private readonly ITagService _tagService;

    public UserTagsController(ITagService tagService)
    {
        _tagService = tagService;
    }

    [HttpPost("tags")]
    public async Task<ActionResult> SaveUserTags([FromBody] SaveUserTagsDto dto)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        await _tagService.SaveUserTagsAsync(userId.Value, dto.TagIds);
        return Ok();
    }

    private int? GetUserId()
    {
        var claim = User.FindFirst("userId")?.Value;
        return int.TryParse(claim, out var id) ? id : null;
    }
}

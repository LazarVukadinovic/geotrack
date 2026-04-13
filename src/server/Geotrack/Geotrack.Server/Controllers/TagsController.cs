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

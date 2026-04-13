using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.DTOs.User;
using Geotrack.Application.DTOs.Collection;
using Geotrack.Application.DTOs.Interest;
using System.Security.Claims;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    private int GetUserId()
    {
        return int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetMe()
    {
        return Ok(await _userService.GetMeAsync(GetUserId()));
    }

    [Authorize]
    [HttpPut("me")]
    public async Task<IActionResult> UpdateMe(UpdateMeRequestDto request)
    {
        return Ok(await _userService.UpdateMeAsync(GetUserId(), request));
    }

    [Authorize]
    [HttpGet("me/interests")]
    public async Task<IActionResult> GetInterests()
    {
        return Ok(await _userService.GetUserInterestsAsync(GetUserId()));
    }

    [Authorize]
    [HttpPut("me/interests")]
    public async Task<IActionResult> UpdateInterests(UpdateUserInterestsRequestDto request)
    {
        return Ok(await _userService.UpdateUserInterestsAsync(GetUserId(), request));
    }

    [Authorize]
    [HttpGet("me/collections")]
    public async Task<IActionResult> GetCollections()
    {
        return Ok(await _userService.GetCollectionsAsync(GetUserId()));
    }

    [Authorize]
    [HttpPost("me/collections")]
    public async Task<IActionResult> CreateCollection(CreateCollectionRequestDto request)
    {
        return Ok(await _userService.CreateCollectionAsync(GetUserId(), request));
    }


    [Authorize]
    [HttpGet("me/collections/{id}")]
    public async Task<IActionResult> GetCollectionDetails(int id)
    {
        return Ok(await _userService.GetCollectionDetailsAsync(GetUserId(), id));
    }

    [Authorize]
    [HttpPut("me/collections/{id}")]
    public async Task<IActionResult> UpdateCollection(int id, UpdateCollectionRequestDto request)
    {
        return Ok(await _userService.UpdateCollectionAsync(GetUserId(), id, request));
    }

    [Authorize]
    [HttpDelete("me/collections/{id}")]
    public async Task<IActionResult> DeleteCollection(int id)
    {
        await _userService.DeleteCollectionAsync(GetUserId(), id);
        return NoContent();
    }

    [Authorize]
    [HttpPost("me/collections/{id}/items")]
    public async Task<IActionResult> AddItem(int id, AddCollectionItemRequestDto request)
    {
        return Ok(await _userService.AddCollectionItemAsync(GetUserId(), id, request));
    }

    [Authorize]
    [HttpDelete("me/collections/{id}/items/{itemId}")]
    public async Task<IActionResult> DeleteItem(int id, int itemId)
    {
        await _userService.DeleteCollectionItemAsync(GetUserId(), id, itemId);
        return NoContent();
    }
}
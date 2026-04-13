using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Server.Requests;
using Geotrack.Server.Responses;
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
        var result = await _userService.GetMeAsync(GetUserId());

        return Ok(new GetMeResponse
        {
            Id = result.Id,
            Username = result.Username,
            Email = result.Email,
            FirstName = result.FirstName,
            LastName = result.LastName,
            PhoneNumber = result.PhoneNumber,
            CreatedAt = result.CreatedAt,
            UpdatedAt = result.UpdatedAt,
            Collections = result.Collections.Select(c => new CollectionPreviewResponse
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt,
                ItemsCount = c.ItemsCount
            }).ToList()
        });
    }

    [Authorize]
    [HttpPut("me")]
    public async Task<IActionResult> UpdateMe(UpdateMeRequest request)
    {
        var dto = new Geotrack.Application.DTOs.User.UpdateMeRequestDto
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            PhoneNumber = request.PhoneNumber
        };

        var result = await _userService.UpdateMeAsync(GetUserId(), dto);

        return Ok(new UpdateMeResponse
        {
            Id = result.Id,
            Username = result.Username,
            Email = result.Email,
            FirstName = result.FirstName,
            LastName = result.LastName,
            PhoneNumber = result.PhoneNumber,
            CreatedAt = result.CreatedAt,
            UpdatedAt = result.UpdatedAt
        });
    }

    [Authorize]
    [HttpGet("me/interests")]
    public async Task<IActionResult> GetInterests()
    {
        var result = await _userService.GetUserInterestsAsync(GetUserId());

        return Ok(new GetUserInterestsResponse
        {
            Tags = result.Tags.Select(t => new InterestItemResponse
            {
                TagId = t.TagId,
                Name = t.Name,
                Slug = t.Slug,
                Description = t.Description,
                ForDestination = t.ForDestination,
                ForLocation = t.ForLocation,
                ForPlace = t.ForPlace,
                ForEvent = t.ForEvent,
                PlaceTypeId = t.PlaceTypeId,
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt
            }).ToList()
        });
    }

    [Authorize]
    [HttpPut("me/interests")]
    public async Task<IActionResult> UpdateInterests(UpdateUserInterestsRequest request)
    {
        var dto = new Geotrack.Application.DTOs.Interest.UpdateUserInterestsRequestDto
        {
            TagIds = request.TagIds
        };

        var result = await _userService.UpdateUserInterestsAsync(GetUserId(), dto);

        return Ok(new UpdateUserInterestsResponse
        {
            Tags = result.Tags.Select(t => new InterestItemResponse
            {
                TagId = t.TagId,
                Name = t.Name,
                Slug = t.Slug,
                Description = t.Description,
                ForDestination = t.ForDestination,
                ForLocation = t.ForLocation,
                ForPlace = t.ForPlace,
                ForEvent = t.ForEvent,
                PlaceTypeId = t.PlaceTypeId,
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt
            }).ToList()
        });
    }

    [Authorize]
    [HttpGet("me/collections")]
    public async Task<IActionResult> GetCollections()
    {
        var result = await _userService.GetCollectionsAsync(GetUserId());

        return Ok(new GetCollectionsResponse
        {
            Collections = result.Collections.Select(c => new CollectionListItemResponse
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                UserId = c.UserId,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt,
                ItemsCount = c.ItemsCount
            }).ToList()
        });
    }

    [Authorize]
    [HttpPost("me/collections")]
    public async Task<IActionResult> CreateCollection(CreateCollectionRequest request)
    {
        var dto = new Geotrack.Application.DTOs.Collection.CreateCollectionRequestDto
        {
            Name = request.Name,
            Description = request.Description
        };

        var result = await _userService.CreateCollectionAsync(GetUserId(), dto);

        return Ok(new CreateCollectionResponse
        {
            Id = result.Id,
            Name = result.Name,
            Description = result.Description,
            UserId = result.UserId,
            CreatedAt = result.CreatedAt,
            UpdatedAt = result.UpdatedAt
        });
    }
}
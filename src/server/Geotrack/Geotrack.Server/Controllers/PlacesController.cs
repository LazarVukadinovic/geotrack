using System.Text.Json;
using Geotrack.Application.DTOs.Destinations;
using Geotrack.Application.DTOs.Places;
using Geotrack.Application.DTOs.Reviews;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/places")]
public class PlacesController : ControllerBase
{
    private readonly IPlaceService _placeService;
    private readonly IContributionMediaStorageService _mediaStorage;
    private readonly IReviewService _reviewService;

    public PlacesController(
        IPlaceService placeService,
        IContributionMediaStorageService mediaStorage,
        IReviewService reviewService)
    {
        _placeService = placeService;
        _mediaStorage = mediaStorage;
        _reviewService = reviewService;
    }

    [HttpGet]
    public async Task<ActionResult> GetPlaces(
        [FromQuery] int? destinationId = null,
        [FromQuery] string? type = null,
        [FromQuery] string? tags = null,
        [FromQuery] string? category = null,
        [FromQuery] double? latitude = null,
        [FromQuery] double? longitude = null,
        [FromQuery] int skip = 0,
        [FromQuery] int? take = null)
    {
        var pagingError = ValidatePaging(skip, take);
        if (pagingError is not null)
        {
            return pagingError;
        }

        var coordinateError = ValidateOptionalCoordinates(latitude, longitude);
        if (coordinateError is not null)
        {
            return coordinateError;
        }

        var userId = GetUserId();
        var result = await _placeService.GetPlacesAsync(destinationId, type, tags, category, userId, latitude, longitude, skip, take);
        var wantsPagedResponse = Request.Query.ContainsKey("skip") || Request.Query.ContainsKey("take");
        return result.Status switch
        {
            DestinationScopedStatus.Success => Ok(wantsPagedResponse ? result.Data! : result.Data!.Items),
            DestinationScopedStatus.InvalidDestination => BadRequest(new { error = "destinationId is required and must be positive." }),
            DestinationScopedStatus.DestinationNotFound => NotFound(new { error = "Destination not found." }),
            _ => StatusCode(500)
        };
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult> GetPlaceDetail(
        int id,
        [FromQuery] double? latitude = null,
        [FromQuery] double? longitude = null,
        [FromQuery] string? distanceOrigin = null)
    {
        var userId = GetUserId();
        var detail = await _placeService.GetPlaceDetailAsync(id, userId, latitude, longitude, UseCurrentLocationDistance(distanceOrigin));
        if (detail == null) return NotFound();
        return Ok(detail);
    }

    [HttpGet("hotels")]
    public async Task<ActionResult> GetHotels(
        [FromQuery] int? destinationId = null,
        [FromQuery] string? tag = null,
        [FromQuery] double? latitude = null,
        [FromQuery] double? longitude = null,
        [FromQuery] int skip = 0,
        [FromQuery] int? take = null)
    {
        var pagingError = ValidatePaging(skip, take);
        if (pagingError is not null)
        {
            return pagingError;
        }

        var coordinateError = ValidateOptionalCoordinates(latitude, longitude);
        if (coordinateError is not null)
        {
            return coordinateError;
        }

        var userId = GetUserId();
        var result = await _placeService.GetHotelsAsync(destinationId, tag, userId, latitude, longitude, skip, take);
        var wantsPagedResponse = Request.Query.ContainsKey("skip") || Request.Query.ContainsKey("take");
        return result.Status switch
        {
            DestinationScopedStatus.Success => Ok(wantsPagedResponse ? result.Data! : result.Data!.Items),
            DestinationScopedStatus.InvalidDestination => BadRequest(new { error = "destinationId is required and must be positive." }),
            DestinationScopedStatus.DestinationNotFound => NotFound(new { error = "Destination not found." }),
            _ => StatusCode(500)
        };
    }

    [HttpGet("hotels/{id:int}")]
    public async Task<ActionResult> GetHotelDetail(
        int id,
        [FromQuery] double? latitude = null,
        [FromQuery] double? longitude = null,
        [FromQuery] string? distanceOrigin = null)
    {
        var userId = GetUserId();
        var detail = await _placeService.GetHotelDetailAsync(id, userId, latitude, longitude, UseCurrentLocationDistance(distanceOrigin));
        if (detail == null) return NotFound();
        return Ok(detail);
    }

    [HttpGet("{id:int}/events")]
    public async Task<IActionResult> GetPlaceEvents([FromRoute] int id)
    {
        try
        {
            var result = await _placeService.GetPlaceEventsAsync(id);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpGet("{id:int}/reviews")]
    public async Task<IActionResult> GetPlaceReviews(
        [FromRoute] int id,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 10)
    {
        var pagingError = ValidatePaging(skip, take);
        if (pagingError is not null)
        {
            return pagingError;
        }

        try
        {
            var result = await _reviewService.GetPlaceReviewsAsync(id, skip, take, GetUserId());
            return Ok(MapLegacyPlaceReviewsResponse(id, result));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    [HttpPut("{id:int}/owner-update")]
    [Authorize]
    [EnableRateLimiting("contributions")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> OwnerUpdatePlace(
        [FromRoute] int id,
        [FromForm] string payload,
        IFormFile? cardImage,
        IFormFile? coverImage,
        List<IFormFile>? galleryImages)
    {
        var userId = GetUserId();
        if (!userId.HasValue)
            return Unauthorized();

        OwnerUpdatePlaceRequestDto? request;
        try
        {
            request = JsonSerializer.Deserialize<OwnerUpdatePlaceRequestDto>(
                payload,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return BadRequest(new { message = "Invalid payload." });
        }

        if (request is null)
            return BadRequest(new { message = "Payload is required." });

        try
        {
            request.Images ??= new List<OwnerUpdatePlaceImageDto>();

            if (coverImage is { Length: > 0 })
            {
                var bytes = await ReadFileBytesAsync(coverImage);
                if (bytes is { Length: > 0 })
                {
                    var url = (await _mediaStorage.StoreAsync(userId.Value, bytes)).MediaUrl;
                    request.Images.RemoveAll(img => EntityImageRole.Normalize(img.ImageRole) == EntityImageRole.Cover);
                    request.Images.Add(new OwnerUpdatePlaceImageDto
                    {
                        Url = url,
                        ImageRole = EntityImageRole.Cover,
                        SortOrder = -10,
                        ImagePosition = request.ImagePosition,
                        ImageSize = request.ImageSize,
                    });
                }
            }

            if (cardImage is { Length: > 0 })
            {
                var bytes = await ReadFileBytesAsync(cardImage);
                if (bytes is { Length: > 0 })
                {
                    var url = (await _mediaStorage.StoreAsync(userId.Value, bytes)).MediaUrl;
                    request.Images.RemoveAll(img => EntityImageRole.Normalize(img.ImageRole) == EntityImageRole.Card);
                    request.Images.Add(new OwnerUpdatePlaceImageDto
                    {
                        Url = url,
                        ImageRole = EntityImageRole.Card,
                        SortOrder = -9,
                        ImagePosition = request.CardImagePosition,
                        ImageSize = request.CardImageSize,
                    });
                }
            }

            if (galleryImages is { Count: > 0 })
            {
                var nextSort = request.Images
                    .Where(img => EntityImageRole.Normalize(img.ImageRole) == EntityImageRole.Gallery)
                    .Select(img => img.SortOrder)
                    .DefaultIfEmpty(-1)
                    .Max() + 1;

                foreach (var file in galleryImages)
                {
                    var bytes = await ReadFileBytesAsync(file);
                    if (bytes is not { Length: > 0 }) continue;
                    var url = (await _mediaStorage.StoreAsync(userId.Value, bytes)).MediaUrl;
                    request.Images.Add(new OwnerUpdatePlaceImageDto
                    {
                        Url = url,
                        ImageRole = EntityImageRole.Gallery,
                        SortOrder = nextSort++,
                    });
                }
            }

            var result = await _placeService.OwnerUpdatePlaceAsync(id, userId.Value, request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return ex is UnauthorizedAccessException
                ? StatusCode(403, new { message = "Forbidden." })
                : MapException(ex);
        }
    }

    private static async Task<byte[]?> ReadFileBytesAsync(IFormFile? file)
    {
        if (file is null || file.Length == 0) return null;
        await using var stream = file.OpenReadStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }

    [HttpPost("{id:int}/reviews")]
    [Authorize]
    [EnableRateLimiting("reviews")]
    public async Task<IActionResult> CreatePlaceReview([FromRoute] int id, [FromBody] CreatePlaceReviewRequestDto request)
    {
        try
        {
            var userId = GetUserId();
            if (!userId.HasValue)
                return Unauthorized();

            var result = await _reviewService.CreatePlaceReviewAsync(
                id,
                userId.Value,
                new CreateReviewDto
                {
                    Rating = (int)Math.Round(request.Rating, MidpointRounding.AwayFromZero),
                    Comment = request.Comment
                });

            return CreatedAtAction(nameof(GetPlaceReviews), new { id }, MapLegacyPlaceReviewResponse(id, result));
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
    }

    private int? GetUserId()
    {
        var claim = User.FindFirst("userId")?.Value;
        return int.TryParse(claim, out var id) ? id : null;
    }

    private ActionResult? ValidatePaging(int skip, int? take)
    {
        if (skip < 0)
        {
            return BadRequest(new { error = "skip must be zero or greater." });
        }

        if (take.HasValue && take.Value <= 0)
        {
            return BadRequest(new { error = "take must be greater than zero." });
        }

        return null;
    }

    private static GetPlaceReviewsResponseDto MapLegacyPlaceReviewsResponse(
        int placeId,
        PagedReviewsResponseDto response) => new()
    {
        PlaceId = placeId,
        AverageRating = response.AverageRating ?? 0,
        ReviewsCount = response.TotalCount,
        TotalCount = response.TotalCount,
        Skip = response.Skip,
        Take = response.Take,
        NextSkip = response.NextSkip,
        HasMore = response.HasMore,
        Reviews = response.Items.Select(review => new PlaceReviewItemDto
        {
            Id = review.Id,
            UserId = review.AuthorUserId,
            Rating = review.Rating,
            Comment = review.Body,
            CreatedAt = review.CreatedAt,
            UpdatedAt = review.UpdatedAt
        }).ToList()
    };

    private static CreatePlaceReviewResponseDto MapLegacyPlaceReviewResponse(
        int placeId,
        ReviewDto review) => new()
    {
        Id = review.Id,
        UserId = review.AuthorUserId,
        PlaceId = placeId,
        Rating = review.Rating,
        Comment = review.Body,
        CreatedAt = review.CreatedAt,
        UpdatedAt = review.UpdatedAt
    };

    private ActionResult? ValidateOptionalCoordinates(double? latitude, double? longitude)
    {
        if (!latitude.HasValue && !longitude.HasValue)
        {
            return null;
        }

        if (latitude.HasValue != longitude.HasValue)
        {
            return BadRequest(new { error = "latitude and longitude must be provided together." });
        }

        if (!HasValidCoordinates(latitude!.Value, longitude!.Value))
        {
            return BadRequest(new { error = "latitude and longitude must be finite values in valid coordinate ranges." });
        }

        return null;
    }

    private static bool UseCurrentLocationDistance(string? distanceOrigin) =>
        string.Equals(distanceOrigin, "current-location", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(distanceOrigin, "user", StringComparison.OrdinalIgnoreCase);

    private static bool HasValidCoordinates(double latitude, double longitude)
    {
        return !double.IsNaN(latitude) &&
               !double.IsNaN(longitude) &&
               !double.IsInfinity(latitude) &&
               !double.IsInfinity(longitude) &&
               latitude >= -90 &&
               latitude <= 90 &&
               longitude >= -180 &&
               longitude <= 180 &&
               (latitude != 0d || longitude != 0d);
    }

    private IActionResult MapException(Exception ex)
    {
        return ex switch
        {
            KeyNotFoundException => NotFound(new { message = ex.Message }),
            InvalidOperationException => BadRequest(new { message = ex.Message }),
            _ => StatusCode(500, new { message = "An unexpected error occurred." })
        };
    }
}

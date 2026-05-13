using System.Security.Claims;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Geotrack.Server.Controllers;

[ApiController]
[Route("api/admin/regional")]
[Authorize(Policy = "RequireRegionalManager")]
public class AdminRegionalController : ControllerBase
{
    private readonly IRegionalDashboardService _regionalDashboard;
    private readonly IRegionalRequestQueueService _requestQueue;
    private readonly IRegionalDestinationService _destinations;
    private readonly IRegionalLocationService _locations;
    private readonly IRegionalPlaceService _places;
    private readonly IRegionalEventService _events;

    public AdminRegionalController(
        IRegionalDashboardService regionalDashboard,
        IRegionalRequestQueueService requestQueue,
        IRegionalDestinationService destinations,
        IRegionalLocationService locations,
        IRegionalPlaceService places,
        IRegionalEventService events)
    {
        _regionalDashboard = regionalDashboard;
        _requestQueue = requestQueue;
        _destinations = destinations;
        _locations = locations;
        _places = places;
        _events = events;
    }

    [HttpGet("regions")]
    public async Task<ActionResult<List<RegionalAssignedRegionDto>>> GetAssignedRegions()
    {
        try
        {
            return Ok(await _regionalDashboard.GetAssignedRegionsAsync(GetCurrentUserId()));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("requests")]
    public async Task<ActionResult<RegionalRequestQueueResponseDto>> GetRequests(
        [FromQuery] int regionId,
        [FromQuery] string? type = null,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        [FromQuery] string? sort = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 10)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        try
        {
            return Ok(await _requestQueue.GetQueueAsync(
                GetCurrentUserId(),
                regionId,
                type,
                status,
                search,
                sort,
                skip,
                take));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("destinations")]
    public async Task<ActionResult<RegionalDestinationListResponseDto>> GetDestinations(
        [FromQuery] int regionId,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] string? sort = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 25)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        try
        {
            return Ok(await _destinations.GetDestinationsAsync(
                GetCurrentUserId(),
                regionId,
                search,
                status,
                sort,
                skip,
                take));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("destinations/{id:int}")]
    public async Task<ActionResult<RegionalDestinationDetailDto>> GetDestination(
        [FromRoute] int id,
        [FromQuery] int regionId)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        try
        {
            return Ok(await _destinations.GetDestinationAsync(GetCurrentUserId(), regionId, id));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPut("destinations/{id:int}")]
    public async Task<ActionResult<RegionalDestinationDetailDto>> UpdateDestination(
        [FromRoute] int id,
        [FromQuery] int regionId,
        [FromBody] RegionalDestinationUpdateRequestDto? request)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        if (request is null)
            return BadRequest(new { message = "Request body is required." });

        try
        {
            return Ok(await _destinations.UpdateDestinationAsync(GetCurrentUserId(), regionId, id, request));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpDelete("destinations/{id:int}")]
    public async Task<IActionResult> DeleteDestination(
        [FromRoute] int id,
        [FromQuery] int regionId)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        try
        {
            await _destinations.DeleteDestinationAsync(GetCurrentUserId(), regionId, id);
            return NoContent();
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("locations")]
    public async Task<ActionResult<RegionalLocationListResponseDto>> GetLocations(
        [FromQuery] int regionId,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] string? sort = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 25)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        try
        {
            return Ok(await _locations.GetLocationsAsync(
                GetCurrentUserId(),
                regionId,
                search,
                status,
                sort,
                skip,
                take));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("locations/{id:int}")]
    public async Task<ActionResult<RegionalLocationDetailDto>> GetLocation(
        [FromRoute] int id,
        [FromQuery] int regionId)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        try
        {
            return Ok(await _locations.GetLocationAsync(GetCurrentUserId(), regionId, id));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPut("locations/{id:int}")]
    public async Task<ActionResult<RegionalLocationDetailDto>> UpdateLocation(
        [FromRoute] int id,
        [FromQuery] int regionId,
        [FromBody] RegionalLocationUpdateRequestDto? request)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        if (request is null)
            return BadRequest(new { message = "Request body is required." });

        try
        {
            return Ok(await _locations.UpdateLocationAsync(GetCurrentUserId(), regionId, id, request));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpDelete("locations/{id:int}")]
    public async Task<IActionResult> DeleteLocation(
        [FromRoute] int id,
        [FromQuery] int regionId)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        try
        {
            await _locations.DeleteLocationAsync(GetCurrentUserId(), regionId, id);
            return NoContent();
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("places")]
    public async Task<ActionResult<RegionalPlaceListResponseDto>> GetPlaces(
        [FromQuery] int regionId,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] string? placeType = null,
        [FromQuery] string? sort = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 25)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        try
        {
            return Ok(await _places.GetPlacesAsync(
                GetCurrentUserId(),
                regionId,
                search,
                status,
                placeType,
                sort,
                skip,
                take));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("places/{id:int}")]
    public async Task<ActionResult<RegionalPlaceDetailDto>> GetPlace(
        [FromRoute] int id,
        [FromQuery] int regionId)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        try
        {
            return Ok(await _places.GetPlaceAsync(GetCurrentUserId(), regionId, id));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPut("places/{id:int}")]
    public async Task<ActionResult<RegionalPlaceDetailDto>> UpdatePlace(
        [FromRoute] int id,
        [FromQuery] int regionId,
        [FromBody] RegionalPlaceUpdateRequestDto? request)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        if (request is null)
            return BadRequest(new { message = "Request body is required." });

        try
        {
            return Ok(await _places.UpdatePlaceAsync(GetCurrentUserId(), regionId, id, request));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpDelete("places/{id:int}")]
    public async Task<IActionResult> DeletePlace(
        [FromRoute] int id,
        [FromQuery] int regionId)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        try
        {
            await _places.DeletePlaceAsync(GetCurrentUserId(), regionId, id);
            return NoContent();
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("events")]
    public async Task<ActionResult<RegionalEventListResponseDto>> GetEvents(
        [FromQuery] int regionId,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] string? eventType = null,
        [FromQuery] string? sort = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 25)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        try
        {
            return Ok(await _events.GetEventsAsync(
                GetCurrentUserId(),
                regionId,
                search,
                status,
                eventType,
                sort,
                skip,
                take));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("events/{id:int}")]
    public async Task<ActionResult<RegionalEventDetailDto>> GetEvent(
        [FromRoute] int id,
        [FromQuery] int regionId)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        try
        {
            return Ok(await _events.GetEventAsync(GetCurrentUserId(), regionId, id));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPut("events/{id:int}")]
    public async Task<ActionResult<RegionalEventDetailDto>> UpdateEvent(
        [FromRoute] int id,
        [FromQuery] int regionId,
        [FromBody] RegionalEventUpdateRequestDto? request)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        if (request is null)
            return BadRequest(new { message = "Request body is required." });

        try
        {
            return Ok(await _events.UpdateEventAsync(GetCurrentUserId(), regionId, id, request));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpDelete("events/{id:int}")]
    public async Task<IActionResult> DeleteEvent(
        [FromRoute] int id,
        [FromQuery] int regionId)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        try
        {
            await _events.DeleteEventAsync(GetCurrentUserId(), regionId, id);
            return NoContent();
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("requests/{source}/{id:int}")]
    public async Task<ActionResult<RegionalRequestQueueDetailDto>> GetRequest(
        [FromRoute] string source,
        [FromRoute] int id,
        [FromQuery] int regionId)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        try
        {
            return Ok(await _requestQueue.GetRequestAsync(GetCurrentUserId(), regionId, source, id));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("requests/{source}/{id:int}/approve")]
    public async Task<ActionResult<RegionalRequestActionResultDto>> ApproveRequest(
        [FromRoute] string source,
        [FromRoute] int id,
        [FromBody] RegionalRequestDecisionRequestDto? request)
    {
        if (request is null)
            return BadRequest(new { message = "Request body is required." });

        if (request.RegionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        try
        {
            return Ok(await _requestQueue.ApproveAsync(
                GetCurrentUserId(),
                request.RegionId,
                source,
                id,
                request.Note));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("requests/{source}/{id:int}/reject")]
    public async Task<ActionResult<RegionalRequestActionResultDto>> RejectRequest(
        [FromRoute] string source,
        [FromRoute] int id,
        [FromBody] RegionalRequestDecisionRequestDto? request)
    {
        if (request is null)
            return BadRequest(new { message = "Request body is required." });

        if (request.RegionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        try
        {
            return Ok(await _requestQueue.RejectAsync(
                GetCurrentUserId(),
                request.RegionId,
                source,
                id,
                request.Reason,
                request.Note));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("requests/bulk-approve")]
    public async Task<ActionResult<RegionalRequestBulkActionResponseDto>> BulkApprove(
        [FromBody] RegionalRequestBulkActionRequestDto? request)
    {
        if (request is null)
            return BadRequest(new { message = "Request body is required." });

        try
        {
            return Ok(await _requestQueue.BulkApproveAsync(GetCurrentUserId(), request));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("requests/bulk-reject")]
    public async Task<ActionResult<RegionalRequestBulkActionResponseDto>> BulkReject(
        [FromBody] RegionalRequestBulkActionRequestDto? request)
    {
        if (request is null)
            return BadRequest(new { message = "Request body is required." });

        try
        {
            return Ok(await _requestQueue.BulkRejectAsync(GetCurrentUserId(), request));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<RegionalDashboardDto>> GetDashboard([FromQuery] int regionId)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        try
        {
            return Ok(await _regionalDashboard.GetDashboardAsync(GetCurrentUserId(), regionId));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("activity")]
    public async Task<ActionResult<RegionalDashboardActivityResponseDto>> GetActivity(
        [FromQuery] int regionId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 20)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        try
        {
            return Ok(await _regionalDashboard.GetActivityAsync(GetCurrentUserId(), regionId, skip, take));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    private int GetCurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("userId");

        return int.TryParse(value, out var userId)
            ? userId
            : throw new UnauthorizedAccessException("Invalid user token.");
    }

    private ActionResult HandleException(Exception ex)
    {
        return ex switch
        {
            UnauthorizedAccessException => StatusCode(StatusCodes.Status403Forbidden, new { message = "Forbidden." }),
            KeyNotFoundException => NotFound(new { message = ex.Message }),
            InvalidOperationException when IsConflict(ex) => Conflict(new { message = ex.Message }),
            InvalidOperationException => BadRequest(new { message = ex.Message }),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new { message = "An unexpected error occurred." })
        };
    }

    private static bool IsConflict(Exception ex)
    {
        return ex.Message.Contains("Only pending", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("already been reviewed", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("related content", StringComparison.OrdinalIgnoreCase);
    }
}

using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Geotrack.Application.DTOs.Admin;
using Geotrack.Application.Interfaces.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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
    private readonly IRegionalMapService _map;
    private readonly IRegionalAnalyticsService _analytics;
    private readonly IRegionalMediaService _media;
    private readonly ILogger<AdminRegionalController> _logger;
    private readonly IContributionMediaStorageService _mediaStorage;

    public AdminRegionalController(
        IRegionalDashboardService regionalDashboard,
        IRegionalRequestQueueService requestQueue,
        IRegionalDestinationService destinations,
        IRegionalLocationService locations,
        IRegionalPlaceService places,
        IRegionalEventService events,
        IRegionalMapService map,
        IRegionalAnalyticsService analytics,
        IRegionalMediaService media,
        IContributionMediaStorageService mediaStorage,
        ILogger<AdminRegionalController> logger)
    {
        _regionalDashboard = regionalDashboard;
        _requestQueue = requestQueue;
        _destinations = destinations;
        _locations = locations;
        _places = places;
        _events = events;
        _map = map;
        _analytics = analytics;
        _media = media;
        _logger = logger;
        _mediaStorage = mediaStorage;
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

    [HttpGet("sidebar-summary")]
    public async Task<ActionResult<RegionalSidebarSummaryDto>> GetSidebarSummary(
        [FromQuery] int? regionId = null)
    {
        if (regionId is <= 0)
            return BadRequest(new { message = "regionId must be positive." });

        try
        {
            return Ok(await _regionalDashboard.GetSidebarSummaryAsync(GetCurrentUserId(), regionId));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("analytics/overview")]
    public async Task<ActionResult<RegionalAnalyticsOverviewDto>> GetAnalyticsOverview(
        [FromQuery] int regionId,
        [FromQuery] string? period = null,
        [FromQuery] string? entityType = null,
        [FromQuery] string? search = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 25,
        [FromQuery] string? sort = null,
        CancellationToken cancellationToken = default)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        try
        {
            return Ok(await _analytics.GetOverviewAsync(
                GetCurrentUserId(),
                new RegionalAnalyticsOverviewQuery
                {
                    RegionId = regionId,
                    Period = period,
                    EntityType = entityType,
                    Search = search,
                    Skip = skip,
                    Take = take,
                    Sort = sort
                },
                cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("analytics/entities/{entityType}/{entityId:int}/details")]
    [EnableRateLimiting("analytics")]
    public async Task<ActionResult<RegionalAnalyticsEntityDetailDto>> GetAnalyticsEntityDetails(
        [FromRoute] string entityType,
        [FromRoute] int entityId,
        [FromQuery] int regionId,
        [FromQuery] string? period = null,
        CancellationToken cancellationToken = default)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        if (entityId <= 0)
            return BadRequest(new { message = "entityId is required." });

        try
        {
            return Ok(await _analytics.GetEntityDetailAsync(
                GetCurrentUserId(),
                new RegionalAnalyticsEntityDetailQuery
                {
                    RegionId = regionId,
                    EntityType = entityType,
                    EntityId = entityId,
                    Period = period
                },
                cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("analytics/monthly-visits")]
    public async Task<ActionResult<RegionalMonthlyVisitsDto>> GetMonthlyVisits(
        [FromQuery] int regionId,
        [FromQuery] int selectedYearsBack = 1,
        [FromQuery] string? entityType = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        if (selectedYearsBack is < 1 or > 3)
            return BadRequest(new { message = "selectedYearsBack must be between 1 and 3." });

        try
        {
            return Ok(await _analytics.GetMonthlyVisitsAsync(
                GetCurrentUserId(),
                new RegionalMonthlyVisitsQuery
                {
                    RegionId = regionId,
                    SelectedYearsBack = selectedYearsBack,
                    EntityType = entityType,
                    Search = search
                },
                cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("analytics/tag-views")]
    [EnableRateLimiting("analytics")]
    public async Task<ActionResult<RegionalTagViewsAnalyticsResponseDto>> GetTagViews(
        [FromQuery] int regionId,
        [FromQuery] string? period = null,
        [FromQuery] int take = 15,
        [FromQuery] string? entityType = null,
        CancellationToken cancellationToken = default)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        if (!TryReadPositiveIntQueryValues("tagIds", out var tagIds, out var tagIdsError))
            return BadRequest(new { message = tagIdsError });

        try
        {
            return Ok(await _analytics.GetTagViewsAsync(
                GetCurrentUserId(),
                new RegionalTagViewsQuery
                {
                    RegionId = regionId,
                    Period = period,
                    TagIds = tagIds,
                    Take = take,
                    EntityType = entityType
                },
                cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("analytics/export")]
    public async Task<IActionResult> ExportAnalytics(
        [FromQuery] int regionId,
        [FromQuery] string? period = null,
        [FromQuery] string? entityType = null,
        [FromQuery] string? search = null,
        [FromQuery] string? sort = null,
        CancellationToken cancellationToken = default)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        try
        {
            var csv = await _analytics.ExportCsvAsync(
                GetCurrentUserId(),
                new RegionalAnalyticsOverviewQuery
                {
                    RegionId = regionId,
                    Period = period,
                    EntityType = entityType,
                    Search = search,
                    Skip = 0,
                    Take = 5000,
                    Sort = sort
                },
                cancellationToken);

            return File(
                Encoding.UTF8.GetBytes(csv),
                "text/csv",
                $"regional-analytics-{regionId}-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("map/entities")]
    public async Task<ActionResult<RegionalMapResponseDto>> GetMapEntities(
        [FromQuery] int regionId,
        [FromQuery] string? types = null,
        CancellationToken cancellationToken = default)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        try
        {
            return Ok(await _map.GetMapAsync(
                GetCurrentUserId(),
                regionId,
                SplitDelimitedValues(types),
                cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("map/viewport")]
    public async Task<ActionResult<RegionalMapViewportDto>> GetMapViewport(
        [FromQuery] int regionId,
        CancellationToken cancellationToken = default)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        try
        {
            return Ok(await _map.GetViewportAsync(GetCurrentUserId(), regionId, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("map/zones")]
    public async Task<ActionResult<List<RegionalMapZoneDto>>> GetMapZones(
        [FromQuery] int regionId,
        CancellationToken cancellationToken = default)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        try
        {
            return Ok(await _map.GetZonesAsync(GetCurrentUserId(), regionId, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("map/search")]
    public async Task<ActionResult<RegionalMapSearchResponseDto>> SearchMap(
        [FromQuery] int regionId,
        [FromQuery] string? query = null,
        CancellationToken cancellationToken = default)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        try
        {
            return Ok(await _map.SearchAsync(GetCurrentUserId(), regionId, query, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpPost("media/images")]
    [RequestSizeLimit(96 * 1024 * 1024)]
    public async Task<ActionResult<RegionalImageUploadResponseDto>> UploadImages(
        [FromQuery] int regionId,
        [FromForm] List<IFormFile>? files,
        CancellationToken cancellationToken = default)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        if (files is null || files.Count == 0)
            return BadRequest(new { message = "Please select at least one image to upload." });

        try
        {
            var uploads = new List<RegionalImageUploadFileDto>();
            foreach (var file in files)
            {
                await using var stream = new MemoryStream();
                await file.CopyToAsync(stream, cancellationToken);
                uploads.Add(new RegionalImageUploadFileDto
                {
                    FileName = file.FileName,
                    ContentType = file.ContentType,
                    Content = stream.ToArray()
                });
            }

            return Ok(await _media.UploadImagesAsync(
                GetCurrentUserId(),
                regionId,
                uploads,
                cancellationToken));
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

    [HttpPost("destinations")]
    public async Task<ActionResult<RegionalDestinationDetailDto>> CreateDestination()
    {
        try
        {
            var request = await ReadCreateRequestAsync<RegionalDestinationCreateRequestDto>();
            if (request is null)
                return BadRequest(new { message = "Request body is required." });

            if (request.RegionId <= 0)
                return BadRequest(new { message = "regionId is required." });

            var created = await _destinations.CreateDestinationAsync(GetCurrentUserId(), request);
            return CreatedAtAction(
                nameof(GetDestination),
                new { id = created.Id, regionId = created.RegionId },
                created);
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

    [HttpGet("destination-tags")]
    public async Task<ActionResult<List<RegionalDestinationTagDto>>> GetDestinationTags(
        [FromQuery] string? search = null)
    {
        try
        {
            return Ok(await _destinations.GetDestinationTagsAsync(GetCurrentUserId(), search));
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

    [HttpPost("locations")]
    public async Task<ActionResult<RegionalLocationDetailDto>> CreateLocation()
    {
        try
        {
            var request = await ReadCreateRequestAsync<RegionalLocationCreateRequestDto>();
            if (request is null)
                return BadRequest(new { message = "Request body is required." });

            if (request.RegionId <= 0)
                return BadRequest(new { message = "regionId is required." });

            var created = await _locations.CreateLocationAsync(GetCurrentUserId(), request);
            return CreatedAtAction(
                nameof(GetLocation),
                new { id = created.Id, regionId = created.RegionId },
                created);
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

    [HttpGet("location-parent-destinations")]
    public async Task<ActionResult<List<RegionalLocationParentDestinationDto>>> GetLocationParentDestinations(
        [FromQuery] int regionId,
        [FromQuery] string? search = null,
        [FromQuery] int take = 50)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        try
        {
            return Ok(await _locations.GetParentDestinationsAsync(
                GetCurrentUserId(),
                regionId,
                search,
                take));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("location-tags")]
    public async Task<ActionResult<List<RegionalLocationTagDto>>> GetLocationTags(
        [FromQuery] string? search = null)
    {
        try
        {
            return Ok(await _locations.GetLocationTagsAsync(GetCurrentUserId(), search));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("location-types")]
    public async Task<ActionResult<List<RegionalLocationTypeDto>>> GetLocationTypes()
    {
        try
        {
            return Ok(await _locations.GetLocationTypesAsync(GetCurrentUserId()));
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

    [HttpPost("places")]
    public async Task<ActionResult<RegionalPlaceDetailDto>> CreatePlace()
    {
        try
        {
            var request = await ReadCreateRequestAsync<RegionalPlaceCreateRequestDto>();
            if (request is null)
                return BadRequest(new { message = "Request body is required." });

            if (request.RegionId <= 0)
                return BadRequest(new { message = "regionId is required." });

            var created = await _places.CreatePlaceAsync(GetCurrentUserId(), request);
            return CreatedAtAction(
                nameof(GetPlace),
                new { id = created.Id, regionId = created.RegionId },
                created);
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

    [HttpPost("places/{id:int}/owners")]
    public async Task<ActionResult<RegionalPlaceDetailDto>> AddPlaceOwner(
        [FromRoute] int id,
        [FromQuery] int regionId,
        [FromBody] RegionalPlaceAddOwnerRequestDto? request)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        if (request is null)
            return BadRequest(new { message = "Request body is required." });

        try
        {
            return Ok(await _places.AddOwnerAsync(GetCurrentUserId(), regionId, id, request));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("place-parent-locations")]
    public async Task<ActionResult<List<RegionalPlaceParentLocationDto>>> GetPlaceParentLocations(
        [FromQuery] int regionId,
        [FromQuery] string? search = null,
        [FromQuery] int take = 50)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        try
        {
            return Ok(await _places.GetParentLocationsAsync(
                GetCurrentUserId(),
                regionId,
                search,
                take));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("place-tags")]
    public async Task<ActionResult<List<RegionalPlaceTagDto>>> GetPlaceTags(
        [FromQuery] string? search = null)
    {
        try
        {
            return Ok(await _places.GetPlaceTagsAsync(GetCurrentUserId(), search));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("place-types")]
    public async Task<ActionResult<List<RegionalPlaceTypeDto>>> GetPlaceTypes()
    {
        try
        {
            return Ok(await _places.GetPlaceTypesAsync(GetCurrentUserId()));
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

    [HttpPost("events")]
    public async Task<ActionResult<RegionalEventDetailDto>> CreateEvent()
    {
        try
        {
            var request = await ReadCreateRequestAsync<RegionalEventCreateRequestDto>();
            if (request is null)
                return BadRequest(new { message = "Request body is required." });

            if (request.RegionId <= 0)
                return BadRequest(new { message = "regionId is required." });

            var created = await _events.CreateEventAsync(GetCurrentUserId(), request);
            return CreatedAtAction(
                nameof(GetEvent),
                new { id = created.Id, regionId = created.RegionId },
                created);
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

    [HttpGet("event-parent-destinations")]
    public async Task<ActionResult<List<RegionalEventParentDestinationDto>>> GetEventParentDestinations(
        [FromQuery] int regionId,
        [FromQuery] string? search = null,
        [FromQuery] int take = 50)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        try
        {
            return Ok(await _events.GetParentDestinationsAsync(
                GetCurrentUserId(),
                regionId,
                search,
                take));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("event-parent-locations")]
    public async Task<ActionResult<List<RegionalEventParentLocationDto>>> GetEventParentLocations(
        [FromQuery] int regionId,
        [FromQuery] string? search = null,
        [FromQuery] int take = 50)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        try
        {
            return Ok(await _events.GetParentLocationsAsync(
                GetCurrentUserId(),
                regionId,
                search,
                take));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("event-parent-places")]
    public async Task<ActionResult<List<RegionalEventParentPlaceDto>>> GetEventParentPlaces(
        [FromQuery] int regionId,
        [FromQuery] string? search = null,
        [FromQuery] int take = 50)
    {
        if (regionId <= 0)
            return BadRequest(new { message = "regionId is required." });

        try
        {
            return Ok(await _events.GetParentPlacesAsync(
                GetCurrentUserId(),
                regionId,
                search,
                take));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("event-tags")]
    public async Task<ActionResult<List<RegionalEventTagDto>>> GetEventTags(
        [FromQuery] string? search = null)
    {
        try
        {
            return Ok(await _events.GetEventTagsAsync(GetCurrentUserId(), search));
        }
        catch (Exception ex)
        {
            return HandleException(ex);
        }
    }

    [HttpGet("event-types")]
    public async Task<ActionResult<List<RegionalEventTypeDto>>> GetEventTypes()
    {
        try
        {
            return Ok(await _events.GetEventTypesAsync(GetCurrentUserId()));
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


    private async Task<T?> ReadCreateRequestAsync<T>() where T : class
    {
        if (!Request.HasFormContentType)
        {
            return await JsonSerializer.DeserializeAsync<T>(Request.Body, JsonOptions);
        }

        var form = await Request.ReadFormAsync();
        var payload = form["payload"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        var request = JsonSerializer.Deserialize<T>(payload, JsonOptions);
        if (request is null)
            return null;

        var cardUrl = await StoreRequiredFormImageAsync(form.Files.GetFile("cardImage"), "Card image is required.");
        var coverUrl = await StoreRequiredFormImageAsync(form.Files.GetFile("coverImage"), "Cover image is required.");
        var galleryUrls = new List<string>();
        foreach (var file in form.Files.GetFiles("galleryImages"))
        {
            galleryUrls.Add(await StoreOptionalFormImageAsync(file));
        }

        ApplyImageUrls(request, cardUrl, coverUrl, galleryUrls);
        return request;
    }

    private async Task<string> StoreRequiredFormImageAsync(IFormFile? file, string message)
    {
        if (file is null || file.Length == 0)
            throw new InvalidOperationException(message);

        return await StoreOptionalFormImageAsync(file);
    }

    private async Task<string> StoreOptionalFormImageAsync(IFormFile file)
    {
        await using var stream = file.OpenReadStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);

        var stored = await _mediaStorage.StoreAsync(GetCurrentUserId(), memory.ToArray());
        return stored.MediaUrl;
    }

    private static void ApplyImageUrls<T>(T request, string cardUrl, string coverUrl, List<string> galleryUrls)
    {
        switch (request)
        {
            case RegionalDestinationCreateRequestDto destination:
                destination.CardImageUrl = cardUrl;
                destination.CoverImageUrl = coverUrl;
                destination.GalleryImageUrls = galleryUrls;
                break;
            case RegionalLocationCreateRequestDto location:
                location.CardImageUrl = cardUrl;
                location.CoverImageUrl = coverUrl;
                location.GalleryImageUrls = galleryUrls;
                break;
            case RegionalPlaceCreateRequestDto place:
                place.CardImageUrl = cardUrl;
                place.CoverImageUrl = coverUrl;
                place.GalleryImageUrls = galleryUrls;
                break;
            case RegionalEventCreateRequestDto eventRequest:
                eventRequest.CardImageUrl = cardUrl;
                eventRequest.CoverImageUrl = coverUrl;
                eventRequest.GalleryImageUrls = galleryUrls;
                break;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

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
        if (ex is not UnauthorizedAccessException &&
            ex is not KeyNotFoundException &&
            ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Unexpected regional admin request failure.");
        }

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

    private static IEnumerable<string> SplitDelimitedValues(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private bool TryReadPositiveIntQueryValues(string key, out List<int> values, out string? error)
    {
        values = new List<int>();
        error = null;

        if (!Request.Query.TryGetValue(key, out var rawValues))
        {
            return true;
        }

        foreach (var rawValue in rawValues)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                continue;
            }

            var tokens = rawValue.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ||
                    parsed <= 0)
                {
                    error = key + " must contain positive integer values.";
                    values.Clear();
                    return false;
                }

                values.Add(parsed);
            }
        }

        values = values.Distinct().ToList();
        return true;
    }
}

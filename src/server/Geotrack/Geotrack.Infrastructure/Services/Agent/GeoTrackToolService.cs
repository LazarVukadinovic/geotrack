using Geotrack.Application.DTOs.Agent;
using Geotrack.Application.DTOs.GeoTrackTools;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Services;
using Geotrack.Infrastructure.Spatial;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Geotrack.Infrastructure.Services.Agent;

public class GeoTrackToolService : IGeoTrackToolService
{
    private const int DefaultLimit = 5;
    private const int MaxLimit = 10;
    private const int CandidateLimit = 120;
    private const int MaxQueryLength = 100;
    private const double MaxViewportSpan = 10d;
    private static readonly GeometryFactory ViewportGeometryFactory = new(new PrecisionModel(), 4326);
    private readonly AppDbContext _db;
    private readonly IAgentQueryPlanner _planner;

    public GeoTrackToolService(AppDbContext db, IAgentQueryPlanner planner)
    {
        _db = db;
        _planner = planner;
    }

    public async Task<IReadOnlyList<GeoTrackToolItemDto>> SearchPlacesAsync(
        GeoTrackToolSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        request = await ExpandRawQueryAsync(request, "places", cancellationToken);
        var criteria = await BuildCriteriaAsync(request, cancellationToken);
        if (!criteria.HasAny)
        {
            return Array.Empty<GeoTrackToolItemDto>();
        }

        var query = _db.Places
            .AsNoTracking()
            .Where(place => place.Stay == null);

        if (criteria.DestinationId.HasValue)
        {
            query = query.Where(place => place.Location.DestinationId == criteria.DestinationId.Value);
        }

        if (criteria.PlaceTypeTerms.Count > 0 || criteria.TagTerms.Count > 0)
        {
            query = query.Where(place =>
                criteria.PlaceTypeTerms.Contains(place.PlaceType.Name.ToLower()) ||
                place.PlaceTags.Any(tag =>
                    criteria.TagTerms.Contains(tag.Tag.Slug.ToLower()) ||
                    criteria.TagTerms.Contains(tag.Tag.Name.ToLower())));
        }

        query = ApplyPlaceTextFilter(query, criteria.Query);

        var items = await query
            .OrderByDescending(place => place.PlaceReviews.Select(review => (double?)review.Rating).Average() ?? 0)
            .ThenByDescending(place => place.PlaceReviews.Count())
            .ThenBy(place => place.Name)
            .Take(NormalizeLimit(request.Limit))
            .Select(place => new GeoTrackToolItemDto
            {
                Id = place.Id,
                EntityType = "place",
                Title = place.Name,
                Description = Trim(place.Description, 280),
                Location = place.Location.Name,
                Destination = place.Location.Destination.Name,
                Category = place.PlaceType.Name,
                DetailRoute = "/places/" + place.Id,
                ImageUrl = place.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                Rating = place.PlaceReviews.Select(review => (double?)review.Rating).Average(),
                ReviewCount = place.PlaceReviews.Count(),
                Latitude = HasUsableCoordinates(place.Latitude, place.Longitude) ? place.Latitude : null,
                Longitude = HasUsableCoordinates(place.Latitude, place.Longitude) ? place.Longitude : null,
                Tags = place.PlaceTags
                    .OrderBy(tag => tag.Tag.Name)
                    .Select(tag => tag.Tag.Name)
                    .Take(8)
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        return NormalizeImageUrls(items);
    }

    public async Task<GeoTrackToolItemDto?> GetPlaceDetailsAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return null;
        }

        var item = await _db.Places
            .AsNoTracking()
            .Where(place => place.Id == id && place.Stay == null)
            .Select(place => new GeoTrackToolItemDto
            {
                Id = place.Id,
                EntityType = "place",
                Title = place.Name,
                Description = Trim(place.Description, 800),
                Location = place.Location.Name,
                Destination = place.Location.Destination.Name,
                Category = place.PlaceType.Name,
                DetailRoute = "/places/" + place.Id,
                ImageUrl = place.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                Rating = place.PlaceReviews.Select(review => (double?)review.Rating).Average(),
                ReviewCount = place.PlaceReviews.Count(),
                Latitude = HasUsableCoordinates(place.Latitude, place.Longitude) ? place.Latitude : null,
                Longitude = HasUsableCoordinates(place.Latitude, place.Longitude) ? place.Longitude : null,
                Tags = place.PlaceTags
                    .OrderBy(tag => tag.Tag.Name)
                    .Select(tag => tag.Tag.Name)
                    .Take(12)
                    .ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);

        return NormalizeImageUrl(item);
    }

    public async Task<IReadOnlyList<GeoTrackToolItemDto>> SearchStaysAsync(
        GeoTrackToolSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        request = await ExpandRawQueryAsync(request, "stays", cancellationToken);
        var criteria = await BuildCriteriaAsync(request, cancellationToken);
        if (!criteria.HasAny)
        {
            return Array.Empty<GeoTrackToolItemDto>();
        }

        var query = _db.Places
            .AsNoTracking()
            .Where(place => place.Stay != null);

        if (criteria.DestinationId.HasValue)
        {
            query = query.Where(place => place.Location.DestinationId == criteria.DestinationId.Value);
        }

        if (criteria.StayTypeTerms.Count > 0 || criteria.TagTerms.Count > 0)
        {
            query = query.Where(place =>
                (place.Stay != null && criteria.StayTypeTerms.Contains(place.Stay.StayType.Name.ToLower())) ||
                place.PlaceTags.Any(tag =>
                    criteria.TagTerms.Contains(tag.Tag.Slug.ToLower()) ||
                    criteria.TagTerms.Contains(tag.Tag.Name.ToLower())));
        }

        query = ApplyPlaceTextFilter(query, criteria.Query);

        var items = await query
            .OrderByDescending(place => place.PlaceReviews.Select(review => (double?)review.Rating).Average() ?? 0)
            .ThenByDescending(place => place.PlaceReviews.Count())
            .ThenBy(place => place.Name)
            .Take(NormalizeLimit(request.Limit))
            .Select(place => new GeoTrackToolItemDto
            {
                Id = place.Id,
                EntityType = "stay",
                Title = place.Name,
                Description = Trim(place.Description, 280),
                Location = place.Location.Name,
                Destination = place.Location.Destination.Name,
                Category = place.Stay != null ? place.Stay.StayType.Name : "Stay",
                DetailRoute = "/stays/" + place.Id,
                ImageUrl = place.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                Rating = place.PlaceReviews.Select(review => (double?)review.Rating).Average(),
                ReviewCount = place.PlaceReviews.Count(),
                Latitude = HasUsableCoordinates(place.Latitude, place.Longitude) ? place.Latitude : null,
                Longitude = HasUsableCoordinates(place.Latitude, place.Longitude) ? place.Longitude : null,
                Tags = place.PlaceTags
                    .OrderBy(tag => tag.Tag.Name)
                    .Select(tag => tag.Tag.Name)
                    .Take(8)
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        return NormalizeImageUrls(items);
    }

    public async Task<GeoTrackToolItemDto?> GetStayDetailsAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return null;
        }

        var item = await _db.Places
            .AsNoTracking()
            .Where(place => place.Id == id && place.Stay != null)
            .Select(place => new GeoTrackToolItemDto
            {
                Id = place.Id,
                EntityType = "stay",
                Title = place.Name,
                Description = Trim(place.Description, 800),
                Location = place.Location.Name,
                Destination = place.Location.Destination.Name,
                Category = place.Stay != null ? place.Stay.StayType.Name : "Stay",
                DetailRoute = "/stays/" + place.Id,
                ImageUrl = place.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                Rating = place.PlaceReviews.Select(review => (double?)review.Rating).Average(),
                ReviewCount = place.PlaceReviews.Count(),
                Latitude = HasUsableCoordinates(place.Latitude, place.Longitude) ? place.Latitude : null,
                Longitude = HasUsableCoordinates(place.Latitude, place.Longitude) ? place.Longitude : null,
                Tags = place.PlaceTags
                    .OrderBy(tag => tag.Tag.Name)
                    .Select(tag => tag.Tag.Name)
                    .Take(12)
                    .ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);

        return NormalizeImageUrl(item);
    }

    public async Task<IReadOnlyList<GeoTrackToolItemDto>> SearchEventsAsync(
        GeoTrackToolSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        request = await ExpandRawQueryAsync(request, "events", cancellationToken);
        var criteria = await BuildCriteriaAsync(request, cancellationToken);
        if (!criteria.HasAny)
        {
            return Array.Empty<GeoTrackToolItemDto>();
        }

        var query = _db.Events.AsNoTracking();
        if (request.UpcomingOnly)
        {
            query = query.Where(evt => evt.EndDate >= DateTime.UtcNow);
        }

        if (criteria.DestinationId.HasValue)
        {
            query = query.Where(evt => evt.Location.DestinationId == criteria.DestinationId.Value);
        }

        if (criteria.EventCategoryTerms.Count > 0 || criteria.TagTerms.Count > 0)
        {
            query = query.Where(evt =>
                criteria.EventCategoryTerms.Contains(evt.EventType.Name.ToLower()) ||
                evt.EventTags.Any(tag =>
                    criteria.TagTerms.Contains(tag.Tag.Slug.ToLower()) ||
                    criteria.TagTerms.Contains(tag.Tag.Name.ToLower())));
        }

        query = ApplyEventTextFilter(query, criteria.Query);

        var items = await query
            .OrderBy(evt => evt.StartDate)
            .ThenBy(evt => evt.Name)
            .Take(NormalizeLimit(request.Limit))
            .Select(evt => new GeoTrackToolItemDto
            {
                Id = evt.Id,
                EntityType = "event",
                Title = evt.Name,
                Description = Trim(evt.Description, 280),
                Location = evt.Place != null ? evt.Place.Name : evt.Location.Name,
                Destination = evt.Location.Destination.Name,
                Category = evt.EventType.Name,
                DetailRoute = "/events/" + evt.Id,
                ImageUrl = evt.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                Latitude = evt.Place != null && HasUsableCoordinates(evt.Place.Latitude, evt.Place.Longitude)
                    ? evt.Place.Latitude
                    : (HasUsableCoordinates(evt.Location.Latitude, evt.Location.Longitude) ? evt.Location.Latitude : null),
                Longitude = evt.Place != null && HasUsableCoordinates(evt.Place.Latitude, evt.Place.Longitude)
                    ? evt.Place.Longitude
                    : (HasUsableCoordinates(evt.Location.Latitude, evt.Location.Longitude) ? evt.Location.Longitude : null),
                StartsAt = evt.StartDate,
                EndsAt = evt.EndDate,
                Tags = evt.EventTags
                    .OrderBy(tag => tag.Tag.Name)
                    .Select(tag => tag.Tag.Name)
                    .Take(8)
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        return NormalizeImageUrls(items);
    }

    public async Task<GeoTrackToolItemDto?> GetEventDetailsAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return null;
        }

        var item = await _db.Events
            .AsNoTracking()
            .Where(evt => evt.Id == id)
            .Select(evt => new GeoTrackToolItemDto
            {
                Id = evt.Id,
                EntityType = "event",
                Title = evt.Name,
                Description = Trim(evt.Description, 800),
                Location = evt.Place != null ? evt.Place.Name : evt.Location.Name,
                Destination = evt.Location.Destination.Name,
                Category = evt.EventType.Name,
                DetailRoute = "/events/" + evt.Id,
                ImageUrl = evt.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                Latitude = evt.Place != null && HasUsableCoordinates(evt.Place.Latitude, evt.Place.Longitude)
                    ? evt.Place.Latitude
                    : (HasUsableCoordinates(evt.Location.Latitude, evt.Location.Longitude) ? evt.Location.Latitude : null),
                Longitude = evt.Place != null && HasUsableCoordinates(evt.Place.Latitude, evt.Place.Longitude)
                    ? evt.Place.Longitude
                    : (HasUsableCoordinates(evt.Location.Latitude, evt.Location.Longitude) ? evt.Location.Longitude : null),
                StartsAt = evt.StartDate,
                EndsAt = evt.EndDate,
                Tags = evt.EventTags
                    .OrderBy(tag => tag.Tag.Name)
                    .Select(tag => tag.Tag.Name)
                    .Take(12)
                    .ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);

        return NormalizeImageUrl(item);
    }

    public async Task<GeoTrackMapFiltersDto> GetMapFiltersAsync(
        GeoTrackMapFiltersRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateBounds(request);
        var envelope = ViewportGeometryFactory.CreatePolygon(new[]
        {
            new Coordinate(request.West, request.South),
            new Coordinate(request.East, request.South),
            new Coordinate(request.East, request.North),
            new Coordinate(request.West, request.North),
            new Coordinate(request.West, request.South)
        });

        var placeCount = await _db.Places
            .AsNoTracking()
            .Where(place =>
                place.Stay == null &&
                EF.Property<Point?>(place, SpatialPointFactory.CoordinatesPropertyName) != null &&
                EF.Property<Point>(place, SpatialPointFactory.CoordinatesPropertyName).Intersects(envelope))
            .CountAsync(cancellationToken);

        var stayCount = await _db.Places
            .AsNoTracking()
            .Where(place =>
                place.Stay != null &&
                EF.Property<Point?>(place, SpatialPointFactory.CoordinatesPropertyName) != null &&
                EF.Property<Point>(place, SpatialPointFactory.CoordinatesPropertyName).Intersects(envelope))
            .CountAsync(cancellationToken);

        var eventCount = await _db.Events
            .AsNoTracking()
            .Where(evt =>
                EF.Property<Point?>(evt, SpatialPointFactory.CoordinatesPropertyName) != null &&
                EF.Property<Point>(evt, SpatialPointFactory.CoordinatesPropertyName).Intersects(envelope))
            .CountAsync(cancellationToken);

        return new GeoTrackMapFiltersDto
        {
            Categories = new List<GeoTrackMapFilterDto>
            {
                new() { Id = "places", Label = "Places", Count = placeCount },
                new() { Id = "stays", Label = "Stays", Count = stayCount },
                new() { Id = "events", Label = "Events", Count = eventCount }
            }.Where(category => category.Count > 0).ToList()
        };
    }

    public async Task<GeoTrackAgentContextDto> BuildAgentContextAsync(
        string message,
        AgentContextOptionsDto? contextOptions,
        int maxItems,
        CancellationToken cancellationToken = default)
    {
        var limit = Math.Clamp(maxItems, 1, MaxLimit);
        var plan = await _planner.PlanAsync(message, contextOptions, cancellationToken);
        var context = new GeoTrackAgentContextDto
        {
            Plan = plan,
            LocationUsed = plan.LocationUsed,
            DestinationId = plan.DestinationId,
            ClarificationNeeded = plan.ClarificationNeeded,
            ClarificationQuestion = plan.ClarificationQuestion,
            FiltersUsed = BuildFilterList(plan)
        };

        if (plan.ClarificationNeeded || !plan.SearchCatalog)
        {
            return context;
        }

        var searchAll = plan.EntityTypes.Count == 0 || plan.EntityTypes.Contains("all");
        var shouldSearchPlaces = searchAll ||
            plan.EntityTypes.Contains("places") ||
            plan.PlaceTypes.Count > 0 ||
            plan.Categories.Any(category => category is "beach" or "restaurant" or "cafe" or "landmark");
        var shouldSearchStays = searchAll || plan.EntityTypes.Contains("stays");
        var shouldSearchEvents = searchAll ||
            plan.EntityTypes.Contains("events") ||
            plan.EventCategories.Count > 0 ||
            plan.Categories.Contains("festival");

        if (shouldSearchPlaces)
        {
            var places = await SearchWithFallbacksAsync(
                "geotrack_search_places",
                "specific",
                BuildRequestFromPlan(plan, limit, "places"),
                context,
                SearchPlacesAsync,
                cancellationToken);
            context.Places = places;
        }

        if (shouldSearchStays)
        {
            var stays = await SearchWithFallbacksAsync(
                "geotrack_search_stays",
                "specific",
                BuildRequestFromPlan(plan, limit, "stays"),
                context,
                SearchStaysAsync,
                cancellationToken);
            context.Stays = stays;
        }

        if (shouldSearchEvents)
        {
            var events = await SearchWithFallbacksAsync(
                "geotrack_search_events",
                "specific",
                BuildRequestFromPlan(plan, limit, "events"),
                context,
                SearchEventsAsync,
                cancellationToken);
            context.Events = events;
        }

        context.ToolsUsed = context.Searches
            .Select(search => search.Tool)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        context.SearchAttempted = context.Searches.Count > 0;
        context.ResultCount = context.Places.Count + context.Stays.Count + context.Events.Count;
        context.NoResults = context.SearchAttempted && context.ResultCount == 0;
        return context;
    }

    private async Task<List<GeoTrackToolItemDto>> SearchWithFallbacksAsync(
        string toolName,
        string searchType,
        GeoTrackToolSearchRequest request,
        GeoTrackAgentContextDto context,
        Func<GeoTrackToolSearchRequest, CancellationToken, Task<IReadOnlyList<GeoTrackToolItemDto>>> search,
        CancellationToken cancellationToken)
    {
        var results = await RunSearchAsync(toolName, searchType, request, context, search, cancellationToken);
        if (results.Count > 0 || !request.DestinationId.HasValue)
        {
            return results.ToList();
        }

        if (HasStructuredFilters(request) && !string.IsNullOrWhiteSpace(context.Plan?.QueryText))
        {
            var tokenFallback = CloneSearchRequest(request);
            tokenFallback.Tags.Clear();
            tokenFallback.Categories.Clear();
            tokenFallback.PlaceTypes.Clear();
            tokenFallback.StayTypes.Clear();
            tokenFallback.EventCategories.Clear();
            tokenFallback.Query = context.Plan.QueryText;
            results = await RunSearchAsync(toolName, "location_and_query_fallback", tokenFallback, context, search, cancellationToken);
            if (results.Count > 0)
            {
                return results.ToList();
            }
        }

        if (HasStructuredFilters(request))
        {
            var locationFallback = CloneSearchRequest(request);
            locationFallback.Query = null;
            locationFallback.Tags.Clear();
            locationFallback.Categories.Clear();
            locationFallback.PlaceTypes.Clear();
            locationFallback.StayTypes.Clear();
            locationFallback.EventCategories.Clear();
            results = await RunSearchAsync(toolName, "location_fallback", locationFallback, context, search, cancellationToken);
        }

        return results.ToList();
    }

    private static async Task<IReadOnlyList<GeoTrackToolItemDto>> RunSearchAsync(
        string toolName,
        string searchType,
        GeoTrackToolSearchRequest request,
        GeoTrackAgentContextDto context,
        Func<GeoTrackToolSearchRequest, CancellationToken, Task<IReadOnlyList<GeoTrackToolItemDto>>> search,
        CancellationToken cancellationToken)
    {
        var results = await search(request, cancellationToken);
        context.Searches.Add(new GeoTrackToolSearchTraceDto
        {
            Tool = toolName,
            SearchType = searchType,
            Query = request.Query,
            Location = request.Location,
            DestinationId = request.DestinationId,
            Filters = BuildFilterList(request),
            ResultCount = results.Count
        });

        return results;
    }

    private async Task<GeoTrackToolSearchRequest> ExpandRawQueryAsync(
        GeoTrackToolSearchRequest request,
        string target,
        CancellationToken cancellationToken)
    {
        if (HasStructuredCriteria(request) || string.IsNullOrWhiteSpace(request.Query))
        {
            return request;
        }

        var plan = await _planner.PlanAsync(request.Query, null, cancellationToken);
        if (!plan.SearchCatalog && !plan.DestinationId.HasValue)
        {
            return request;
        }

        var expanded = CloneSearchRequest(request);
        expanded.DestinationId ??= plan.DestinationId;
        expanded.Location ??= plan.LocationText;
        expanded.Query = plan.QueryText;

        if (target == "places")
        {
            expanded.Tags.AddRange(plan.Tags);
            expanded.Categories.AddRange(plan.Categories);
            expanded.PlaceTypes.AddRange(plan.PlaceTypes);
        }
        else if (target == "stays")
        {
            expanded.Tags.AddRange(plan.Tags);
            expanded.Categories.AddRange(plan.Categories);
            expanded.StayTypes.AddRange(plan.StayTypes);
        }
        else if (target == "events")
        {
            expanded.Tags.AddRange(plan.Tags);
            expanded.Categories.AddRange(plan.Categories);
            expanded.EventCategories.AddRange(plan.EventCategories);
        }

        return expanded;
    }

    private async Task<ToolSearchCriteria> BuildCriteriaAsync(
        GeoTrackToolSearchRequest request,
        CancellationToken cancellationToken)
    {
        var destinationId = request.DestinationId;
        if (!destinationId.HasValue && !string.IsNullOrWhiteSpace(request.Location))
        {
            destinationId = await ResolveDestinationIdAsync(request.Location, cancellationToken);
        }

        var query = NormalizeQuery(request.Query);
        var tagTerms = BuildFilterTerms(request.Tags.Concat(request.Categories));
        var placeTypeTerms = BuildFilterTerms(request.PlaceTypes.Concat(request.Categories));
        var stayTypeTerms = BuildFilterTerms(request.StayTypes.Concat(request.Categories));
        var eventCategoryTerms = BuildFilterTerms(request.EventCategories.Concat(request.Categories));

        return new ToolSearchCriteria(
            query,
            destinationId,
            tagTerms,
            placeTypeTerms,
            stayTypeTerms,
            eventCategoryTerms);
    }

    private async Task<int?> ResolveDestinationIdAsync(string locationText, CancellationToken cancellationToken)
    {
        var normalized = AgentTextNormalizer.Normalize(locationText);
        var tokens = AgentTextNormalizer.Tokenize(locationText).ToHashSet(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var destinations = await _db.Destinations
            .AsNoTracking()
            .Select(destination => new
            {
                destination.Id,
                destination.Name
            })
            .ToListAsync(cancellationToken);

        var destinationMatch = destinations
            .Select(destination => new
            {
                destination.Id,
                Score = ScoreLocationName(destination.Name, normalized, tokens)
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .FirstOrDefault();
        if (destinationMatch is not null)
        {
            return destinationMatch.Id;
        }

        var locations = await _db.Locations
            .AsNoTracking()
            .Select(location => new
            {
                location.DestinationId,
                location.Name,
                DestinationName = location.Destination.Name
            })
            .ToListAsync(cancellationToken);

        return locations
            .Select(location => new
            {
                location.DestinationId,
                Score = Math.Max(
                    ScoreLocationName(location.Name, normalized, tokens),
                    ScoreLocationName(location.DestinationName, normalized, tokens))
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .Select(item => (int?)item.DestinationId)
            .FirstOrDefault();
    }

    private static int ScoreLocationName(string name, string normalizedText, IReadOnlySet<string> tokens)
    {
        var normalizedName = AgentTextNormalizer.Normalize(name);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return 0;
        }

        if (normalizedText == normalizedName)
        {
            return 300;
        }

        if (AgentTextNormalizer.ContainsPhrase(normalizedText, normalizedName))
        {
            return 220;
        }

        var nameTokens = AgentTextNormalizer.Tokenize(name).ToHashSet(StringComparer.Ordinal);
        return nameTokens.Count > 0 && nameTokens.All(tokens.Contains)
            ? 160 + nameTokens.Count * 20
            : 0;
    }

    private static IQueryable<Geotrack.Domain.Entities.Core.Place> ApplyPlaceTextFilter(
        IQueryable<Geotrack.Domain.Entities.Core.Place> query,
        string? rawQuery)
    {
        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            return query;
        }

        var pattern = LikePattern(rawQuery);
        return query.Where(place =>
            EF.Functions.ILike(place.Name, pattern) ||
            (place.Subtitle != null && EF.Functions.ILike(place.Subtitle, pattern)) ||
            EF.Functions.ILike(place.Description, pattern) ||
            (place.Address != null && EF.Functions.ILike(place.Address, pattern)) ||
            EF.Functions.ILike(place.Location.Name, pattern) ||
            EF.Functions.ILike(place.Location.Destination.Name, pattern) ||
            EF.Functions.ILike(place.PlaceType.Name, pattern) ||
            place.PlaceTags.Any(tag =>
                EF.Functions.ILike(tag.Tag.Name, pattern) ||
                EF.Functions.ILike(tag.Tag.Slug, pattern)) ||
            (place.Stay != null && EF.Functions.ILike(place.Stay.StayType.Name, pattern)));
    }

    private static IQueryable<Geotrack.Domain.Entities.Core.Event> ApplyEventTextFilter(
        IQueryable<Geotrack.Domain.Entities.Core.Event> query,
        string? rawQuery)
    {
        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            return query;
        }

        var pattern = LikePattern(rawQuery);
        return query.Where(evt =>
            EF.Functions.ILike(evt.Name, pattern) ||
            EF.Functions.ILike(evt.Description, pattern) ||
            EF.Functions.ILike(evt.Location.Name, pattern) ||
            EF.Functions.ILike(evt.Location.Destination.Name, pattern) ||
            EF.Functions.ILike(evt.EventType.Name, pattern) ||
            (evt.HostedBy != null && EF.Functions.ILike(evt.HostedBy, pattern)) ||
            evt.EventTags.Any(tag =>
                EF.Functions.ILike(tag.Tag.Name, pattern) ||
                EF.Functions.ILike(tag.Tag.Slug, pattern)));
    }

    private static GeoTrackToolSearchRequest BuildRequestFromPlan(
        GeoTrackAgentQueryPlanDto plan,
        int limit,
        string target)
    {
        var request = new GeoTrackToolSearchRequest
        {
            Query = plan.QueryText,
            Location = plan.LocationText,
            DestinationId = plan.DestinationId,
            Limit = limit,
            UpcomingOnly = true
        };

        if (target == "places")
        {
            request.Tags.AddRange(plan.Tags);
            request.Categories.AddRange(plan.Categories);
            request.PlaceTypes.AddRange(plan.PlaceTypes);
        }
        else if (target == "stays")
        {
            request.Tags.AddRange(plan.Tags);
            request.Categories.AddRange(plan.Categories);
            request.StayTypes.AddRange(plan.StayTypes);
        }
        else if (target == "events")
        {
            request.Tags.AddRange(plan.Tags);
            request.Categories.AddRange(plan.Categories);
            request.EventCategories.AddRange(plan.EventCategories);
        }

        return request;
    }

    private static GeoTrackToolSearchRequest CloneSearchRequest(GeoTrackToolSearchRequest request) => new()
    {
        Query = request.Query,
        Location = request.Location,
        DestinationId = request.DestinationId,
        Tags = request.Tags.ToList(),
        Categories = request.Categories.ToList(),
        PlaceTypes = request.PlaceTypes.ToList(),
        StayTypes = request.StayTypes.ToList(),
        EventCategories = request.EventCategories.ToList(),
        Limit = request.Limit,
        UpcomingOnly = request.UpcomingOnly
    };

    private static bool HasStructuredCriteria(GeoTrackToolSearchRequest request) =>
        request.DestinationId.HasValue ||
        !string.IsNullOrWhiteSpace(request.Location) ||
        HasStructuredFilters(request);

    private static bool HasStructuredFilters(GeoTrackToolSearchRequest request) =>
        request.Tags.Count > 0 ||
        request.Categories.Count > 0 ||
        request.PlaceTypes.Count > 0 ||
        request.StayTypes.Count > 0 ||
        request.EventCategories.Count > 0;

    private static List<string> BuildFilterList(GeoTrackAgentQueryPlanDto plan) =>
        plan.Tags
            .Concat(plan.Categories)
            .Concat(plan.PlaceTypes)
            .Concat(plan.StayTypes)
            .Concat(plan.EventCategories)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item)
            .ToList();

    private static List<string> BuildFilterList(GeoTrackToolSearchRequest request) =>
        request.Tags
            .Concat(request.Categories)
            .Concat(request.PlaceTypes)
            .Concat(request.StayTypes)
            .Concat(request.EventCategories)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item)
            .ToList();

    private static List<string> BuildFilterTerms(IEnumerable<string> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(AgentTextNormalizer.TermVariants)
            .Select(value => value.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .Take(32)
            .ToList();

    private static string? NormalizeQuery(string? query)
    {
        var normalized = query?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return null;
        }

        if (normalized.Length > MaxQueryLength)
        {
            normalized = normalized[..MaxQueryLength];
        }

        return normalized.Any(char.IsControl) ? null : normalized;
    }

    private static int NormalizeLimit(int? limit) =>
        Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);

    private static string LikePattern(string value) =>
        $"%{value.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";

    private static void ValidateBounds(GeoTrackMapFiltersRequest request)
    {
        if (!IsFinite(request.North) || !IsFinite(request.South) || !IsFinite(request.East) || !IsFinite(request.West) ||
            request.North <= request.South ||
            request.East <= request.West ||
            request.North is < -90 or > 90 ||
            request.South is < -90 or > 90 ||
            request.East is < -180 or > 180 ||
            request.West is < -180 or > 180 ||
            request.North - request.South > MaxViewportSpan ||
            request.East - request.West > MaxViewportSpan)
        {
            throw new AgentException("invalid_tool_arguments", "Map bounds are invalid or too large.", 400);
        }
    }

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    private static bool HasUsableCoordinates(double latitude, double longitude) =>
        !double.IsNaN(latitude) &&
        !double.IsNaN(longitude) &&
        !double.IsInfinity(latitude) &&
        !double.IsInfinity(longitude) &&
        latitude >= -90 &&
        latitude <= 90 &&
        longitude >= -180 &&
        longitude <= 180 &&
        (latitude != 0d || longitude != 0d);

    private static IReadOnlyList<GeoTrackToolItemDto> NormalizeImageUrls(List<GeoTrackToolItemDto> items)
    {
        foreach (var item in items)
        {
            item.ImageUrl = PublicImageUrlSafety.Normalize(item.ImageUrl);
        }

        return items;
    }

    private static GeoTrackToolItemDto? NormalizeImageUrl(GeoTrackToolItemDto? item)
    {
        if (item is not null)
        {
            item.ImageUrl = PublicImageUrlSafety.Normalize(item.ImageUrl);
        }

        return item;
    }

    private static string? Trim(string? value, int maxLength)
    {
        var cleaned = DisplayTextSafety.CleanOptional(value);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return null;
        }

        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength] + "...";
    }

    private sealed record ToolSearchCriteria(
        string? Query,
        int? DestinationId,
        List<string> TagTerms,
        List<string> PlaceTypeTerms,
        List<string> StayTypeTerms,
        List<string> EventCategoryTerms)
    {
        public bool HasAny =>
            !string.IsNullOrWhiteSpace(Query) ||
            DestinationId.HasValue ||
            TagTerms.Count > 0 ||
            PlaceTypeTerms.Count > 0 ||
            StayTypeTerms.Count > 0 ||
            EventCategoryTerms.Count > 0;
    }
}

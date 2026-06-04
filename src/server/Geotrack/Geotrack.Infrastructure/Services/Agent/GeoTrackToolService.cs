using Geotrack.Domain.Entities.Core;
using Geotrack.Application.DTOs.Agent;
using Geotrack.Application.DTOs.GeoTrackTools;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Application.Services;
using Geotrack.Infrastructure.Services;
using Geotrack.Infrastructure.Spatial;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Geotrack.Infrastructure.Services.Agent;

public class GeoTrackToolService : IGeoTrackToolService
{
    private const int DefaultLimit = 5;
    private const int MaxLimit = 10;
    private const int MaxCatalogLimit = 20;
    private const int CandidateLimit = 120;
    private const int MaxQueryLength = 100;
    private const double MaxViewportSpan = 10d;
    private static readonly GeometryFactory ViewportGeometryFactory = new(new PrecisionModel(), 4326);
    private readonly AppDbContext _db;
    private readonly IAgentQueryPlanner _planner;
    private readonly IAgentDatabaseCapabilityService _capabilities;
    private readonly IAgentTaxonomyProvider? _taxonomyProvider;

    public GeoTrackToolService(AppDbContext db, IAgentQueryPlanner planner)
        : this(db, planner, new DisabledAgentDatabaseCapabilityService(), taxonomyProvider: null)
    {
    }

    public GeoTrackToolService(
        AppDbContext db,
        IAgentQueryPlanner planner,
        IAgentDatabaseCapabilityService capabilities,
        IAgentTaxonomyProvider? taxonomyProvider)
    {
        _db = db;
        _planner = planner;
        _capabilities = capabilities;
        _taxonomyProvider = taxonomyProvider;
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
            .WherePublicPlaces()
            .Where(place => place.Stay == null);

        query = ApplyPlaceAreaFilter(query, criteria);

        if (criteria.PlaceTypeTerms.Count > 0 || criteria.TagTerms.Count > 0)
        {
            if (request.StrictCategory &&
                string.Equals(NormalizeCategoryId(request.Category), "beaches", StringComparison.Ordinal) &&
                criteria.PlaceTypeTerms.Count > 0)
            {
                query = query.Where(place => criteria.PlaceTypeTerms.Contains(place.PlaceType.Name.ToLower()));
            }
            else
            {
                query = query.Where(place =>
                    criteria.PlaceTypeTerms.Contains(place.PlaceType.Name.ToLower()) ||
                    place.PlaceTags.Any(tag =>
                        criteria.TagTerms.Contains(tag.Tag.Slug.ToLower()) ||
                        criteria.TagTerms.Contains(tag.Tag.Name.ToLower())));
            }
        }

        query = ApplyPlaceTextFilter(
            query,
            criteria.Query,
            await _capabilities.GetCapabilitiesAsync(cancellationToken));

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
                    .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
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
            .WherePublicPlaces()
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
                    .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
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
            .WherePublicPlaces()
            .Where(place => place.Stay != null);

        query = ApplyPlaceAreaFilter(query, criteria);

        if (criteria.StayTypeTerms.Count > 0 || criteria.TagTerms.Count > 0)
        {
            query = query.Where(place =>
                (place.Stay != null && criteria.StayTypeTerms.Contains(place.Stay.StayType.Name.ToLower())) ||
                place.PlaceTags.Any(tag =>
                    criteria.TagTerms.Contains(tag.Tag.Slug.ToLower()) ||
                    criteria.TagTerms.Contains(tag.Tag.Name.ToLower())));
        }

        query = ApplyPlaceTextFilter(
            query,
            criteria.Query,
            await _capabilities.GetCapabilitiesAsync(cancellationToken));

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
                    .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
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
            .WherePublicPlaces()
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
                    .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
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

        var query = _db.Events
            .AsNoTracking()
            .WherePublicEvents();
        if (request.UpcomingOnly)
        {
            query = query.Where(evt => evt.EndDate >= DateTime.UtcNow);
        }

        query = ApplyEventAreaFilter(query, criteria);

        if (criteria.EventCategoryTerms.Count > 0 || criteria.TagTerms.Count > 0)
        {
            query = query.Where(evt =>
                criteria.EventCategoryTerms.Contains(evt.EventType.Name.ToLower()) ||
                evt.EventTags.Any(tag =>
                    criteria.TagTerms.Contains(tag.Tag.Slug.ToLower()) ||
                    criteria.TagTerms.Contains(tag.Tag.Name.ToLower())));
        }

        query = ApplyEventTextFilter(
            query,
            criteria.Query,
            await _capabilities.GetCapabilitiesAsync(cancellationToken));

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
                Location = evt.Place != null
                    ? evt.Place.Name
                    : evt.Location != null ? evt.Location.Name : evt.Destination.Name,
                Destination = evt.Destination.Name,
                Category = evt.EventType.Name,
                DetailRoute = "/events/" + evt.Id,
                ImageUrl = evt.Images
                    .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                Latitude = evt.Place != null && HasUsableCoordinates(evt.Place.Latitude, evt.Place.Longitude)
                    ? evt.Place.Latitude
                    : evt.Location != null && HasUsableCoordinates(evt.Location.Latitude, evt.Location.Longitude)
                        ? evt.Location.Latitude
                        : HasUsableCoordinates(evt.Destination.Latitude, evt.Destination.Longitude) ? evt.Destination.Latitude : null,
                Longitude = evt.Place != null && HasUsableCoordinates(evt.Place.Latitude, evt.Place.Longitude)
                    ? evt.Place.Longitude
                    : evt.Location != null && HasUsableCoordinates(evt.Location.Latitude, evt.Location.Longitude)
                        ? evt.Location.Longitude
                        : HasUsableCoordinates(evt.Destination.Latitude, evt.Destination.Longitude) ? evt.Destination.Longitude : null,
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
            .WherePublicEvents()
            .Where(evt => evt.Id == id)
            .Select(evt => new GeoTrackToolItemDto
            {
                Id = evt.Id,
                EntityType = "event",
                Title = evt.Name,
                Description = Trim(evt.Description, 800),
                Location = evt.Place != null
                    ? evt.Place.Name
                    : evt.Location != null ? evt.Location.Name : evt.Destination.Name,
                Destination = evt.Destination.Name,
                Category = evt.EventType.Name,
                DetailRoute = "/events/" + evt.Id,
                ImageUrl = evt.Images
                    .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                Latitude = evt.Place != null && HasUsableCoordinates(evt.Place.Latitude, evt.Place.Longitude)
                    ? evt.Place.Latitude
                    : evt.Location != null && HasUsableCoordinates(evt.Location.Latitude, evt.Location.Longitude)
                        ? evt.Location.Latitude
                        : HasUsableCoordinates(evt.Destination.Latitude, evt.Destination.Longitude) ? evt.Destination.Latitude : null,
                Longitude = evt.Place != null && HasUsableCoordinates(evt.Place.Latitude, evt.Place.Longitude)
                    ? evt.Place.Longitude
                    : evt.Location != null && HasUsableCoordinates(evt.Location.Latitude, evt.Location.Longitude)
                        ? evt.Location.Longitude
                        : HasUsableCoordinates(evt.Destination.Latitude, evt.Destination.Longitude) ? evt.Destination.Longitude : null,
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
            .WherePublicPlaces()
            .Where(place =>
                place.Stay == null &&
                EF.Property<Point?>(place, SpatialPointFactory.CoordinatesPropertyName) != null &&
                EF.Property<Point>(place, SpatialPointFactory.CoordinatesPropertyName).Intersects(envelope))
            .CountAsync(cancellationToken);

        var stayCount = await _db.Places
            .AsNoTracking()
            .WherePublicPlaces()
            .Where(place =>
                place.Stay != null &&
                EF.Property<Point?>(place, SpatialPointFactory.CoordinatesPropertyName) != null &&
                EF.Property<Point>(place, SpatialPointFactory.CoordinatesPropertyName).Intersects(envelope))
            .CountAsync(cancellationToken);

        var eventCount = await _db.Events
            .AsNoTracking()
            .WherePublicEvents()
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

    public async Task<GeoTrackAreaResolveResponseDto> ResolveAreaAsync(
        GeoTrackAreaResolveRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = NormalizeRequiredText(request.Query, "query", MaxQueryLength);
        var matches = await ResolveAreaMatchesAsync(
            query,
            Math.Clamp(request.Limit ?? DefaultLimit, 1, MaxLimit),
            includeEntities: true,
            cancellationToken);

        return new GeoTrackAreaResolveResponseDto
        {
            Matches = matches
        };
    }

    private async Task<GeoTrackAreaMatchDto?> ResolveAreaMatchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var matches = await ResolveAreaMatchesAsync(query, 1, includeEntities: false, cancellationToken);
        return matches.FirstOrDefault();
    }

    private async Task<List<GeoTrackAreaMatchDto>> ResolveAreaMatchesAsync(
        string query,
        int limit,
        bool includeEntities,
        CancellationToken cancellationToken)
    {
        var normalized = AgentTextNormalizer.Normalize(query);
        var tokens = AgentTextNormalizer.Tokenize(query).ToHashSet(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new List<GeoTrackAreaMatchDto>();
        }

        var candidates = new List<AreaCandidate>();

        candidates.AddRange(await _db.Destinations
            .AsNoTracking()
            .WherePublicDestinations()
            .OrderBy(destination => destination.Name)
            .Take(CandidateLimit)
            .Select(destination => new AreaCandidate
            {
                Type = "destination",
                Id = destination.Id,
                Name = destination.Name,
                DisplayName = destination.Name,
                DestinationId = destination.Id,
                DestinationName = destination.Name,
                Latitude = destination.Latitude,
                Longitude = destination.Longitude
            })
            .ToListAsync(cancellationToken));

        candidates.AddRange(await _db.Locations
            .AsNoTracking()
            .WherePublicLocations()
            .OrderBy(location => location.Name)
            .Take(CandidateLimit)
            .Select(location => new AreaCandidate
            {
                Type = "location",
                Id = location.Id,
                Name = location.Name,
                DisplayName = location.Name == location.Destination.Name
                    ? location.Name
                    : location.Name + ", " + location.Destination.Name,
                ParentNames = new List<string> { location.Destination.Name },
                DestinationId = location.DestinationId,
                DestinationName = location.Destination.Name,
                Latitude = location.Latitude,
                Longitude = location.Longitude
            })
            .ToListAsync(cancellationToken));

        candidates.AddRange(await _db.Regions
            .AsNoTracking()
            .Where(region => region.Destinations.Any(destination => destination.Status == PublicVisibilityQueryExtensions.PublishedStatus))
            .OrderBy(region => region.Name)
            .Take(CandidateLimit)
            .Select(region => new AreaCandidate
            {
                Type = "region",
                Id = region.Id,
                Name = region.Name,
                DisplayName = region.Name,
                ParentNames = new List<string> { region.Territory.Name }
            })
            .ToListAsync(cancellationToken));

        candidates.AddRange(await _db.Territories
            .AsNoTracking()
            .Where(territory => territory.Regions.Any(region =>
                region.Destinations.Any(destination => destination.Status == PublicVisibilityQueryExtensions.PublishedStatus)))
            .OrderBy(territory => territory.Name)
            .Take(CandidateLimit)
            .Select(territory => new AreaCandidate
            {
                Type = "territory",
                Id = territory.Id,
                Name = territory.Name,
                DisplayName = territory.Name,
                ParentNames = new List<string> { territory.CountryCode ?? territory.CountryIso3 ?? string.Empty },
                Aliases = new List<string> { territory.CountryCode ?? string.Empty, territory.CountryIso3 ?? string.Empty }
            })
            .ToListAsync(cancellationToken));

        if (includeEntities)
        {
            candidates.AddRange(await _db.Places
                .AsNoTracking()
                .WherePublicPlaces()
                .OrderBy(place => place.Name)
                .Take(CandidateLimit)
                .Select(place => new AreaCandidate
                {
                    Type = place.Stay == null ? "place" : "stay",
                    Id = place.Id,
                    Name = place.Name,
                    DisplayName = place.Name + ", " + place.Location.Destination.Name,
                    ParentNames = new List<string> { place.Location.Name, place.Location.Destination.Name },
                    DestinationId = place.Location.DestinationId,
                    DestinationName = place.Location.Destination.Name,
                    Latitude = HasUsableCoordinates(place.Latitude, place.Longitude) ? place.Latitude : null,
                    Longitude = HasUsableCoordinates(place.Latitude, place.Longitude) ? place.Longitude : null
                })
                .ToListAsync(cancellationToken));

            candidates.AddRange(await _db.Events
                .AsNoTracking()
                .WherePublicEvents()
                .OrderBy(evt => evt.Name)
                .Take(CandidateLimit)
                .Select(evt => new AreaCandidate
                {
                    Type = "event",
                    Id = evt.Id,
                    Name = evt.Name,
                    DisplayName = evt.Name + ", " + evt.Destination.Name,
                    ParentNames = new List<string>
                    {
                        evt.Place != null
                            ? evt.Place.Name
                            : evt.Location != null ? evt.Location.Name : evt.Destination.Name,
                        evt.Destination.Name
                    },
                    DestinationId = evt.DestinationId,
                    DestinationName = evt.Destination.Name,
                    Latitude = evt.Place != null && HasUsableCoordinates(evt.Place.Latitude, evt.Place.Longitude)
                        ? evt.Place.Latitude
                        : evt.Location != null && HasUsableCoordinates(evt.Location.Latitude, evt.Location.Longitude)
                            ? evt.Location.Latitude
                            : HasUsableCoordinates(evt.Destination.Latitude, evt.Destination.Longitude) ? evt.Destination.Latitude : null,
                    Longitude = evt.Place != null && HasUsableCoordinates(evt.Place.Latitude, evt.Place.Longitude)
                        ? evt.Place.Longitude
                        : evt.Location != null && HasUsableCoordinates(evt.Location.Latitude, evt.Location.Longitude)
                            ? evt.Location.Longitude
                            : HasUsableCoordinates(evt.Destination.Latitude, evt.Destination.Longitude) ? evt.Destination.Longitude : null
                })
                .ToListAsync(cancellationToken));
        }

        return candidates
            .Select(candidate => ScoreAreaCandidate(candidate, normalized, tokens))
            .Where(match => match.Score > 0)
            .OrderByDescending(match => match.Score)
            .ThenBy(match => AreaTypeRank(match.Item.Type))
            .ThenBy(match => match.Item.Name)
            .Take(limit)
            .Select(match => ToAreaMatch(match))
            .ToList();
    }

    public async Task<GeoTrackCatalogSearchResponseDto> SearchCatalogAsync(
        GeoTrackCatalogSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var limit = NormalizeCatalogLimit(request.Limit);
        var areaType = NormalizeAreaType(request.AreaType);
        var areaId = request.AreaId is > 0 ? request.AreaId : null;
        var category = BuildCategorySpec(request.Category);
        var entityTypes = ResolveRequestedEntityTypes(request.EntityTypes, category);
        var results = new List<GeoTrackToolItemDto>();

        if (areaType is null && !areaId.HasValue && !string.IsNullOrWhiteSpace(request.Query))
        {
            var areaMatches = await ResolveAreaMatchesAsync(
                request.Query,
                Math.Min(3, limit),
                includeEntities: false,
                cancellationToken);
            results.AddRange(areaMatches.Select(AreaMatchToToolItem));

            var scopedArea = areaMatches.FirstOrDefault(match =>
                match.Confidence >= 0.6 &&
                match.Type is "destination" or "location" or "region" or "territory");
            if (scopedArea is not null)
            {
                areaType = scopedArea.Type;
                areaId = scopedArea.Id;
            }
        }

        var perTypeLimit = Math.Min(MaxLimit, limit);
        var contentQuery = category.Strict && areaId.HasValue ? null : request.Query;
        if (entityTypes.Contains("places"))
        {
            results.AddRange(await SearchPlacesAsync(BuildToolRequest(contentQuery, areaType, areaId, category, perTypeLimit, "places"), cancellationToken));
        }

        if (entityTypes.Contains("stays"))
        {
            results.AddRange(await SearchStaysAsync(BuildToolRequest(contentQuery, areaType, areaId, category, perTypeLimit, "stays"), cancellationToken));
        }

        if (entityTypes.Contains("events"))
        {
            results.AddRange(await SearchEventsAsync(BuildToolRequest(contentQuery, areaType, areaId, category, perTypeLimit, "events"), cancellationToken));
        }

        var ranked = results
            .GroupBy(item => $"{item.EntityType}:{item.Id}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => CatalogEntityRank(item.EntityType, category.Id))
            .ThenByDescending(item => CategoryMatchRank(item, category))
            .ThenByDescending(item => item.Rating ?? 0)
            .ThenByDescending(item => item.ReviewCount ?? 0)
            .ThenBy(item => item.Title)
            .Take(limit)
            .ToList();

        return new GeoTrackCatalogSearchResponseDto
        {
            Results = ranked,
            Count = ranked.Count,
            AreaType = areaType,
            AreaId = areaId,
            Category = category.Id
        };
    }

    public async Task<GeoTrackAreaContentResponseDto> SearchByAreaAsync(
        GeoTrackAreaContentRequest request,
        CancellationToken cancellationToken = default)
    {
        var areaType = NormalizeAreaType(request.AreaType)
            ?? throw new AgentException("invalid_tool_arguments", "areaType is invalid.", 400);
        if (request.AreaId <= 0)
        {
            throw new AgentException("invalid_tool_arguments", "areaId must be a positive integer.", 400);
        }

        var limit = NormalizeCatalogLimit(request.Limit);
        var category = BuildCategorySpec(request.Category);
        var entityTypes = ResolveRequestedEntityTypes(request.EntityTypes, category);
        var destinations = await LoadDestinationsInAreaAsync(areaType, request.AreaId, limit, cancellationToken);
        var locations = await LoadLocationsInAreaAsync(areaType, request.AreaId, limit, cancellationToken);

        var places = entityTypes.Contains("places")
            ? (await SearchPlacesAsync(BuildToolRequest(null, areaType, request.AreaId, category, Math.Min(MaxLimit, limit), "places"), cancellationToken)).ToList()
            : new List<GeoTrackToolItemDto>();
        var stays = entityTypes.Contains("stays")
            ? (await SearchStaysAsync(BuildToolRequest(null, areaType, request.AreaId, category, Math.Min(MaxLimit, limit), "stays"), cancellationToken)).ToList()
            : new List<GeoTrackToolItemDto>();
        var events = entityTypes.Contains("events")
            ? (await SearchEventsAsync(BuildToolRequest(null, areaType, request.AreaId, category, Math.Min(MaxLimit, limit), "events"), cancellationToken)).ToList()
            : new List<GeoTrackToolItemDto>();

        return new GeoTrackAreaContentResponseDto
        {
            Destinations = destinations,
            Locations = locations,
            Places = places,
            Stays = stays,
            Events = events,
            Counts = new GeoTrackAreaContentCountsDto
            {
                Destinations = destinations.Count,
                Locations = locations.Count,
                Places = places.Count,
                Stays = stays.Count,
                Events = events.Count
            }
        };
    }

    public async Task<GeoTrackAreaOverviewDto> GetAreaOverviewAsync(
        GeoTrackAreaOverviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var areaType = NormalizeAreaType(request.AreaType)
            ?? throw new AgentException("invalid_tool_arguments", "areaType is invalid.", 400);
        if (request.AreaId <= 0)
        {
            throw new AgentException("invalid_tool_arguments", "areaId must be a positive integer.", 400);
        }

        var limit = Math.Clamp(request.LimitPerType ?? DefaultLimit, 1, MaxLimit);
        var area = await LoadAreaByIdAsync(areaType, request.AreaId, cancellationToken);
        var generalCategory = BuildCategorySpec(null);
        var places = (await SearchPlacesAsync(BuildToolRequest(null, areaType, request.AreaId, generalCategory, limit, "places"), cancellationToken)).ToList();
        var stays = (await SearchStaysAsync(BuildToolRequest(null, areaType, request.AreaId, generalCategory, limit, "stays"), cancellationToken)).ToList();
        var events = (await SearchEventsAsync(BuildToolRequest(null, areaType, request.AreaId, generalCategory, limit, "events"), cancellationToken)).ToList();
        var categories = places
            .Concat(stays)
            .Concat(events)
            .Where(item => !string.IsNullOrWhiteSpace(item.Category))
            .GroupBy(item => item.Category!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new GeoTrackCategorySummaryDto { Name = group.Key, Count = group.Count() })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Name)
            .Take(10)
            .ToList();

        return new GeoTrackAreaOverviewDto
        {
            Area = area,
            Counts = new GeoTrackAreaContentCountsDto
            {
                Destinations = (await LoadDestinationsInAreaAsync(areaType, request.AreaId, limit, cancellationToken)).Count,
                Locations = (await LoadLocationsInAreaAsync(areaType, request.AreaId, limit, cancellationToken)).Count,
                Places = places.Count,
                Stays = stays.Count,
                Events = events.Count
            },
            TopCategories = categories,
            TopPlaces = places,
            TopStays = stays,
            TopEvents = events,
            SuggestedFollowUpCategories = BuildSuggestedFollowUps(places, stays, events)
        };
    }

    public async Task<GeoTrackCategoryOptionsResponseDto> GetCategoryOptionsAsync(
        GeoTrackCategoryOptionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var groups = _taxonomyProvider is null
            ? BuildCategoryGroups()
            : await BuildCategoryGroupsAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.CategoryGroup))
        {
            groups = groups
                .Where(group => string.Equals(group.Id, NormalizeCategoryId(request.CategoryGroup), StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return new GeoTrackCategoryOptionsResponseDto
        {
            CategoryGroups = groups
        };
    }

    public async Task<GeoTrackNearbySearchResponseDto> FindNearbyPublicContentAsync(
        GeoTrackNearbySearchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!HasUsableCoordinates(request.Latitude, request.Longitude))
        {
            throw new AgentException("invalid_tool_arguments", "Coordinates are invalid.", 400);
        }

        var limit = NormalizeCatalogLimit(request.Limit);
        var radiusKm = NormalizeRadiusKm(request.RadiusKm);
        var radiusMeters = radiusKm * 1000d;
        var userPoint = SpatialPointFactory.Create(request.Latitude, request.Longitude);
        var category = BuildCategorySpec(request.Category);
        var entityTypes = ResolveRequestedEntityTypes(request.EntityTypes, category);
        var results = new List<GeoTrackToolItemDto>();

        if (entityTypes.Contains("places"))
        {
            var query = _db.Places
                .AsNoTracking()
                .WherePublicPlaces()
                .Where(place => place.Stay == null)
                .Where(place => EF.Property<Point?>(place, SpatialPointFactory.CoordinatesPropertyName) != null);

            query = ApplyPlaceCategoryFilter(query, category, includeStays: false);

            results.AddRange(await query
                .Select(place => new
                {
                    Item = new GeoTrackToolItemDto
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
                            .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
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
                    },
                    DistanceMeters = EF.Property<Point>(place, SpatialPointFactory.CoordinatesPropertyName).Distance(userPoint)
                })
                .Where(row => row.DistanceMeters <= radiusMeters)
                .OrderBy(row => row.DistanceMeters)
                .Take(limit)
                .ToListAsync(cancellationToken)
                .ContinueWith(task => task.Result.Select(row =>
                {
                    row.Item.DistanceKm = Math.Round(row.DistanceMeters / 1000d, 2);
                    return row.Item;
                }), cancellationToken));
        }

        if (entityTypes.Contains("stays"))
        {
            var query = _db.Places
                .AsNoTracking()
                .WherePublicPlaces()
                .Where(place => place.Stay != null)
                .Where(place => EF.Property<Point?>(place, SpatialPointFactory.CoordinatesPropertyName) != null);

            query = ApplyPlaceCategoryFilter(query, category, includeStays: true);

            results.AddRange(await query
                .Select(place => new
                {
                    Item = new GeoTrackToolItemDto
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
                            .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
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
                    },
                    DistanceMeters = EF.Property<Point>(place, SpatialPointFactory.CoordinatesPropertyName).Distance(userPoint)
                })
                .Where(row => row.DistanceMeters <= radiusMeters)
                .OrderBy(row => row.DistanceMeters)
                .Take(limit)
                .ToListAsync(cancellationToken)
                .ContinueWith(task => task.Result.Select(row =>
                {
                    row.Item.DistanceKm = Math.Round(row.DistanceMeters / 1000d, 2);
                    return row.Item;
                }), cancellationToken));
        }

        if (entityTypes.Contains("events"))
        {
            var query = _db.Events
                .AsNoTracking()
                .WherePublicEvents()
                .Where(evt => evt.EndDate >= DateTime.UtcNow)
                .Where(evt => EF.Property<Point?>(evt, SpatialPointFactory.CoordinatesPropertyName) != null);

            query = ApplyEventCategoryFilter(query, category);

            results.AddRange(await query
                .Select(evt => new
                {
                    Item = new GeoTrackToolItemDto
                    {
                        Id = evt.Id,
                        EntityType = "event",
                        Title = evt.Name,
                        Description = Trim(evt.Description, 280),
                        Location = evt.Place != null
                            ? evt.Place.Name
                            : evt.Location != null ? evt.Location.Name : evt.Destination.Name,
                        Destination = evt.Destination.Name,
                        Category = evt.EventType.Name,
                        DetailRoute = "/events/" + evt.Id,
                        ImageUrl = evt.Images
                            .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
                            .ThenBy(image => image.SortOrder)
                            .ThenBy(image => image.CreatedAt)
                            .ThenBy(image => image.Id)
                            .Select(image => image.Url)
                            .FirstOrDefault(),
                        Latitude = evt.Place != null && HasUsableCoordinates(evt.Place.Latitude, evt.Place.Longitude)
                            ? evt.Place.Latitude
                            : evt.Location != null && HasUsableCoordinates(evt.Location.Latitude, evt.Location.Longitude)
                                ? evt.Location.Latitude
                                : HasUsableCoordinates(evt.Destination.Latitude, evt.Destination.Longitude) ? evt.Destination.Latitude : null,
                        Longitude = evt.Place != null && HasUsableCoordinates(evt.Place.Latitude, evt.Place.Longitude)
                            ? evt.Place.Longitude
                            : evt.Location != null && HasUsableCoordinates(evt.Location.Latitude, evt.Location.Longitude)
                                ? evt.Location.Longitude
                                : HasUsableCoordinates(evt.Destination.Latitude, evt.Destination.Longitude) ? evt.Destination.Longitude : null,
                        StartsAt = evt.StartDate,
                        EndsAt = evt.EndDate,
                        Tags = evt.EventTags
                            .OrderBy(tag => tag.Tag.Name)
                            .Select(tag => tag.Tag.Name)
                            .Take(8)
                            .ToList()
                    },
                    DistanceMeters = EF.Property<Point>(evt, SpatialPointFactory.CoordinatesPropertyName).Distance(userPoint)
                })
                .Where(row => row.DistanceMeters <= radiusMeters)
                .OrderBy(row => row.DistanceMeters)
                .Take(limit)
                .ToListAsync(cancellationToken)
                .ContinueWith(task => task.Result.Select(row =>
                {
                    row.Item.DistanceKm = Math.Round(row.DistanceMeters / 1000d, 2);
                    return row.Item;
                }), cancellationToken));
        }

        var ranked = NormalizeImageUrls(results)
            .GroupBy(item => $"{item.EntityType}:{item.Id}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => CatalogEntityRank(item.EntityType, category.Id))
            .ThenByDescending(item => CategoryMatchRank(item, category))
            .ThenBy(item => item.DistanceKm ?? double.MaxValue)
            .ThenByDescending(item => item.Rating ?? 0)
            .ThenByDescending(item => item.ReviewCount ?? 0)
            .ThenBy(item => item.Title)
            .Take(limit)
            .ToList();

        return new GeoTrackNearbySearchResponseDto
        {
            Results = ranked,
            Count = ranked.Count,
            RadiusKm = radiusKm,
            Category = category.Id
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

        if (plan.UsesUserLocation && HasUsableCoordinates(contextOptions?.Latitude ?? 0d, contextOptions?.Longitude ?? 0d))
        {
            var nearby = await FindNearbyPublicContentAsync(
                new GeoTrackNearbySearchRequest
                {
                    Latitude = contextOptions!.Latitude!.Value,
                    Longitude = contextOptions.Longitude!.Value,
                    RadiusKm = 25,
                    Category = plan.Category,
                    EntityTypes = plan.EntityTypes.ToList(),
                    Limit = limit
                },
                cancellationToken);

            context.Searches.Add(new GeoTrackToolSearchTraceDto
            {
                Tool = "geotrack_find_nearby_public_content",
                SearchType = "nearby",
                Filters = BuildFilterList(plan),
                ResultCount = nearby.Count
            });
            context.ToolsUsed = ["geotrack_find_nearby_public_content"];
            context.Places = nearby.Results.Where(item => item.EntityType == "place").ToList();
            context.Stays = nearby.Results.Where(item => item.EntityType == "stay").ToList();
            context.Events = nearby.Results.Where(item => item.EntityType == "event").ToList();
            context.SearchAttempted = true;
            context.ResultCount = nearby.Count;
            context.NoResults = nearby.Count == 0;
            return context;
        }

        var searchAll = plan.EntityTypes.Count == 0 || plan.EntityTypes.Contains("all");
        var shouldSearchPlaces = searchAll ||
            plan.EntityTypes.Contains("places") ||
            plan.PlaceTypes.Count > 0 ||
            plan.Categories.Any(category => category is "beach" or "restaurant" or "cafe" or "landmark") ||
            plan.Category is "restaurants" or "beaches" or "attractions";
        var shouldSearchStays = searchAll || plan.EntityTypes.Contains("stays") || plan.Category == "stays";
        var shouldSearchEvents = searchAll ||
            plan.EntityTypes.Contains("events") ||
            plan.EventCategories.Count > 0 ||
            plan.Categories.Contains("festival") ||
            plan.Category == "events";

        if (shouldSearchPlaces)
        {
            var places = await SearchWithFallbacksAsync(
                "geotrack_search_places",
                "specific",
                BuildRequestFromPlan(plan, limit, "places"),
                context,
                SearchPlacesAsync,
                allowBroadFallback: !plan.StrictCategoryMatch,
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
                allowBroadFallback: !plan.StrictCategoryMatch,
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
                allowBroadFallback: !plan.StrictCategoryMatch,
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
        bool allowBroadFallback,
        CancellationToken cancellationToken)
    {
        var results = await RunSearchAsync(toolName, searchType, request, context, search, cancellationToken);
        if (results.Count > 0 || !HasAreaScope(request) || !allowBroadFallback)
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
        expanded.AreaType ??= plan.AreaType;
        expanded.AreaId ??= plan.AreaId;
        if (IsFilterCategory(plan.Category))
        {
            expanded.Category ??= plan.Category;
        }
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
        var areaType = NormalizeAreaType(request.AreaType);
        var areaId = request.AreaId is > 0 ? request.AreaId : null;

        if (areaType is null && areaId is null && destinationId.HasValue)
        {
            areaType = "destination";
            areaId = destinationId;
        }

        if ((areaType is null || !areaId.HasValue) && !string.IsNullOrWhiteSpace(request.Location))
        {
            var resolvedArea = await ResolveAreaMatchAsync(request.Location, cancellationToken);
            if (resolvedArea is not null)
            {
                areaType = resolvedArea.Type;
                areaId = resolvedArea.Id;
                destinationId ??= resolvedArea.DestinationId;
            }
        }

        var query = NormalizeQuery(request.Query);
        var categories = request.Categories
            .Concat(IsFilterCategory(request.Category) ? new[] { request.Category } : [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToList();
        var tagTerms = BuildFilterTerms(request.Tags.Concat(categories));
        var placeTypeTerms = BuildFilterTerms(request.PlaceTypes.Concat(categories));
        var stayTypeTerms = BuildFilterTerms(request.StayTypes.Concat(categories));
        var eventCategoryTerms = BuildFilterTerms(request.EventCategories.Concat(categories));

        return new ToolSearchCriteria(
            query,
            destinationId,
            areaType,
            areaId,
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
            .WherePublicDestinations()
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
            .WherePublicLocations()
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
        return NameMatchesTokens(name, tokens)
            ? 160 + nameTokens.Count * 20
            : 0;
    }

    private static IQueryable<Geotrack.Domain.Entities.Core.Place> ApplyPlaceAreaFilter(
        IQueryable<Geotrack.Domain.Entities.Core.Place> query,
        ToolSearchCriteria criteria)
    {
        if (!string.IsNullOrWhiteSpace(criteria.AreaType) && criteria.AreaId.HasValue)
        {
            var areaId = criteria.AreaId.Value;
            return criteria.AreaType switch
            {
                "destination" => query.Where(place => place.Location.DestinationId == areaId),
                "location" => query.Where(place => place.LocationId == areaId),
                "region" => query.Where(place => place.Location.Destination.RegionId == areaId),
                "territory" => query.Where(place => place.Location.Destination.Region.TerritoryId == areaId),
                "place" => query.Where(place => place.Id == areaId),
                _ => query
            };
        }

        return criteria.DestinationId.HasValue
            ? query.Where(place => place.Location.DestinationId == criteria.DestinationId.Value)
            : query;
    }

    private static IQueryable<Geotrack.Domain.Entities.Core.Event> ApplyEventAreaFilter(
        IQueryable<Geotrack.Domain.Entities.Core.Event> query,
        ToolSearchCriteria criteria)
    {
        if (!string.IsNullOrWhiteSpace(criteria.AreaType) && criteria.AreaId.HasValue)
        {
            var areaId = criteria.AreaId.Value;
            return criteria.AreaType switch
            {
                "destination" => query.Where(evt => evt.DestinationId == areaId),
                "location" => query.Where(evt =>
                    evt.LocationId == areaId ||
                    (evt.Place != null && evt.Place.LocationId == areaId)),
                "region" => query.Where(evt => evt.Destination.RegionId == areaId),
                "territory" => query.Where(evt => evt.Destination.Region.TerritoryId == areaId),
                "place" => query.Where(evt => evt.PlaceId == areaId),
                "event" => query.Where(evt => evt.Id == areaId),
                _ => query
            };
        }

        return criteria.DestinationId.HasValue
            ? query.Where(evt => evt.DestinationId == criteria.DestinationId.Value)
            : query;
    }

    private static IQueryable<Geotrack.Domain.Entities.Core.Place> ApplyPlaceTextFilter(
        IQueryable<Geotrack.Domain.Entities.Core.Place> query,
        string? rawQuery,
        AgentDatabaseCapabilitiesDto capabilities)
    {
        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            return query;
        }

        var pattern = LikePattern(rawQuery);
        var normalized = AgentTextNormalizer.Normalize(rawQuery);
        var normalizedPattern = LikePattern(normalized);
        if (capabilities.SupportsPgTrgm && !string.IsNullOrWhiteSpace(normalized))
        {
            return capabilities.SupportsUnaccent
                ? query.Where(place =>
                    EF.Functions.TrigramsSimilarity(place.SearchText, normalized) >= 0.12d ||
                    EF.Functions.TrigramsWordSimilarity(place.SearchText, normalized) >= 0.45d ||
                    EF.Functions.ILike(place.SearchText, normalizedPattern) ||
                    EF.Functions.ILike(AgentPostgresDbFunctions.Unaccent(place.Name), AgentPostgresDbFunctions.Unaccent(pattern)) ||
                    (place.Subtitle != null && EF.Functions.ILike(AgentPostgresDbFunctions.Unaccent(place.Subtitle), AgentPostgresDbFunctions.Unaccent(pattern))) ||
                    EF.Functions.ILike(AgentPostgresDbFunctions.Unaccent(place.Description), AgentPostgresDbFunctions.Unaccent(pattern)) ||
                    (place.Address != null && EF.Functions.ILike(AgentPostgresDbFunctions.Unaccent(place.Address), AgentPostgresDbFunctions.Unaccent(pattern))) ||
                    EF.Functions.ILike(AgentPostgresDbFunctions.Unaccent(place.Location.Name), AgentPostgresDbFunctions.Unaccent(pattern)) ||
                    EF.Functions.ILike(AgentPostgresDbFunctions.Unaccent(place.Location.Destination.Name), AgentPostgresDbFunctions.Unaccent(pattern)) ||
                    EF.Functions.ILike(AgentPostgresDbFunctions.Unaccent(place.PlaceType.Name), AgentPostgresDbFunctions.Unaccent(pattern)) ||
                    place.PlaceTags.Any(tag =>
                        EF.Functions.ILike(AgentPostgresDbFunctions.Unaccent(tag.Tag.Name), AgentPostgresDbFunctions.Unaccent(pattern)) ||
                        EF.Functions.ILike(tag.Tag.Slug, normalizedPattern)) ||
                    (place.Stay != null && EF.Functions.ILike(AgentPostgresDbFunctions.Unaccent(place.Stay.StayType.Name), AgentPostgresDbFunctions.Unaccent(pattern))))
                : query.Where(place =>
                    EF.Functions.TrigramsSimilarity(place.SearchText, normalized) >= 0.12d ||
                    EF.Functions.TrigramsWordSimilarity(place.SearchText, normalized) >= 0.45d ||
                    EF.Functions.ILike(place.SearchText, normalizedPattern) ||
                    EF.Functions.ILike(place.Name, pattern) ||
                    (place.Subtitle != null && EF.Functions.ILike(place.Subtitle, pattern)) ||
                    EF.Functions.ILike(place.Description, pattern) ||
                    (place.Address != null && EF.Functions.ILike(place.Address, pattern)) ||
                    EF.Functions.ILike(place.Location.Name, pattern) ||
                    EF.Functions.ILike(place.Location.Destination.Name, pattern) ||
                    EF.Functions.ILike(place.PlaceType.Name, pattern) ||
                    place.PlaceTags.Any(tag =>
                        EF.Functions.ILike(tag.Tag.Name, pattern) ||
                        EF.Functions.ILike(tag.Tag.Slug, normalizedPattern)) ||
                    (place.Stay != null && EF.Functions.ILike(place.Stay.StayType.Name, pattern)));
        }

        return query.Where(place =>
            EF.Functions.ILike(place.SearchText, normalizedPattern) ||
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

    private static IQueryable<Geotrack.Domain.Entities.Core.Place> ApplyPlaceCategoryFilter(
        IQueryable<Geotrack.Domain.Entities.Core.Place> query,
        CategorySpec category,
        bool includeStays)
    {
        if (!category.Strict)
        {
            return query;
        }

        var categories = category.Categories;
        var tagTerms = BuildFilterTerms(category.Tags.Concat(categories));
        var placeTypeTerms = BuildFilterTerms(category.PlaceTypes.Concat(categories));
        var stayTypeTerms = BuildFilterTerms(category.StayTypes.Concat(categories));

        if (includeStays)
        {
            if (stayTypeTerms.Count == 0 && tagTerms.Count == 0)
            {
                return query.Where(_ => false);
            }

            return query.Where(place =>
                (place.Stay != null && stayTypeTerms.Contains(place.Stay.StayType.Name.ToLower())) ||
                place.PlaceTags.Any(tag =>
                    tagTerms.Contains(tag.Tag.Slug.ToLower()) ||
                    tagTerms.Contains(tag.Tag.Name.ToLower())));
        }

        if (placeTypeTerms.Count == 0 && tagTerms.Count == 0)
        {
            return query.Where(_ => false);
        }

        if (category.Id == "beaches" && placeTypeTerms.Count > 0)
        {
            return query.Where(place => placeTypeTerms.Contains(place.PlaceType.Name.ToLower()));
        }

        return query.Where(place =>
            placeTypeTerms.Contains(place.PlaceType.Name.ToLower()) ||
            place.PlaceTags.Any(tag =>
                tagTerms.Contains(tag.Tag.Slug.ToLower()) ||
                tagTerms.Contains(tag.Tag.Name.ToLower())));
    }

    private static IQueryable<Geotrack.Domain.Entities.Core.Event> ApplyEventTextFilter(
        IQueryable<Geotrack.Domain.Entities.Core.Event> query,
        string? rawQuery,
        AgentDatabaseCapabilitiesDto capabilities)
    {
        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            return query;
        }

        var pattern = LikePattern(rawQuery);
        var normalized = AgentTextNormalizer.Normalize(rawQuery);
        var normalizedPattern = LikePattern(normalized);
        if (capabilities.SupportsPgTrgm && !string.IsNullOrWhiteSpace(normalized))
        {
            return capabilities.SupportsUnaccent
                ? query.Where(evt =>
                    EF.Functions.TrigramsSimilarity(evt.SearchText, normalized) >= 0.12d ||
                    EF.Functions.TrigramsWordSimilarity(evt.SearchText, normalized) >= 0.45d ||
                    EF.Functions.ILike(evt.SearchText, normalizedPattern) ||
                    EF.Functions.ILike(AgentPostgresDbFunctions.Unaccent(evt.Name), AgentPostgresDbFunctions.Unaccent(pattern)) ||
                    EF.Functions.ILike(AgentPostgresDbFunctions.Unaccent(evt.Description), AgentPostgresDbFunctions.Unaccent(pattern)) ||
                    EF.Functions.ILike(AgentPostgresDbFunctions.Unaccent(evt.Destination.Name), AgentPostgresDbFunctions.Unaccent(pattern)) ||
                    (evt.Location != null && EF.Functions.ILike(AgentPostgresDbFunctions.Unaccent(evt.Location.Name), AgentPostgresDbFunctions.Unaccent(pattern))) ||
                    (evt.Place != null && EF.Functions.ILike(AgentPostgresDbFunctions.Unaccent(evt.Place.Name), AgentPostgresDbFunctions.Unaccent(pattern))) ||
                    EF.Functions.ILike(AgentPostgresDbFunctions.Unaccent(evt.EventType.Name), AgentPostgresDbFunctions.Unaccent(pattern)) ||
                    (evt.HostedBy != null && EF.Functions.ILike(AgentPostgresDbFunctions.Unaccent(evt.HostedBy), AgentPostgresDbFunctions.Unaccent(pattern))) ||
                    evt.EventTags.Any(tag =>
                        EF.Functions.ILike(AgentPostgresDbFunctions.Unaccent(tag.Tag.Name), AgentPostgresDbFunctions.Unaccent(pattern)) ||
                        EF.Functions.ILike(tag.Tag.Slug, normalizedPattern)))
                : query.Where(evt =>
                    EF.Functions.TrigramsSimilarity(evt.SearchText, normalized) >= 0.12d ||
                    EF.Functions.TrigramsWordSimilarity(evt.SearchText, normalized) >= 0.45d ||
                    EF.Functions.ILike(evt.SearchText, normalizedPattern) ||
                    EF.Functions.ILike(evt.Name, pattern) ||
                    EF.Functions.ILike(evt.Description, pattern) ||
                    EF.Functions.ILike(evt.Destination.Name, pattern) ||
                    (evt.Location != null && EF.Functions.ILike(evt.Location.Name, pattern)) ||
                    (evt.Place != null && EF.Functions.ILike(evt.Place.Name, pattern)) ||
                    EF.Functions.ILike(evt.EventType.Name, pattern) ||
                    (evt.HostedBy != null && EF.Functions.ILike(evt.HostedBy, pattern)) ||
                    evt.EventTags.Any(tag =>
                        EF.Functions.ILike(tag.Tag.Name, pattern) ||
                        EF.Functions.ILike(tag.Tag.Slug, normalizedPattern)));
        }

        return query.Where(evt =>
            EF.Functions.ILike(evt.SearchText, normalizedPattern) ||
            EF.Functions.ILike(evt.Name, pattern) ||
            EF.Functions.ILike(evt.Description, pattern) ||
            EF.Functions.ILike(evt.Destination.Name, pattern) ||
            (evt.Location != null && EF.Functions.ILike(evt.Location.Name, pattern)) ||
            (evt.Place != null && EF.Functions.ILike(evt.Place.Name, pattern)) ||
            EF.Functions.ILike(evt.EventType.Name, pattern) ||
            (evt.HostedBy != null && EF.Functions.ILike(evt.HostedBy, pattern)) ||
            evt.EventTags.Any(tag =>
                EF.Functions.ILike(tag.Tag.Name, pattern) ||
                EF.Functions.ILike(tag.Tag.Slug, pattern)));
    }

    private static IQueryable<Geotrack.Domain.Entities.Core.Event> ApplyEventCategoryFilter(
        IQueryable<Geotrack.Domain.Entities.Core.Event> query,
        CategorySpec category)
    {
        if (!category.Strict || category.Id == "events")
        {
            return query;
        }

        var categories = category.Categories;
        var tagTerms = BuildFilterTerms(category.Tags.Concat(categories));
        var eventCategoryTerms = BuildFilterTerms(category.EventCategories.Concat(categories));

        if (eventCategoryTerms.Count == 0 && tagTerms.Count == 0)
        {
            return query.Where(_ => false);
        }

        return query.Where(evt =>
            eventCategoryTerms.Contains(evt.EventType.Name.ToLower()) ||
            evt.EventTags.Any(tag =>
                tagTerms.Contains(tag.Tag.Slug.ToLower()) ||
                tagTerms.Contains(tag.Tag.Name.ToLower())));
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
            AreaType = plan.AreaType,
            AreaId = plan.AreaId,
            Category = plan.Category,
            EntityTypes = plan.EntityTypes.ToList(),
            Limit = limit,
            UpcomingOnly = true,
            StrictCategory = plan.StrictCategoryMatch
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

    private static GeoTrackToolSearchRequest BuildToolRequest(
        string? query,
        string? areaType,
        int? areaId,
        CategorySpec category,
        int limit,
        string target)
    {
        var request = new GeoTrackToolSearchRequest
        {
            Query = NormalizeQuery(query),
            AreaType = areaType,
            AreaId = areaId,
            Category = category.Id,
            EntityTypes = category.EntityTypes.ToList(),
            Limit = limit,
            UpcomingOnly = true,
            StrictCategory = category.Strict
        };

        if (target == "places")
        {
            request.Tags.AddRange(category.Tags);
            request.Categories.AddRange(category.Categories);
            request.PlaceTypes.AddRange(category.PlaceTypes);
        }
        else if (target == "stays")
        {
            request.Tags.AddRange(category.Tags);
            request.Categories.AddRange(category.Categories);
            request.StayTypes.AddRange(category.StayTypes);
        }
        else if (target == "events")
        {
            request.Tags.AddRange(category.Tags);
            request.Categories.AddRange(category.Categories);
            request.EventCategories.AddRange(category.EventCategories);
        }

        return request;
    }

    private static CategorySpec BuildCategorySpec(string? category)
    {
        var id = NormalizeCategoryId(category);
        return id switch
        {
            "restaurants" => new CategorySpec(
                id,
                Strict: true,
                EntityTypes: ["places"],
                Categories: ["restaurant", "cafe"],
                Tags: ["food-drink", "fine-dining", "nightlife", "food-market", "wine-bar"],
                PlaceTypes: ["Restaurant", "Cafe", "Bar"],
                StayTypes: [],
                EventCategories: []),
            "beaches" => new CategorySpec(
                id,
                Strict: true,
                EntityTypes: ["places"],
                Categories: ["beach"],
                Tags: ["beaches"],
                PlaceTypes: ["Beach"],
                StayTypes: [],
                EventCategories: []),
            "stays" => new CategorySpec(
                id,
                Strict: true,
                EntityTypes: ["stays"],
                Categories: [],
                Tags: [],
                PlaceTypes: [],
                StayTypes: ["Hotel", "Apartment", "Villa"],
                EventCategories: []),
            "events" => new CategorySpec(
                id,
                Strict: true,
                EntityTypes: ["events"],
                Categories: [],
                Tags: [],
                PlaceTypes: [],
                StayTypes: [],
                EventCategories: []),
            "attractions" => new CategorySpec(
                id,
                Strict: true,
                EntityTypes: ["places"],
                Categories: ["landmark"],
                Tags: ["landmarks", "historic-sites", "culture", "history", "nature", "hiking", "viewpoints"],
                PlaceTypes: ["Landmark", "Museum", "Viewpoint", "Nature"],
                StayTypes: [],
                EventCategories: []),
            _ => new CategorySpec(
                "general",
                Strict: false,
                EntityTypes: ["places", "stays", "events"],
                Categories: [],
                Tags: [],
                PlaceTypes: [],
                StayTypes: [],
                EventCategories: [])
        };
    }

    private static string NormalizeCategoryId(string? category)
    {
        var normalized = AgentTextNormalizer.Normalize(category);
        return normalized switch
        {
            "food drink" or "restaurant" or "restaurants" or "restoran" or "restorani" or "restorana" or "food" or "hrana" or "dining" or "bar" or "bars" or "cafe" or "cafes" or "kafic" or "kafici" => "restaurants",
            "beach" or "beaches" or "plaza" or "plaze" or "more" or "kupanje" => "beaches",
            "stay" or "stays" or "hotel" or "hotels" or "hoteli" or "smestaj" or "smjestaj" or "accommodation" or "lodging" or "apartman" => "stays",
            "event" or "events" or "dogadaj" or "dogadjaj" or "manifestacija" or "koncert" or "festival" => "events",
            "sights attractions" or "attraction" or "attractions" or "landmark" or "landmarks" or "znamenitost" or "znamenitosti" or "muzej" or "museum" or "nature" or "hiking" => "attractions",
            "general discovery" or "activity" or "activities" or "aktivnost" or "aktivnosti" => "general",
            _ => "general"
        };
    }

    private static HashSet<string> ResolveRequestedEntityTypes(
        IEnumerable<string> requested,
        CategorySpec category)
    {
        var result = requested
            .Select(NormalizeEntityTypeForSearch)
            .Where(value => value is not null)
            .Select(value => value!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (result.Contains("all"))
        {
            result.Clear();
        }

        if (result.Count == 0)
        {
            result.UnionWith(category.EntityTypes);
        }

        if (result.Count == 0)
        {
            result.UnionWith(["places", "stays", "events"]);
        }

        return result;
    }

    private static string? NormalizeEntityTypeForSearch(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "all" => "all",
            "place" or "places" or "destination" or "destinations" or "location" or "locations" => "places",
            "stay" or "stays" or "hotel" or "hotels" => "stays",
            "event" or "events" => "events",
            _ => null
        };
    }

    private static GeoTrackToolItemDto AreaMatchToToolItem(GeoTrackAreaMatchDto match) => new()
    {
        Id = match.Id,
        EntityType = match.Type,
        Title = match.DisplayName,
        Description = match.Reason,
        Location = match.ParentNames.FirstOrDefault(),
        Destination = match.DestinationName,
        Category = match.Type,
        DetailRoute = match.Type switch
        {
            "destination" => "/destinations/" + match.Id,
            "location" => "/locations/" + match.Id,
            "place" => "/places/" + match.Id,
            "stay" => "/stays/" + match.Id,
            "event" => "/events/" + match.Id,
            _ => string.Empty
        },
        Latitude = match.Latitude,
        Longitude = match.Longitude
    };

    private static int CatalogEntityRank(string entityType, string category) =>
        category switch
        {
            "stays" => entityType.Equals("stay", StringComparison.OrdinalIgnoreCase) ? 0 : 5,
            "events" => entityType.Equals("event", StringComparison.OrdinalIgnoreCase) ? 0 : 5,
            "restaurants" or "beaches" or "attractions" => entityType.Equals("place", StringComparison.OrdinalIgnoreCase) ? 0 : 5,
            _ => entityType.ToLowerInvariant() switch
            {
                "destination" => 0,
                "location" => 1,
                "place" => 2,
                "stay" => 3,
                "event" => 4,
                _ => 9
            }
        };

    private static int CategoryMatchRank(GeoTrackToolItemDto item, CategorySpec category)
    {
        if (!category.Strict)
        {
            return 0;
        }

        var normalizedCategory = AgentTextNormalizer.Normalize(item.Category);
        if (category.PlaceTypes.Concat(category.StayTypes).Concat(category.EventCategories)
            .Select(AgentTextNormalizer.Normalize)
            .Contains(normalizedCategory, StringComparer.Ordinal))
        {
            return 100;
        }

        var tagTerms = category.Tags.Select(AgentTextNormalizer.Normalize).ToHashSet(StringComparer.Ordinal);
        return item.Tags.Select(AgentTextNormalizer.Normalize).Any(tagTerms.Contains) ? 80 : 0;
    }

    private async Task<List<GeoTrackAreaMatchDto>> LoadDestinationsInAreaAsync(
        string areaType,
        int areaId,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = _db.Destinations
            .AsNoTracking()
            .WherePublicDestinations();

        query = areaType switch
        {
            "destination" => query.Where(destination => destination.Id == areaId),
            "location" => query.Where(destination => destination.Locations.Any(location => location.Id == areaId)),
            "region" => query.Where(destination => destination.RegionId == areaId),
            "territory" => query.Where(destination => destination.Region.TerritoryId == areaId),
            "place" => query.Where(destination => destination.Locations.Any(location => location.Places.Any(place => place.Id == areaId))),
            "event" => query.Where(destination => destination.Events.Any(evt => evt.Id == areaId)),
            _ => query.Where(_ => false)
        };

        var candidates = await query
            .OrderBy(destination => destination.Name)
            .Take(Math.Clamp(limit, 1, MaxCatalogLimit))
            .Select(destination => new AreaCandidate
            {
                Type = "destination",
                Id = destination.Id,
                Name = destination.Name,
                DisplayName = destination.Name,
                DestinationId = destination.Id,
                DestinationName = destination.Name,
                Latitude = destination.Latitude,
                Longitude = destination.Longitude
            })
            .ToListAsync(cancellationToken);

        return candidates
            .Select(candidate => ToAreaMatch(new ScoredAreaCandidate(candidate, 500, "area_scope")))
            .ToList();
    }

    private async Task<List<GeoTrackAreaMatchDto>> LoadLocationsInAreaAsync(
        string areaType,
        int areaId,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = _db.Locations
            .AsNoTracking()
            .WherePublicLocations();

        query = areaType switch
        {
            "destination" => query.Where(location => location.DestinationId == areaId),
            "location" => query.Where(location => location.Id == areaId),
            "region" => query.Where(location => location.Destination.RegionId == areaId),
            "territory" => query.Where(location => location.Destination.Region.TerritoryId == areaId),
            "place" => query.Where(location => location.Places.Any(place => place.Id == areaId)),
            "event" => query.Where(location => location.Events.Any(evt => evt.Id == areaId) || location.Places.Any(place => place.Events.Any(evt => evt.Id == areaId))),
            _ => query.Where(_ => false)
        };

        var candidates = await query
            .OrderBy(location => location.Name)
            .Take(Math.Clamp(limit, 1, MaxCatalogLimit))
            .Select(location => new AreaCandidate
            {
                Type = "location",
                Id = location.Id,
                Name = location.Name,
                DisplayName = location.Name == location.Destination.Name
                    ? location.Name
                    : location.Name + ", " + location.Destination.Name,
                ParentNames = new List<string> { location.Destination.Name },
                DestinationId = location.DestinationId,
                DestinationName = location.Destination.Name,
                Latitude = location.Latitude,
                Longitude = location.Longitude
            })
            .ToListAsync(cancellationToken);

        return candidates
            .Select(candidate => ToAreaMatch(new ScoredAreaCandidate(candidate, 500, "area_scope")))
            .ToList();
    }

    private async Task<GeoTrackAreaMatchDto?> LoadAreaByIdAsync(
        string areaType,
        int areaId,
        CancellationToken cancellationToken)
    {
        if (areaType == "destination")
        {
            return (await LoadDestinationsInAreaAsync(areaType, areaId, 1, cancellationToken)).FirstOrDefault();
        }

        if (areaType == "location")
        {
            return (await LoadLocationsInAreaAsync(areaType, areaId, 1, cancellationToken)).FirstOrDefault();
        }

        if (areaType == "region")
        {
            var region = await _db.Regions
                .AsNoTracking()
                .Where(item => item.Id == areaId &&
                    item.Destinations.Any(destination => destination.Status == PublicVisibilityQueryExtensions.PublishedStatus))
                .Select(item => new AreaCandidate
                {
                    Type = "region",
                    Id = item.Id,
                    Name = item.Name,
                    DisplayName = item.Name,
                    ParentNames = new List<string> { item.Territory.Name }
                })
                .SingleOrDefaultAsync(cancellationToken);
            return region is null ? null : ToAreaMatch(new ScoredAreaCandidate(region, 500, "area_scope"));
        }

        if (areaType == "territory")
        {
            var territory = await _db.Territories
                .AsNoTracking()
                .Where(item => item.Id == areaId &&
                    item.Regions.Any(region =>
                        region.Destinations.Any(destination => destination.Status == PublicVisibilityQueryExtensions.PublishedStatus)))
                .Select(item => new AreaCandidate
                {
                    Type = "territory",
                    Id = item.Id,
                    Name = item.Name,
                    DisplayName = item.Name,
                    ParentNames = new List<string> { item.CountryCode ?? item.CountryIso3 ?? string.Empty }
                })
                .SingleOrDefaultAsync(cancellationToken);
            return territory is null ? null : ToAreaMatch(new ScoredAreaCandidate(territory, 500, "area_scope"));
        }

        return null;
    }

    private static List<string> BuildSuggestedFollowUps(
        IReadOnlyCollection<GeoTrackToolItemDto> places,
        IReadOnlyCollection<GeoTrackToolItemDto> stays,
        IReadOnlyCollection<GeoTrackToolItemDto> events)
    {
        var suggestions = new List<string>();
        if (places.Any(item => string.Equals(item.Category, "Restaurant", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(item.Category, "Cafe", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(item.Category, "Bar", StringComparison.OrdinalIgnoreCase)))
        {
            suggestions.Add("restaurants");
        }

        if (places.Any(item => string.Equals(item.Category, "Beach", StringComparison.OrdinalIgnoreCase)))
        {
            suggestions.Add("beaches");
        }

        if (stays.Count > 0)
        {
            suggestions.Add("stays");
        }

        if (events.Count > 0)
        {
            suggestions.Add("events");
        }

        if (places.Count > 0)
        {
            suggestions.Add("attractions");
        }

        return suggestions.Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToList();
    }

    private async Task<List<GeoTrackCategoryGroupDto>> BuildCategoryGroupsAsync(CancellationToken cancellationToken)
    {
        if (_taxonomyProvider is null)
        {
            return BuildCategoryGroups();
        }

        var taxonomy = await _taxonomyProvider.GetTaxonomyAsync(cancellationToken);
        var groups = taxonomy.Concepts
            .Where(concept => concept.Id is not "NEARBY" and not "FOLLOW_UP_ACCEPTANCE")
            .Where(concept => concept.Category is not "nearby" and not "follow_up_acceptance")
            .Select(concept => new GeoTrackCategoryGroupDto
            {
                Id = concept.Category,
                Label = concept.DisplayLabel,
                EntityTypes = concept.TargetEntityTypes
                    .Select(NormalizeEntityTypeForSearch)
                    .Where(value => value is not null and not "all")
                    .Select(value => value!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value)
                    .ToList(),
                PlaceTypes = concept.PlaceTypes.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToList(),
                StayTypes = concept.StayTypes.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToList(),
                EventCategories = concept.EventCategories.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToList(),
                Tags = concept.Tags.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToList()
            })
            .Where(group => !string.IsNullOrWhiteSpace(group.Id))
            .GroupBy(group => group.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        return groups.Count == 0 ? BuildCategoryGroups() : groups;
    }

    private static List<GeoTrackCategoryGroupDto> BuildCategoryGroups() =>
    [
        new()
        {
            Id = "restaurants",
            Label = "Restaurants, cafes and bars",
            EntityTypes = ["places"],
            PlaceTypes = ["Restaurant", "Cafe", "Bar"],
            Tags = ["food-drink", "fine-dining", "nightlife", "food-market", "wine-bar"]
        },
        new()
        {
            Id = "beaches",
            Label = "Beaches and swimming",
            EntityTypes = ["places"],
            PlaceTypes = ["Beach"],
            Tags = ["beaches"]
        },
        new()
        {
            Id = "stays",
            Label = "Stays and accommodation",
            EntityTypes = ["stays"],
            StayTypes = ["Hotel", "Apartment", "Villa"],
            Tags = ["family-stay", "budget", "breakfast", "beach-district"]
        },
        new()
        {
            Id = "events",
            Label = "Events",
            EntityTypes = ["events"],
            EventCategories = ["Festival", "Concert"],
            Tags = ["festivals", "culture", "music"]
        },
        new()
        {
            Id = "attractions",
            Label = "Attractions and sights",
            EntityTypes = ["places"],
            PlaceTypes = ["Landmark", "Museum", "Viewpoint", "Nature"],
            Tags = ["landmarks", "historic-sites", "culture", "history", "nature", "hiking", "viewpoints"]
        }
    ];

    private static GeoTrackToolSearchRequest CloneSearchRequest(GeoTrackToolSearchRequest request) => new()
    {
        Query = request.Query,
        Location = request.Location,
        DestinationId = request.DestinationId,
        AreaType = request.AreaType,
        AreaId = request.AreaId,
        Category = request.Category,
        EntityTypes = request.EntityTypes.ToList(),
        Tags = request.Tags.ToList(),
        Categories = request.Categories.ToList(),
        PlaceTypes = request.PlaceTypes.ToList(),
        StayTypes = request.StayTypes.ToList(),
        EventCategories = request.EventCategories.ToList(),
        Limit = request.Limit,
        UpcomingOnly = request.UpcomingOnly,
        StrictCategory = request.StrictCategory
    };

    private static bool HasStructuredCriteria(GeoTrackToolSearchRequest request) =>
        request.DestinationId.HasValue ||
        !string.IsNullOrWhiteSpace(request.AreaType) ||
        request.AreaId.HasValue ||
        !string.IsNullOrWhiteSpace(request.Location) ||
        HasStructuredFilters(request);

    private static bool HasStructuredFilters(GeoTrackToolSearchRequest request) =>
        IsFilterCategory(request.Category) ||
        request.Tags.Count > 0 ||
        request.Categories.Count > 0 ||
        request.PlaceTypes.Count > 0 ||
        request.StayTypes.Count > 0 ||
        request.EventCategories.Count > 0;

    private static bool IsFilterCategory(string? category) =>
        NormalizeCategoryId(category) is "restaurants" or "beaches" or "attractions";

    private static bool HasAreaScope(GeoTrackToolSearchRequest request) =>
        request.DestinationId.HasValue ||
        (!string.IsNullOrWhiteSpace(request.AreaType) && request.AreaId.HasValue) ||
        !string.IsNullOrWhiteSpace(request.Location);

    private static List<string> BuildFilterList(GeoTrackAgentQueryPlanDto plan) =>
        BuildDisplayFilterList(
            plan.Category,
            plan.PlaceTypes
                .Concat(plan.StayTypes)
                .Concat(plan.EventCategories)
                .Concat(plan.Categories));

    private static List<string> BuildFilterList(GeoTrackToolSearchRequest request) =>
        BuildDisplayFilterList(
            request.Category,
            request.PlaceTypes
                .Concat(request.StayTypes)
                .Concat(request.EventCategories)
                .Concat(request.Categories));

    private static List<string> BuildDisplayFilterList(
        string? category,
        IEnumerable<string> labels)
    {
        var categoryLabel = DisplayCategoryLabel(category);
        if (!string.IsNullOrWhiteSpace(categoryLabel))
        {
            return [categoryLabel];
        }

        return labels
            .Select(DisplayFilterLabel)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item)
            .Take(4)
            .ToList();
    }

    private static string? DisplayCategoryLabel(string? category)
    {
        var normalized = NormalizeCategoryId(category);
        return normalized switch
        {
            "restaurants" => "restaurants, cafes and bars",
            "beaches" => "beaches",
            "stays" => "stays",
            "events" => "events",
            "attractions" => "sights and attractions",
            _ => null
        };
    }

    private static string? DisplayFilterLabel(string? value)
    {
        var cleaned = DisplayTextSafety.CleanOptional(value);
        if (string.IsNullOrWhiteSpace(cleaned) || cleaned.Contains('-', StringComparison.Ordinal))
        {
            return null;
        }

        return cleaned;
    }

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

    private static string? NormalizeAreaType(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "destination" or "destinations" => "destination",
            "location" or "locations" => "location",
            "region" or "regions" => "region",
            "territory" or "territories" or "country" or "countries" => "territory",
            "place" or "places" or "stay" or "stays" => "place",
            "event" or "events" => "event",
            _ => null
        };
    }

    private static int NormalizeLimit(int? limit) =>
        Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);

    private static int NormalizeCatalogLimit(int? limit) =>
        Math.Clamp(limit ?? DefaultLimit, 1, MaxCatalogLimit);

    private static double NormalizeRadiusKm(double? radiusKm)
    {
        var radius = radiusKm ?? 25d;
        if (double.IsNaN(radius) || double.IsInfinity(radius) || radius <= 0d || radius > 100d)
        {
            throw new AgentException("invalid_tool_arguments", "radiusKm must be between 0 and 100.", 400);
        }

        return radius;
    }

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

    private static ScoredAreaCandidate ScoreAreaCandidate(
        AreaCandidate candidate,
        string normalizedText,
        IReadOnlySet<string> tokens)
    {
        var names = new[]
            {
                candidate.Name,
                candidate.DisplayName,
                candidate.DestinationName
            }
            .Concat(candidate.ParentNames)
            .Concat(candidate.Aliases)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var bestScore = 0;
        var reason = string.Empty;
        foreach (var name in names)
        {
            var normalizedName = AgentTextNormalizer.Normalize(name);
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                continue;
            }

            var nameTokens = AgentTextNormalizer.Tokenize(name).ToHashSet(StringComparer.Ordinal);
            int score;
            string candidateReason;
            if (normalizedText == normalizedName)
            {
                score = 500;
                candidateReason = "exact_name";
            }
            else if (NameMatchesTokens(name, tokens))
            {
                score = 430 + Math.Min(40, nameTokens.Count * 10);
                candidateReason = "token_match";
            }
            else if (AgentTextNormalizer.ContainsPhrase(normalizedText, normalizedName))
            {
                score = 390;
                candidateReason = "phrase_match";
            }
            else if (normalizedName.Length >= 3 && normalizedText.Contains(normalizedName, StringComparison.Ordinal))
            {
                score = 300;
                candidateReason = "contains_name";
            }
            else if (normalizedText.Length >= 3 && normalizedName.Contains(normalizedText, StringComparison.Ordinal))
            {
                score = 180;
                candidateReason = "contains_query";
            }
            else
            {
                score = 0;
                candidateReason = string.Empty;
            }

            if (score > bestScore)
            {
                bestScore = score;
                reason = candidateReason;
            }
        }

        return new ScoredAreaCandidate(candidate, bestScore, reason);
    }

    private static GeoTrackAreaMatchDto ToAreaMatch(ScoredAreaCandidate match) => new()
    {
        Type = match.Item.Type,
        Id = match.Item.Id,
        Name = DisplayTextSafety.CleanOptional(match.Item.Name) ?? string.Empty,
        DisplayName = DisplayTextSafety.CleanOptional(match.Item.DisplayName) ?? string.Empty,
        ParentNames = DisplayTextSafety.CleanLabels(match.Item.ParentNames).Take(4).ToList(),
        Confidence = Math.Round(Math.Clamp(match.Score / 500d, 0d, 1d), 3),
        Reason = match.Reason,
        DestinationId = match.Item.DestinationId,
        DestinationName = DisplayTextSafety.CleanOptional(match.Item.DestinationName),
        Latitude = match.Item.Latitude,
        Longitude = match.Item.Longitude
    };

    private static int AreaTypeRank(string type) => type switch
    {
        "destination" => 0,
        "location" => 1,
        "region" => 2,
        "territory" => 3,
        "place" => 4,
        "stay" => 5,
        "event" => 6,
        _ => 9
    };

    private static bool NameMatchesTokens(string name, IReadOnlySet<string> tokenSet)
    {
        var normalized = AgentTextNormalizer.Normalize(name);
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length > 0 &&
            words.All(word => AgentTextNormalizer.TokenVariants(word).Any(tokenSet.Contains));
    }

    private static string NormalizeRequiredText(string? value, string name, int maxLength)
    {
        var cleaned = value?.Trim() ?? string.Empty;
        if (cleaned.Length < 2 || cleaned.Length > maxLength || cleaned.Any(char.IsControl))
        {
            throw new AgentException("invalid_tool_arguments", $"{name} is invalid.", 400);
        }

        return cleaned;
    }

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
        string? AreaType,
        int? AreaId,
        List<string> TagTerms,
        List<string> PlaceTypeTerms,
        List<string> StayTypeTerms,
        List<string> EventCategoryTerms)
    {
        public bool HasAny =>
            !string.IsNullOrWhiteSpace(Query) ||
            DestinationId.HasValue ||
            (!string.IsNullOrWhiteSpace(AreaType) && AreaId.HasValue) ||
            TagTerms.Count > 0 ||
            PlaceTypeTerms.Count > 0 ||
            StayTypeTerms.Count > 0 ||
            EventCategoryTerms.Count > 0;
    }

    private sealed record CategorySpec(
        string Id,
        bool Strict,
        string[] EntityTypes,
        string[] Categories,
        string[] Tags,
        string[] PlaceTypes,
        string[] StayTypes,
        string[] EventCategories);

    private sealed class AreaCandidate
    {
        public string Type { get; init; } = string.Empty;
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public List<string> ParentNames { get; init; } = new();
        public List<string> Aliases { get; init; } = new();
        public int? DestinationId { get; init; }
        public string? DestinationName { get; init; }
        public double? Latitude { get; init; }
        public double? Longitude { get; init; }
    }

    private sealed record ScoredAreaCandidate(
        AreaCandidate Item,
        int Score,
        string Reason);
}

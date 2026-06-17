using Geotrack.Application.DTOs.Destinations;
using Geotrack.Domain.Entities.Core;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public class DestinationDiscoverySearchService
{
    private const int DefaultTake = 20;
    private const int MaxTake = 50;
    private const int MinQueryLength = 2;
    private const int MaxQueryLength = 100;
    private const string PublishedStatus = "Published";

    private readonly AppDbContext _db;

    public DestinationDiscoverySearchService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<DestinationDiscoverySearchResponseDto> SearchAsync(
        string? query,
        IEnumerable<int>? destinationTypeIds = null,
        IEnumerable<int>? tagIds = null,
        int skip = 0,
        int take = DefaultTake,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = NormalizeQuery(query);
        var hasQuery = normalizedQuery.Length >= MinQueryLength;
        var normalizedSkip = Math.Max(0, skip);
        var normalizedTake = Math.Clamp(take <= 0 ? DefaultTake : take, 1, MaxTake);
        var normalizedDestinationTypeIds = NormalizeIds(destinationTypeIds);
        var normalizedTagIds = NormalizeIds(tagIds);

        if (normalizedQuery.Length is > 0 and < MinQueryLength)
        {
            return new DestinationDiscoverySearchResponseDto
            {
                Query = normalizedQuery,
                Skip = normalizedSkip,
                Take = normalizedTake,
                NextSkip = normalizedSkip,
                HasMore = false
            };
        }

        if (normalizedQuery.Length > MaxQueryLength)
        {
            throw new InvalidOperationException("Search query must be 100 characters or fewer.");
        }

        if (normalizedDestinationTypeIds.Count > 0)
        {
            var validDestinationTypeCount = await _db.DestinationTypes
                .AsNoTracking()
                .CountAsync(destinationType => normalizedDestinationTypeIds.Contains(destinationType.Id), cancellationToken);

            if (validDestinationTypeCount != normalizedDestinationTypeIds.Count)
            {
                throw new InvalidOperationException("One or more destination type filters are invalid.");
            }
        }

        var selectedTagGroups = await LoadSelectedDestinationTagGroupsAsync(normalizedTagIds, cancellationToken);
        var selectedTagIdArray = selectedTagGroups
            .SelectMany(group => group.TagIds)
            .Distinct()
            .ToArray();
        var destinationTypeIdArray = normalizedDestinationTypeIds.ToArray();

        var baseQuery = _db.Destinations
            .AsNoTracking()
            .Where(destination => destination.Status == PublishedStatus);

        if (hasQuery)
        {
            baseQuery = ApplyTextSearch(baseQuery, normalizedQuery);
        }

        baseQuery = ApplyDestinationTypeFilters(baseQuery, destinationTypeIdArray);
        baseQuery = ApplyDestinationTagFilters(baseQuery, selectedTagGroups);

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        if (totalCount == 0)
        {
            return new DestinationDiscoverySearchResponseDto
            {
                Query = normalizedQuery,
                Skip = normalizedSkip,
                Take = normalizedTake,
                NextSkip = normalizedSkip,
                HasMore = false,
                TotalCount = 0
            };
        }

        var scoredPage = await baseQuery
            .Select(destination => new DestinationScoreProjection
            {
                Id = destination.Id,
                Name = destination.Name,
                Score =
                    (hasQuery && destination.Name.ToLower().Contains(normalizedQuery) ? 140 : 0) +
                    (hasQuery && destination.DestinationType.Name.ToLower().Contains(normalizedQuery) ? 105 : 0) +
                    (hasQuery && destination.DestinationTags.Any(destinationTag =>
                        destinationTag.Tag.Name.ToLower().Contains(normalizedQuery) ||
                        destinationTag.Tag.Slug.ToLower().Contains(normalizedQuery)) ? 100 : 0) +
                    (hasQuery && destination.Locations.Any(location =>
                        location.Status == PublishedStatus &&
                        location.Name.ToLower().Contains(normalizedQuery)) ? 80 : 0) +
                    (hasQuery && destination.Locations.Any(location =>
                        location.Status == PublishedStatus &&
                        location.LocationTags.Any(locationTag =>
                            locationTag.Tag.Name.ToLower().Contains(normalizedQuery) ||
                            locationTag.Tag.Slug.ToLower().Contains(normalizedQuery))) ? 70 : 0) +
                    (hasQuery && destination.Locations.Any(location =>
                        location.Status == PublishedStatus &&
                        location.Places.Any(place =>
                            place.Status == PublishedStatus &&
                            place.Name.ToLower().Contains(normalizedQuery))) ? 65 : 0) +
                    (hasQuery && destination.Locations.Any(location =>
                        location.Status == PublishedStatus &&
                        location.Places.Any(place =>
                            place.Status == PublishedStatus &&
                            place.PlaceTags.Any(placeTag =>
                                placeTag.Tag.Name.ToLower().Contains(normalizedQuery) ||
                                placeTag.Tag.Slug.ToLower().Contains(normalizedQuery)))) ? 60 : 0) +
                    (hasQuery && destination.Locations.Any(location =>
                        location.Status == PublishedStatus &&
                        location.Places.Any(place =>
                            place.Status == PublishedStatus &&
                            place.PlaceType.Name.ToLower().Contains(normalizedQuery))) ? 55 : 0) +
                    (hasQuery && destination.Locations.Any(location =>
                        location.Status == PublishedStatus &&
                        location.Places.Any(place =>
                            place.Status == PublishedStatus &&
                            place.Stay != null &&
                            place.Stay.StayType.Name.ToLower().Contains(normalizedQuery))) ? 55 : 0) +
                    (hasQuery && destination.Summary != null && destination.Summary.ToLower().Contains(normalizedQuery) ? 45 : 0) +
                    (hasQuery && destination.Description.ToLower().Contains(normalizedQuery) ? 30 : 0) +
                    (hasQuery && destination.Locations.Any(location =>
                        location.Status == PublishedStatus &&
                        location.Summary != null &&
                        location.Summary.ToLower().Contains(normalizedQuery)) ? 25 : 0) +
                    (hasQuery && destination.Locations.Any(location =>
                        location.Status == PublishedStatus &&
                        location.Description.ToLower().Contains(normalizedQuery)) ? 20 : 0) +
                    (hasQuery && destination.Locations.Any(location =>
                        location.Status == PublishedStatus &&
                        location.Places.Any(place =>
                            place.Status == PublishedStatus &&
                            place.Subtitle != null &&
                            place.Subtitle.ToLower().Contains(normalizedQuery))) ? 20 : 0) +
                    (hasQuery && destination.Locations.Any(location =>
                        location.Status == PublishedStatus &&
                        location.Places.Any(place =>
                            place.Status == PublishedStatus &&
                            place.Description.ToLower().Contains(normalizedQuery))) ? 15 : 0) +
                    (destinationTypeIdArray.Length > 0 && destinationTypeIdArray.Contains(destination.DestinationTypeId) ? 45 : 0) +
                    (selectedTagIdArray.Length > 0 && destination.DestinationTags.Any(destinationTag => selectedTagIdArray.Contains(destinationTag.TagId)) ? 40 : 0)
            })
            .OrderByDescending(destination => destination.Score)
            .ThenBy(destination => destination.Name)
            .ThenBy(destination => destination.Id)
            .Skip(normalizedSkip)
            .Take(normalizedTake)
            .ToListAsync(cancellationToken);

        var destinationIds = scoredPage.Select(destination => destination.Id).ToArray();
        var scoresById = scoredPage.ToDictionary(destination => destination.Id, destination => destination.Score);
        var rows = await LoadRowsAsync(destinationIds, normalizedQuery, hasQuery, selectedTagIdArray, destinationTypeIdArray, cancellationToken);
        var items = rows
            .OrderBy(row => Array.IndexOf(destinationIds, row.Id))
            .Select(row => MapRow(row, scoresById.GetValueOrDefault(row.Id)))
            .ToList();

        return new DestinationDiscoverySearchResponseDto
        {
            Query = normalizedQuery,
            Items = items,
            TotalCount = totalCount,
            Skip = normalizedSkip,
            Take = normalizedTake,
            NextSkip = normalizedSkip + items.Count,
            HasMore = normalizedSkip + items.Count < totalCount
        };
    }

    public async Task<DestinationDiscoveryFiltersDto> GetFiltersAsync(CancellationToken cancellationToken = default)
    {
        var destinationTypeFilters = await _db.DestinationTypes
            .AsNoTracking()
            .Select(destinationType => new DestinationDiscoveryDestinationTypeFilterDto
            {
                Id = destinationType.Id,
                Name = destinationType.Name,
                Count = destinationType.Destinations.Count(destination => destination.Status == PublishedStatus)
            })
            .Where(option => option.Count > 0)
            .OrderBy(option => option.Name)
            .ToListAsync(cancellationToken);

        var tagTypeFilters = await _db.TagTypes
            .AsNoTracking()
            .Where(tagType =>
                tagType.IsFilterable &&
                tagType.ForDestination)
            .OrderBy(tagType => tagType.FilterOrder)
            .ThenBy(tagType => tagType.Name)
            .Select(tagType => new DestinationDiscoveryTagTypeFilterDto
            {
                Id = tagType.Id,
                Name = tagType.Name,
                Slug = tagType.Slug,
                Label = tagType.FilterLabel ?? tagType.Name,
                Order = tagType.FilterOrder,
                Options = tagType.Tags
                    .Where(tag => tag.ForDestination)
                    .OrderBy(tag => tag.Name)
                    .Select(tag => new DestinationDiscoveryTagFilterOptionDto
                    {
                        Id = tag.Id,
                        Name = tag.Name,
                        Slug = tag.Slug,
                        Count = tag.DestinationTags
                            .Where(destinationTag => destinationTag.Destination.Status == PublishedStatus)
                            .Select(destinationTag => destinationTag.DestinationId)
                            .Distinct()
                            .Count()
                    })
                    .Where(option => option.Count > 0)
                    .ToList()
            })
            .Where(group => group.Options.Count > 0)
            .ToListAsync(cancellationToken);

        return new DestinationDiscoveryFiltersDto
        {
            DestinationTypeFilters = destinationTypeFilters,
            TagTypeFilters = tagTypeFilters
        };
    }

    private async Task<List<SelectedTagGroup>> LoadSelectedDestinationTagGroupsAsync(
        IReadOnlyCollection<int> tagIds,
        CancellationToken cancellationToken)
    {
        if (tagIds.Count == 0)
            return [];

        var tags = await _db.Tags
            .AsNoTracking()
            .Where(tag =>
                tagIds.Contains(tag.Id) &&
                tag.ForDestination &&
                tag.TagType != null &&
                tag.TagType.IsFilterable &&
                tag.TagType.ForDestination)
            .Select(tag => new
            {
                tag.Id,
                TagTypeId = tag.TagTypeId!.Value
            })
            .ToListAsync(cancellationToken);

        if (tags.Count != tagIds.Count)
        {
            throw new InvalidOperationException("One or more tag filters are invalid, not filterable, or not destination-level tags.");
        }

        return tags
            .GroupBy(tag => tag.TagTypeId)
            .Select(group => new SelectedTagGroup
            {
                TagTypeId = group.Key,
                TagIds = group.Select(tag => tag.Id).Distinct().ToArray()
            })
            .ToList();
    }

    private static IQueryable<Destination> ApplyTextSearch(IQueryable<Destination> query, string term)
    {
        return query.Where(destination =>
            destination.Name.ToLower().Contains(term) ||
            destination.DestinationType.Name.ToLower().Contains(term) ||
            (destination.Summary != null && destination.Summary.ToLower().Contains(term)) ||
            destination.Description.ToLower().Contains(term) ||
            destination.DestinationTags.Any(destinationTag =>
                destinationTag.Tag.Name.ToLower().Contains(term) ||
                destinationTag.Tag.Slug.ToLower().Contains(term)) ||
            destination.Locations.Any(location =>
                location.Status == PublishedStatus &&
                (
                    location.Name.ToLower().Contains(term) ||
                    (location.Summary != null && location.Summary.ToLower().Contains(term)) ||
                    location.Description.ToLower().Contains(term) ||
                    location.LocationType.Name.ToLower().Contains(term) ||
                    location.LocationTags.Any(locationTag =>
                        locationTag.Tag.Name.ToLower().Contains(term) ||
                        locationTag.Tag.Slug.ToLower().Contains(term)) ||
                    location.Places.Any(place =>
                        place.Status == PublishedStatus &&
                        (
                            place.Name.ToLower().Contains(term) ||
                            (place.Subtitle != null && place.Subtitle.ToLower().Contains(term)) ||
                            (place.Address != null && place.Address.ToLower().Contains(term)) ||
                            place.Description.ToLower().Contains(term) ||
                            place.PlaceType.Name.ToLower().Contains(term) ||
                            (place.Stay != null && place.Stay.StayType.Name.ToLower().Contains(term)) ||
                            place.PlaceTags.Any(placeTag =>
                                placeTag.Tag.Name.ToLower().Contains(term) ||
                                placeTag.Tag.Slug.ToLower().Contains(term))
                        ))
                )));
    }

    private static IQueryable<Destination> ApplyDestinationTypeFilters(IQueryable<Destination> query, int[] destinationTypeIds)
    {
        if (destinationTypeIds.Length == 0)
            return query;

        return query.Where(destination => destinationTypeIds.Contains(destination.DestinationTypeId));
    }

    private static IQueryable<Destination> ApplyDestinationTagFilters(IQueryable<Destination> query, IReadOnlyCollection<SelectedTagGroup> tagGroups)
    {
        foreach (var tagGroup in tagGroups)
        {
            var groupTagIds = tagGroup.TagIds;
            query = query.Where(destination =>
                destination.DestinationTags.Any(destinationTag => groupTagIds.Contains(destinationTag.TagId)));
        }

        return query;
    }

    private async Task<List<DestinationSearchRow>> LoadRowsAsync(
        int[] destinationIds,
        string term,
        bool hasQuery,
        int[] selectedTagIds,
        int[] selectedDestinationTypeIds,
        CancellationToken cancellationToken)
    {
        if (destinationIds.Length == 0)
            return [];

        // Do not project all child collections in one query. The previous query
        // projected destination tags, matching locations, location tags, places,
        // place tags, place types and stay types at once. EF translated that into
        // one very large cartesian query, which caused a sharp memory spike in
        // production. Load the scalar destination rows first and hydrate each
        // collection with small flat queries instead.
        var rows = await _db.Destinations
            .AsNoTracking()
            .Where(destination => destinationIds.Contains(destination.Id))
            .Select(destination => new DestinationSearchRow
            {
                Id = destination.Id,
                Name = destination.Name,
                Summary = destination.Summary,
                Description = destination.Description,
                Latitude = destination.Latitude,
                Longitude = destination.Longitude,
                DestinationTypeId = destination.DestinationTypeId,
                DestinationTypeName = destination.DestinationType.Name,
                ImageUrl = destination.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Destination)
                    .OrderBy(image => image.ImageRole == EntityImageRole.Card ? 0 : image.ImageRole == EntityImageRole.Cover ? 1 : 2)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                DestinationTypeMatched = selectedDestinationTypeIds.Contains(destination.DestinationTypeId) ||
                    (hasQuery && destination.DestinationType.Name.ToLower().Contains(term))
            })
            .ToListAsync(cancellationToken);

        var rowsById = rows.ToDictionary(row => row.Id);

        var destinationTags = await _db.DestinationTags
            .AsNoTracking()
            .Where(destinationTag => destinationIds.Contains(destinationTag.DestinationId))
            .OrderBy(destinationTag => destinationTag.DestinationId)
            .ThenBy(destinationTag => destinationTag.Tag.Name)
            .ThenBy(destinationTag => destinationTag.TagId)
            .Select(destinationTag => new
            {
                destinationTag.DestinationId,
                destinationTag.TagId,
                destinationTag.Tag.Name,
                destinationTag.Tag.Slug,
                destinationTag.Tag.TagTypeId,
                TagTypeSlug = destinationTag.Tag.TagType != null
                    ? destinationTag.Tag.TagType.Slug
                    : null
            })
            .ToListAsync(cancellationToken);

        foreach (var group in destinationTags.GroupBy(tag => tag.DestinationId))
        {
            if (!rowsById.TryGetValue(group.Key, out var row))
                continue;

            row.Tags = group
                .Select(tag => new DestinationDiscoveryTagDto
                {
                    Id = tag.TagId,
                    Name = tag.Name,
                    Slug = tag.Slug,
                    TagTypeId = tag.TagTypeId,
                    TagTypeSlug = tag.TagTypeSlug
                })
                .ToList();

            row.MatchedDestinationTags = TakeDistinctNames(
                group
                    .Where(tag =>
                        selectedTagIds.Contains(tag.TagId) ||
                        (hasQuery &&
                         (tag.Name.ToLower().Contains(term) ||
                          tag.Slug.ToLower().Contains(term))))
                    .Select(tag => tag.Name),
                8);
        }

        if (!hasQuery)
            return rows;

        var matchedLocations = await _db.Locations
            .AsNoTracking()
            .Where(location =>
                destinationIds.Contains(location.DestinationId) &&
                location.Status == PublishedStatus &&
                (
                    location.Name.ToLower().Contains(term) ||
                    (location.Summary != null && location.Summary.ToLower().Contains(term)) ||
                    location.Description.ToLower().Contains(term) ||
                    location.LocationType.Name.ToLower().Contains(term) ||
                    location.LocationTags.Any(locationTag =>
                        locationTag.Tag.Name.ToLower().Contains(term) ||
                        locationTag.Tag.Slug.ToLower().Contains(term))
                ))
            .OrderBy(location => location.DestinationId)
            .ThenBy(location => location.Name)
            .ThenBy(location => location.Id)
            .Select(location => new
            {
                location.DestinationId,
                location.Id,
                location.Name
            })
            .ToListAsync(cancellationToken);

        foreach (var group in matchedLocations.GroupBy(location => location.DestinationId))
        {
            if (!rowsById.TryGetValue(group.Key, out var row))
                continue;

            var matches = group.ToList();
            row.MatchedLocationCount = matches.Count;
            row.MatchedLocationNames = TakeDistinctNames(matches.Select(location => location.Name), 5);
        }

        var matchedLocationTags = await _db.LocationTags
            .AsNoTracking()
            .Where(locationTag =>
                destinationIds.Contains(locationTag.Location.DestinationId) &&
                locationTag.Location.Status == PublishedStatus &&
                (
                    locationTag.Tag.Name.ToLower().Contains(term) ||
                    locationTag.Tag.Slug.ToLower().Contains(term)
                ))
            .OrderBy(locationTag => locationTag.Location.DestinationId)
            .ThenBy(locationTag => locationTag.Tag.Name)
            .ThenBy(locationTag => locationTag.TagId)
            .Select(locationTag => new
            {
                locationTag.Location.DestinationId,
                locationTag.TagId,
                locationTag.Tag.Name
            })
            .ToListAsync(cancellationToken);

        foreach (var group in matchedLocationTags.GroupBy(tag => tag.DestinationId))
        {
            if (rowsById.TryGetValue(group.Key, out var row))
                row.MatchedLocationTags = TakeDistinctNames(group.Select(tag => tag.Name), 8);
        }

        var matchedPlaces = await _db.Places
            .AsNoTracking()
            .Where(place =>
                destinationIds.Contains(place.Location.DestinationId) &&
                place.Location.Status == PublishedStatus &&
                place.Status == PublishedStatus &&
                (
                    place.Name.ToLower().Contains(term) ||
                    (place.Subtitle != null && place.Subtitle.ToLower().Contains(term)) ||
                    (place.Address != null && place.Address.ToLower().Contains(term)) ||
                    place.Description.ToLower().Contains(term) ||
                    place.PlaceType.Name.ToLower().Contains(term) ||
                    (place.Stay != null && place.Stay.StayType.Name.ToLower().Contains(term)) ||
                    place.PlaceTags.Any(placeTag =>
                        placeTag.Tag.Name.ToLower().Contains(term) ||
                        placeTag.Tag.Slug.ToLower().Contains(term))
                ))
            .OrderBy(place => place.Location.DestinationId)
            .ThenBy(place => place.Name)
            .ThenBy(place => place.Id)
            .Select(place => new
            {
                DestinationId = place.Location.DestinationId,
                place.Id,
                place.Name
            })
            .ToListAsync(cancellationToken);

        foreach (var group in matchedPlaces.GroupBy(place => place.DestinationId))
        {
            if (!rowsById.TryGetValue(group.Key, out var row))
                continue;

            var matches = group.ToList();
            row.MatchedPlaceCount = matches.Count;
            row.MatchedPlaceNames = TakeDistinctNames(matches.Select(place => place.Name), 5);
        }

        var matchedPlaceTags = await _db.PlaceTags
            .AsNoTracking()
            .Where(placeTag =>
                destinationIds.Contains(placeTag.Place.Location.DestinationId) &&
                placeTag.Place.Location.Status == PublishedStatus &&
                placeTag.Place.Status == PublishedStatus &&
                (
                    placeTag.Tag.Name.ToLower().Contains(term) ||
                    placeTag.Tag.Slug.ToLower().Contains(term)
                ))
            .OrderBy(placeTag => placeTag.Place.Location.DestinationId)
            .ThenBy(placeTag => placeTag.Tag.Name)
            .ThenBy(placeTag => placeTag.TagId)
            .Select(placeTag => new
            {
                DestinationId = placeTag.Place.Location.DestinationId,
                placeTag.TagId,
                placeTag.Tag.Name
            })
            .ToListAsync(cancellationToken);

        foreach (var group in matchedPlaceTags.GroupBy(tag => tag.DestinationId))
        {
            if (rowsById.TryGetValue(group.Key, out var row))
                row.MatchedPlaceTags = TakeDistinctNames(group.Select(tag => tag.Name), 8);
        }

        var matchedPlaceTypes = await _db.Places
            .AsNoTracking()
            .Where(place =>
                destinationIds.Contains(place.Location.DestinationId) &&
                place.Location.Status == PublishedStatus &&
                place.Status == PublishedStatus &&
                place.PlaceType.Name.ToLower().Contains(term))
            .OrderBy(place => place.Location.DestinationId)
            .ThenBy(place => place.PlaceType.Name)
            .ThenBy(place => place.PlaceTypeId)
            .Select(place => new
            {
                DestinationId = place.Location.DestinationId,
                place.PlaceTypeId,
                place.PlaceType.Name
            })
            .ToListAsync(cancellationToken);

        foreach (var group in matchedPlaceTypes.GroupBy(placeType => placeType.DestinationId))
        {
            if (rowsById.TryGetValue(group.Key, out var row))
                row.MatchedPlaceTypes = TakeDistinctNames(group.Select(placeType => placeType.Name), 8);
        }

        var matchedStayTypes = await _db.Places
            .AsNoTracking()
            .Where(place =>
                destinationIds.Contains(place.Location.DestinationId) &&
                place.Location.Status == PublishedStatus &&
                place.Status == PublishedStatus &&
                place.Stay != null &&
                place.Stay.StayType.Name.ToLower().Contains(term))
            .OrderBy(place => place.Location.DestinationId)
            .ThenBy(place => place.Stay!.StayType.Name)
            .ThenBy(place => place.Stay!.StayTypeId)
            .Select(place => new
            {
                DestinationId = place.Location.DestinationId,
                StayTypeId = place.Stay!.StayTypeId,
                Name = place.Stay!.StayType.Name
            })
            .ToListAsync(cancellationToken);

        foreach (var group in matchedStayTypes.GroupBy(stayType => stayType.DestinationId))
        {
            if (rowsById.TryGetValue(group.Key, out var row))
                row.MatchedStayTypes = TakeDistinctNames(group.Select(stayType => stayType.Name), 8);
        }

        return rows;
    }

    private static List<string> TakeDistinctNames(IEnumerable<string> values, int take)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .ToList();
    }

    private static DestinationDiscoverySearchItemDto MapRow(DestinationSearchRow row, double score)
    {
        return new DestinationDiscoverySearchItemDto
        {
            Id = row.Id,
            Name = row.Name,
            Summary = CleanOptional(row.Summary),
            Description = TrimDescription(row.Description, 180),
            ImageUrl = row.ImageUrl,
            DetailRoute = $"/explore?destinationId={row.Id}",
            Latitude = row.Latitude,
            Longitude = row.Longitude,
            DestinationTypeId = row.DestinationTypeId,
            DestinationTypeName = row.DestinationTypeName,
            Tags = row.Tags,
            MatchedBy = BuildMatchedBy(row),
            MatchedLocationCount = row.MatchedLocationCount,
            MatchedPlaceCount = row.MatchedPlaceCount,
            Score = score
        };
    }

    private static List<string> BuildMatchedBy(DestinationSearchRow row)
    {
        var matches = new List<string>();

        if (row.DestinationTypeMatched)
        {
            matches.Add(row.DestinationTypeName);
        }

        matches.AddRange(row.MatchedDestinationTags);
        matches.AddRange(row.MatchedLocationNames);
        matches.AddRange(row.MatchedLocationTags);
        matches.AddRange(row.MatchedPlaceNames);
        matches.AddRange(row.MatchedPlaceTags);
        matches.AddRange(row.MatchedPlaceTypes);
        matches.AddRange(row.MatchedStayTypes);

        return matches
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }

    private static List<int> NormalizeIds(IEnumerable<int>? ids)
    {
        return ids?
            .Where(id => id > 0)
            .Distinct()
            .ToList() ?? [];
    }

    private static string NormalizeQuery(string? query)
    {
        return (query ?? string.Empty)
            .Trim()
            .Replace("%", string.Empty)
            .Replace("_", string.Empty)
            .ToLowerInvariant();
    }

    private static string? CleanOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? TrimDescription(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength].TrimEnd() + "...";
    }

    private sealed class SelectedTagGroup
    {
        public int TagTypeId { get; set; }
        public int[] TagIds { get; set; } = [];
    }

    private sealed class DestinationScoreProjection
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public double Score { get; set; }
    }

    private sealed class DestinationSearchRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Summary { get; set; }
        public string? Description { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int DestinationTypeId { get; set; }
        public string DestinationTypeName { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public List<DestinationDiscoveryTagDto> Tags { get; set; } = new();
        public bool DestinationTypeMatched { get; set; }
        public List<string> MatchedDestinationTags { get; set; } = new();
        public List<string> MatchedLocationNames { get; set; } = new();
        public List<string> MatchedLocationTags { get; set; } = new();
        public List<string> MatchedPlaceNames { get; set; } = new();
        public List<string> MatchedPlaceTags { get; set; } = new();
        public List<string> MatchedPlaceTypes { get; set; } = new();
        public List<string> MatchedStayTypes { get; set; } = new();
        public int MatchedLocationCount { get; set; }
        public int MatchedPlaceCount { get; set; }
    }
}

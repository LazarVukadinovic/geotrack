using Geotrack.Application.DTOs.Destinations;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Tags;
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
        IEnumerable<int>? tagIds = null,
        IEnumerable<int>? placeTypeIds = null,
        int skip = 0,
        int take = DefaultTake,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = NormalizeQuery(query);
        var hasQuery = normalizedQuery.Length >= MinQueryLength;
        var normalizedSkip = Math.Max(0, skip);
        var normalizedTake = Math.Clamp(take <= 0 ? DefaultTake : take, 1, MaxTake);
        var normalizedTagIds = NormalizeIds(tagIds);
        var normalizedPlaceTypeIds = NormalizeIds(placeTypeIds);

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

        var selectedTagGroups = await LoadSelectedFilterableTagGroupsAsync(normalizedTagIds, cancellationToken);
        var selectedTagIdArray = selectedTagGroups
            .SelectMany(group => group.TagIds)
            .Distinct()
            .ToArray();

        if (normalizedPlaceTypeIds.Count > 0)
        {
            var validPlaceTypeCount = await _db.PlaceTypes
                .AsNoTracking()
                .CountAsync(placeType => normalizedPlaceTypeIds.Contains(placeType.Id), cancellationToken);

            if (validPlaceTypeCount != normalizedPlaceTypeIds.Count)
            {
                throw new InvalidOperationException("One or more place type filters are invalid.");
            }
        }

        var placeTypeIdArray = normalizedPlaceTypeIds.ToArray();
        var baseQuery = _db.Destinations
            .AsNoTracking()
            .Where(destination => destination.Status == PublishedStatus);

        if (hasQuery)
        {
            baseQuery = ApplyTextSearch(baseQuery, normalizedQuery);
        }

        baseQuery = ApplyTagFilters(baseQuery, selectedTagGroups);
        baseQuery = ApplyPlaceTypeFilters(baseQuery, placeTypeIdArray);

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
                    (selectedTagIdArray.Length > 0 && destination.DestinationTags.Any(destinationTag => selectedTagIdArray.Contains(destinationTag.TagId)) ? 35 : 0) +
                    (selectedTagIdArray.Length > 0 && destination.Locations.Any(location =>
                        location.Status == PublishedStatus &&
                        location.LocationTags.Any(locationTag => selectedTagIdArray.Contains(locationTag.TagId))) ? 30 : 0) +
                    (selectedTagIdArray.Length > 0 && destination.Locations.Any(location =>
                        location.Status == PublishedStatus &&
                        location.Places.Any(place =>
                            place.Status == PublishedStatus &&
                            place.PlaceTags.Any(placeTag => selectedTagIdArray.Contains(placeTag.TagId)))) ? 30 : 0) +
                    (placeTypeIdArray.Length > 0 && destination.Locations.Any(location =>
                        location.Status == PublishedStatus &&
                        location.Places.Any(place =>
                            place.Status == PublishedStatus &&
                            placeTypeIdArray.Contains(place.PlaceTypeId))) ? 25 : 0)
            })
            .OrderByDescending(destination => destination.Score)
            .ThenBy(destination => destination.Name)
            .Skip(normalizedSkip)
            .Take(normalizedTake)
            .ToListAsync(cancellationToken);

        var destinationIds = scoredPage.Select(destination => destination.Id).ToArray();
        var scoresById = scoredPage.ToDictionary(destination => destination.Id, destination => destination.Score);
        var rows = await LoadRowsAsync(destinationIds, normalizedQuery, hasQuery, selectedTagIdArray, placeTypeIdArray, cancellationToken);
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
        var tagTypeFilters = await _db.TagTypes
            .AsNoTracking()
            .Where(tagType => tagType.IsFilterable)
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
                    .Where(tag =>
                        tag.ForDestination ||
                        tag.ForLocation ||
                        tag.ForPlace)
                    .OrderBy(tag => tag.Name)
                    .Select(tag => new DestinationDiscoveryTagFilterOptionDto
                    {
                        Id = tag.Id,
                        Name = tag.Name,
                        Slug = tag.Slug,
                        Count =
                            tag.DestinationTags
                                .Where(destinationTag => destinationTag.Destination.Status == PublishedStatus)
                                .Select(destinationTag => destinationTag.DestinationId)
                                .Distinct()
                                .Count() +
                            tag.LocationTags
                                .Where(locationTag =>
                                    locationTag.Location.Status == PublishedStatus &&
                                    locationTag.Location.Destination.Status == PublishedStatus)
                                .Select(locationTag => locationTag.Location.DestinationId)
                                .Distinct()
                                .Count() +
                            tag.PlaceTags
                                .Where(placeTag =>
                                    placeTag.Place.Status == PublishedStatus &&
                                    placeTag.Place.Location.Status == PublishedStatus &&
                                    placeTag.Place.Location.Destination.Status == PublishedStatus)
                                .Select(placeTag => placeTag.Place.Location.DestinationId)
                                .Distinct()
                                .Count()
                    })
                    .Where(option => option.Count > 0)
                    .ToList()
            })
            .Where(group => group.Options.Count > 0)
            .ToListAsync(cancellationToken);

        var placeTypeFilters = await _db.PlaceTypes
            .AsNoTracking()
            .Select(placeType => new DestinationDiscoveryPlaceTypeFilterDto
            {
                Id = placeType.Id,
                Name = placeType.Name,
                Count = placeType.Places
                    .Where(place =>
                        place.Status == PublishedStatus &&
                        place.Location.Status == PublishedStatus &&
                        place.Location.Destination.Status == PublishedStatus)
                    .Select(place => place.Location.DestinationId)
                    .Distinct()
                    .Count()
            })
            .Where(option => option.Count > 0)
            .OrderBy(option => option.Name)
            .ToListAsync(cancellationToken);

        return new DestinationDiscoveryFiltersDto
        {
            TagTypeFilters = tagTypeFilters,
            PlaceTypeFilters = placeTypeFilters
        };
    }

    private async Task<List<SelectedTagGroup>> LoadSelectedFilterableTagGroupsAsync(
        IReadOnlyCollection<int> tagIds,
        CancellationToken cancellationToken)
    {
        if (tagIds.Count == 0)
            return [];

        var tags = await _db.Tags
            .AsNoTracking()
            .Where(tag =>
                tagIds.Contains(tag.Id) &&
                tag.TagType != null &&
                tag.TagType.IsFilterable)
            .Select(tag => new
            {
                tag.Id,
                TagTypeId = tag.TagTypeId!.Value
            })
            .ToListAsync(cancellationToken);

        if (tags.Count != tagIds.Count)
        {
            throw new InvalidOperationException("One or more tag filters are invalid or not filterable.");
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

    private static IQueryable<Destination> ApplyTagFilters(IQueryable<Destination> query, IReadOnlyCollection<SelectedTagGroup> tagGroups)
    {
        foreach (var tagGroup in tagGroups)
        {
            var groupTagIds = tagGroup.TagIds;
            query = query.Where(destination =>
                destination.DestinationTags.Any(destinationTag => groupTagIds.Contains(destinationTag.TagId)) ||
                destination.Locations.Any(location =>
                    location.Status == PublishedStatus &&
                    location.LocationTags.Any(locationTag => groupTagIds.Contains(locationTag.TagId))) ||
                destination.Locations.Any(location =>
                    location.Status == PublishedStatus &&
                    location.Places.Any(place =>
                        place.Status == PublishedStatus &&
                        place.PlaceTags.Any(placeTag => groupTagIds.Contains(placeTag.TagId)))));
        }

        return query;
    }

    private static IQueryable<Destination> ApplyPlaceTypeFilters(IQueryable<Destination> query, int[] placeTypeIds)
    {
        if (placeTypeIds.Length == 0)
            return query;

        return query.Where(destination =>
            destination.Locations.Any(location =>
                location.Status == PublishedStatus &&
                location.Places.Any(place =>
                    place.Status == PublishedStatus &&
                    placeTypeIds.Contains(place.PlaceTypeId))));
    }

    private async Task<List<DestinationSearchRow>> LoadRowsAsync(
        int[] destinationIds,
        string term,
        bool hasQuery,
        int[] selectedTagIds,
        int[] selectedPlaceTypeIds,
        CancellationToken cancellationToken)
    {
        if (destinationIds.Length == 0)
            return [];

        return await _db.Destinations
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
                ImageUrl = destination.Images
                    .Where(image => image.TargetType == EntityImageTargetType.Destination)
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.SortOrder)
                    .ThenBy(image => image.CreatedAt)
                    .ThenBy(image => image.Id)
                    .Select(image => image.Url)
                    .FirstOrDefault(),
                Tags = destination.DestinationTags
                    .OrderBy(destinationTag => destinationTag.Tag.Name)
                    .Select(destinationTag => new DestinationDiscoveryTagDto
                    {
                        Id = destinationTag.TagId,
                        Name = destinationTag.Tag.Name,
                        Slug = destinationTag.Tag.Slug,
                        TagTypeId = destinationTag.Tag.TagTypeId,
                        TagTypeSlug = destinationTag.Tag.TagType != null ? destinationTag.Tag.TagType.Slug : null
                    })
                    .ToList(),
                MatchedDestinationTags = destination.DestinationTags
                    .Where(destinationTag =>
                        selectedTagIds.Contains(destinationTag.TagId) ||
                        (hasQuery &&
                         (destinationTag.Tag.Name.ToLower().Contains(term) ||
                          destinationTag.Tag.Slug.ToLower().Contains(term))))
                    .Select(destinationTag => destinationTag.Tag.Name)
                    .ToList(),
                MatchedLocationNames = destination.Locations
                    .Where(location =>
                        location.Status == PublishedStatus &&
                        (
                            (hasQuery &&
                             (
                                 location.Name.ToLower().Contains(term) ||
                                 (location.Summary != null && location.Summary.ToLower().Contains(term)) ||
                                 location.Description.ToLower().Contains(term) ||
                                 location.LocationType.Name.ToLower().Contains(term) ||
                                 location.LocationTags.Any(locationTag =>
                                     locationTag.Tag.Name.ToLower().Contains(term) ||
                                     locationTag.Tag.Slug.ToLower().Contains(term))
                             )) ||
                            location.LocationTags.Any(locationTag => selectedTagIds.Contains(locationTag.TagId))
                        ))
                    .OrderBy(location => location.Name)
                    .Select(location => location.Name)
                    .Take(5)
                    .ToList(),
                MatchedLocationTags = destination.Locations
                    .Where(location => location.Status == PublishedStatus)
                    .SelectMany(location => location.LocationTags)
                    .Where(locationTag =>
                        selectedTagIds.Contains(locationTag.TagId) ||
                        (hasQuery &&
                         (locationTag.Tag.Name.ToLower().Contains(term) ||
                          locationTag.Tag.Slug.ToLower().Contains(term))))
                    .Select(locationTag => locationTag.Tag.Name)
                    .Take(8)
                    .ToList(),
                MatchedPlaceNames = destination.Locations
                    .Where(location => location.Status == PublishedStatus)
                    .SelectMany(location => location.Places)
                    .Where(place =>
                        place.Status == PublishedStatus &&
                        (
                            (hasQuery &&
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
                             )) ||
                            selectedPlaceTypeIds.Contains(place.PlaceTypeId) ||
                            place.PlaceTags.Any(placeTag => selectedTagIds.Contains(placeTag.TagId))
                        ))
                    .OrderBy(place => place.Name)
                    .Select(place => place.Name)
                    .Take(5)
                    .ToList(),
                MatchedPlaceTags = destination.Locations
                    .Where(location => location.Status == PublishedStatus)
                    .SelectMany(location => location.Places)
                    .Where(place => place.Status == PublishedStatus)
                    .SelectMany(place => place.PlaceTags)
                    .Where(placeTag =>
                        selectedTagIds.Contains(placeTag.TagId) ||
                        (hasQuery &&
                         (placeTag.Tag.Name.ToLower().Contains(term) ||
                          placeTag.Tag.Slug.ToLower().Contains(term))))
                    .Select(placeTag => placeTag.Tag.Name)
                    .Take(8)
                    .ToList(),
                MatchedPlaceTypes = destination.Locations
                    .Where(location => location.Status == PublishedStatus)
                    .SelectMany(location => location.Places)
                    .Where(place =>
                        place.Status == PublishedStatus &&
                        (
                            selectedPlaceTypeIds.Contains(place.PlaceTypeId) ||
                            (hasQuery && place.PlaceType.Name.ToLower().Contains(term))
                        ))
                    .Select(place => place.PlaceType.Name)
                    .Take(8)
                    .ToList(),
                MatchedStayTypes = destination.Locations
                    .Where(location => location.Status == PublishedStatus)
                    .SelectMany(location => location.Places)
                    .Where(place =>
                        place.Status == PublishedStatus &&
                        place.Stay != null &&
                        hasQuery &&
                        place.Stay.StayType.Name.ToLower().Contains(term))
                    .Select(place => place.Stay!.StayType.Name)
                    .Take(8)
                    .ToList(),
                MatchedLocationCount = destination.Locations
                    .Count(location =>
                        location.Status == PublishedStatus &&
                        (
                            location.LocationTags.Any(locationTag => selectedTagIds.Contains(locationTag.TagId)) ||
                            (hasQuery &&
                             (
                                 location.Name.ToLower().Contains(term) ||
                                 (location.Summary != null && location.Summary.ToLower().Contains(term)) ||
                                 location.Description.ToLower().Contains(term) ||
                                 location.LocationType.Name.ToLower().Contains(term) ||
                                 location.LocationTags.Any(locationTag =>
                                     locationTag.Tag.Name.ToLower().Contains(term) ||
                                     locationTag.Tag.Slug.ToLower().Contains(term))
                             ))
                        )),
                MatchedPlaceCount = destination.Locations
                    .Where(location => location.Status == PublishedStatus)
                    .SelectMany(location => location.Places)
                    .Count(place =>
                        place.Status == PublishedStatus &&
                        (
                            selectedPlaceTypeIds.Contains(place.PlaceTypeId) ||
                            place.PlaceTags.Any(placeTag => selectedTagIds.Contains(placeTag.TagId)) ||
                            (hasQuery &&
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
                        ))
            })
            .ToListAsync(cancellationToken);
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
            Tags = row.Tags,
            MatchedBy = BuildMatchedBy(row),
            MatchedLocationCount = row.MatchedLocationCount,
            MatchedPlaceCount = row.MatchedPlaceCount,
            Score = score
        };
    }

    private static List<string> BuildMatchedBy(DestinationSearchRow row)
    {
        return row.MatchedDestinationTags
            .Concat(row.MatchedLocationNames)
            .Concat(row.MatchedLocationTags)
            .Concat(row.MatchedPlaceNames)
            .Concat(row.MatchedPlaceTags)
            .Concat(row.MatchedPlaceTypes)
            .Concat(row.MatchedStayTypes)
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
        public string? ImageUrl { get; set; }
        public List<DestinationDiscoveryTagDto> Tags { get; set; } = new();
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

using System.Text.Json;
using System.Text.RegularExpressions;
using Geotrack.Application.DTOs.Contributions;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Tags;
using Geotrack.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public class StayContributionService : IStayContributionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> AllowedStayTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Hotel", "Villa", "Apartment", "Resort", "Cabin", "Glamping", "Hostel"
    };

    private static readonly HashSet<string> AllowedAmenities = new(StringComparer.OrdinalIgnoreCase)
    {
        "pool", "wifi", "breakfast", "parking", "spa", "gym", "dining", "restaurant", "ac"
    };

    private static readonly HashSet<string> AllowedPartnerIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "booking", "airbnb", "hotel", "expedia"
    };

    private readonly AppDbContext _db;
    private readonly IContributionMediaStorageService _mediaStorage;

    public StayContributionService(AppDbContext db, IContributionMediaStorageService mediaStorage)
    {
        _db = db;
        _mediaStorage = mediaStorage;
    }

    public async Task<GetStayContributionsResponseDto> GetMineAsync(int userId)
    {
        var contributions = await _db.StayContributions
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.UpdatedAt ?? x.SubmittedAt)
            .ToListAsync();

        return new GetStayContributionsResponseDto
        {
            Contributions = contributions.Select(MapListItem).ToList()
        };
    }

    public async Task<StayContributionDetailDto> GetMineDetailAsync(int userId, int contributionId)
    {
        var contribution = await _db.StayContributions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == contributionId && x.UserId == userId)
            ?? throw new KeyNotFoundException("Contribution not found");

        return MapDetail(contribution);
    }

    public async Task<StayContributionDetailDto> CreateAsync(int userId, UpsertStayContributionDto request, byte[]? coverImageBytes)
    {
        Validate(request);

        var now = DateTime.UtcNow;
        string? storedMediaUrl = null;

        if (coverImageBytes is { Length: > 0 })
        {
            storedMediaUrl = (await _mediaStorage.StoreAsync(userId, coverImageBytes)).MediaUrl;
        }

        var contribution = new StayContribution
        {
            UserId = userId,
            Status = StayContributionStatus.PendingReview,
            Name = request.Name.Trim(),
            StayType = request.StayType.Trim(),
            Address = request.Address.Trim(),
            City = request.City.Trim(),
            Region = request.Region.Trim(),
            Country = request.Country.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            CoverImageUrl = storedMediaUrl,
            ImagePosition = NormalizeImagePosition(request.ImagePosition),
            ImageSize = NormalizeImageSize(request.ImageSize),
            Description = request.Description.Trim(),
            Phone = request.Phone.Trim(),
            Website = NormalizeWebsite(request.Website),
            TagsJson = Serialize(NormalizeTags(request.Tags)),
            AmenitiesJson = Serialize(NormalizeAmenities(request.Amenities)),
            BookingPartnersJson = Serialize(NormalizeBookingPartners(request.BookingPartners)),
            SubmittedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        try
        {
            await _db.StayContributions.AddAsync(contribution);
            await _db.SaveChangesAsync();
        }
        catch
        {
            if (storedMediaUrl is not null)
                await _mediaStorage.DeleteAsync(storedMediaUrl);

            throw;
        }

        return MapDetail(contribution);
    }

    public async Task<StayContributionDetailDto> UpdateAsync(
        int userId,
        int contributionId,
        UpsertStayContributionDto request,
        byte[]? coverImageBytes)
    {
        Validate(request);

        var contribution = await _db.StayContributions
            .FirstOrDefaultAsync(x => x.Id == contributionId && x.UserId == userId)
            ?? throw new KeyNotFoundException("Contribution not found");

        var previousCoverImageUrl = contribution.CoverImageUrl;
        string? storedMediaUrl = null;

        if (coverImageBytes is { Length: > 0 })
        {
            storedMediaUrl = (await _mediaStorage.StoreAsync(userId, coverImageBytes)).MediaUrl;
        }

        contribution.Name = request.Name.Trim();
        contribution.StayType = request.StayType.Trim();
        contribution.Address = request.Address.Trim();
        contribution.City = request.City.Trim();
        contribution.Region = request.Region.Trim();
        contribution.Country = request.Country.Trim();
        contribution.Latitude = request.Latitude;
        contribution.Longitude = request.Longitude;
        contribution.ImagePosition = NormalizeImagePosition(request.ImagePosition);
        contribution.ImageSize = NormalizeImageSize(request.ImageSize);
        contribution.Description = request.Description.Trim();
        contribution.Phone = request.Phone.Trim();
        contribution.Website = NormalizeWebsite(request.Website);
        contribution.TagsJson = Serialize(NormalizeTags(request.Tags));
        contribution.AmenitiesJson = Serialize(NormalizeAmenities(request.Amenities));
        contribution.BookingPartnersJson = Serialize(NormalizeBookingPartners(request.BookingPartners));
        contribution.Status = StayContributionStatus.PendingReview;
        contribution.ReviewNote = null;
        contribution.ReviewedAt = null;
        contribution.ReviewedByUserId = null;
        contribution.UpdatedAt = DateTime.UtcNow;

        if (storedMediaUrl is not null)
        {
            contribution.CoverImageUrl = storedMediaUrl;
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch
        {
            if (storedMediaUrl is not null)
                await _mediaStorage.DeleteAsync(storedMediaUrl);

            throw;
        }

        if (storedMediaUrl is not null &&
            _mediaStorage.IsManagedContributionMediaUrl(previousCoverImageUrl) &&
            !string.Equals(previousCoverImageUrl, storedMediaUrl, StringComparison.OrdinalIgnoreCase))
        {
            await _mediaStorage.DeleteAsync(previousCoverImageUrl!);
        }

        return MapDetail(contribution);
    }

    public async Task DeleteAsync(int userId, int contributionId)
    {
        var contribution = await _db.StayContributions
            .FirstOrDefaultAsync(x => x.Id == contributionId && x.UserId == userId)
            ?? throw new KeyNotFoundException("Contribution not found");

        var coverImageUrl = contribution.CoverImageUrl;

        _db.StayContributions.Remove(contribution);
        await _db.SaveChangesAsync();

        if (_mediaStorage.IsManagedContributionMediaUrl(coverImageUrl))
            await _mediaStorage.DeleteAsync(coverImageUrl!);
    }

    public async Task<GetStayContributionsResponseDto> GetReviewQueueAsync(int reviewerUserId, string? status)
    {
        await EnsureRegionalManagerAsync(reviewerUserId);

        var query = _db.StayContributions.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(x => x.Status == ParseStatus(status));
        }

        var contributions = await query
            .OrderByDescending(x => x.UpdatedAt ?? x.SubmittedAt)
            .ToListAsync();

        return new GetStayContributionsResponseDto
        {
            Contributions = contributions.Select(MapListItem).ToList()
        };
    }

    public async Task<StayContributionDetailDto> ApproveAsync(
        int reviewerUserId,
        int contributionId,
        ReviewStayContributionRequestDto request)
    {
        var contribution = await _db.StayContributions
            .Include(x => x.PublishedPlace)
            .FirstOrDefaultAsync(x => x.Id == contributionId)
            ?? throw new KeyNotFoundException("Contribution not found");

        await EnsureRegionalManagerCanReviewAsync(reviewerUserId, contribution);

        await using var transaction = await _db.Database.BeginTransactionAsync();

        var place = await PublishPlaceAsync(contribution);
        contribution.PublishedPlaceId = place.Id;
        contribution.Status = StayContributionStatus.Approved;
        contribution.ReviewedByUserId = reviewerUserId;
        contribution.ReviewedAt = DateTime.UtcNow;
        contribution.ReviewNote = NormalizeReviewNote(request.Note);
        contribution.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return MapDetail(contribution);
    }

    public async Task<StayContributionDetailDto> RejectAsync(
        int reviewerUserId,
        int contributionId,
        ReviewStayContributionRequestDto request)
    {
        var contribution = await _db.StayContributions
            .FirstOrDefaultAsync(x => x.Id == contributionId)
            ?? throw new KeyNotFoundException("Contribution not found");

        await EnsureRegionalManagerCanReviewAsync(reviewerUserId, contribution);

        contribution.Status = StayContributionStatus.Rejected;
        contribution.ReviewedByUserId = reviewerUserId;
        contribution.ReviewedAt = DateTime.UtcNow;
        contribution.ReviewNote = NormalizeReviewNote(request.Note);
        contribution.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return MapDetail(contribution);
    }

    private async Task<Place> PublishPlaceAsync(StayContribution contribution)
    {
        var now = DateTime.UtcNow;
        var location = await ResolveLocationAsync(contribution, now);
        var placeTypeId = await ResolvePlaceTypeIdAsync("Hotel", now);

        Place place;
        if (contribution.PublishedPlaceId.HasValue)
        {
            place = await _db.Places
                .Include(x => x.PlaceTags)
                .Include(x => x.PlaceAmenities)
                .Include(x => x.PlaceBookingPartners)
                .FirstOrDefaultAsync(x => x.Id == contribution.PublishedPlaceId.Value)
                ?? new Place { CreatedAt = now };

            if (place.Id == 0)
                await _db.Places.AddAsync(place);
        }
        else
        {
            place = new Place { CreatedAt = now };
            await _db.Places.AddAsync(place);
        }

        place.Name = contribution.Name;
        place.Subtitle = contribution.StayType;
        place.ImageUrl = contribution.CoverImageUrl;
        place.Address = contribution.Address;
        place.Description = contribution.Description;
        place.Number = contribution.Phone;
        place.ContactEmail = string.Empty;
        place.OpeningHours = null;
        place.PricePerNight = 0m;
        place.PlaceTypeId = placeTypeId;
        place.LocationId = location.Id;
        place.Latitude = contribution.Latitude ?? location.Latitude;
        place.Longitude = contribution.Longitude ?? location.Longitude;
        place.UpdatedAt = now;

        await _db.SaveChangesAsync();

        await ReplaceTagsAsync(place.Id, contribution);
        await ReplaceAmenitiesAsync(place.Id, contribution);
        await ReplaceBookingPartnersAsync(place.Id, contribution);

        await _db.SaveChangesAsync();
        return place;
    }

    private async Task<Location> ResolveLocationAsync(StayContribution contribution, DateTime now)
    {
        var regionName = FirstNonEmpty(contribution.Region, contribution.Country, "User Contributions");
        var normalizedRegionName = regionName.ToLowerInvariant();

        var region = await _db.Regions.FirstOrDefaultAsync(x => x.Name.ToLower() == normalizedRegionName);
        if (region is null)
        {
            region = new Region { Name = regionName, CreatedAt = now };
            await _db.Regions.AddAsync(region);
            await _db.SaveChangesAsync();
        }

        var destinationTypeId = await ResolveDestinationTypeIdAsync("City", now);
        var destinationName = contribution.City.Trim();
        var normalizedDestinationName = destinationName.ToLowerInvariant();

        var destination = await _db.Destinations
            .FirstOrDefaultAsync(x => x.RegionId == region.Id && x.Name.ToLower() == normalizedDestinationName);

        if (destination is null)
        {
            destination = new Destination
            {
                Name = destinationName,
                DestinationTypeId = destinationTypeId,
                RegionId = region.Id,
                Description = string.Join(", ", new[] { contribution.City, contribution.Region, contribution.Country }.Where(x => !string.IsNullOrWhiteSpace(x))),
                Latitude = contribution.Latitude ?? 0d,
                Longitude = contribution.Longitude ?? 0d,
                CreatedAt = now
            };
            await _db.Destinations.AddAsync(destination);
            await _db.SaveChangesAsync();
        }

        var locationTypeId = await ResolveLocationTypeIdAsync("Neighborhood", now);
        var locationName = FirstNonEmpty(contribution.Address, contribution.City);
        var normalizedLocationName = locationName.ToLowerInvariant();

        var location = await _db.Locations
            .FirstOrDefaultAsync(x => x.DestinationId == destination.Id && x.Name.ToLower() == normalizedLocationName);

        if (location is not null)
        {
            return location;
        }

        location = new Location
        {
            Name = locationName,
            DestinationId = destination.Id,
            LocationTypeId = locationTypeId,
            Latitude = contribution.Latitude ?? destination.Latitude,
            Longitude = contribution.Longitude ?? destination.Longitude,
            CreatedAt = now
        };

        await _db.Locations.AddAsync(location);
        await _db.SaveChangesAsync();

        return location;
    }

    private async Task<int> ResolveDestinationTypeIdAsync(string name, DateTime now)
    {
        var normalized = name.ToLowerInvariant();
        var destinationType = await _db.DestinationTypes.FirstOrDefaultAsync(x => x.Name.ToLower() == normalized);
        if (destinationType is not null)
            return destinationType.Id;

        destinationType = new DestinationType
        {
            Name = name,
            Description = $"{name} destination",
            CreatedAt = now
        };
        await _db.DestinationTypes.AddAsync(destinationType);
        await _db.SaveChangesAsync();
        return destinationType.Id;
    }

    private async Task<int> ResolveLocationTypeIdAsync(string name, DateTime now)
    {
        var normalized = name.ToLowerInvariant();
        var locationType = await _db.LocationTypes.FirstOrDefaultAsync(x => x.Name.ToLower() == normalized);
        if (locationType is not null)
            return locationType.Id;

        locationType = new LocationType
        {
            Name = name,
            Description = $"{name} location",
            CreatedAt = now
        };
        await _db.LocationTypes.AddAsync(locationType);
        await _db.SaveChangesAsync();
        return locationType.Id;
    }

    private async Task<int> ResolvePlaceTypeIdAsync(string name, DateTime now)
    {
        var normalized = name.ToLowerInvariant();
        var placeType = await _db.PlaceTypes.FirstOrDefaultAsync(x => x.Name.ToLower() == normalized);
        if (placeType is not null)
            return placeType.Id;

        placeType = new PlaceType
        {
            Name = name,
            Description = $"{name} places",
            CreatedAt = now
        };
        await _db.PlaceTypes.AddAsync(placeType);
        await _db.SaveChangesAsync();
        return placeType.Id;
    }

    private async Task ReplaceTagsAsync(int placeId, StayContribution contribution)
    {
        var existing = await _db.PlaceTags.Where(x => x.PlaceId == placeId).ToListAsync();
        _db.PlaceTags.RemoveRange(existing);

        var tagNames = DeserializeStringList(contribution.TagsJson);
        foreach (var tagName in tagNames)
        {
            var tagId = await ResolveTagIdAsync(tagName);
            await _db.PlaceTags.AddAsync(new PlaceTag { PlaceId = placeId, TagId = tagId });
        }
    }

    private async Task<int> ResolveTagIdAsync(string tagName)
    {
        var normalizedName = tagName.Trim().ToLowerInvariant();
        var tag = await _db.Tags.FirstOrDefaultAsync(x => x.Name.ToLower() == normalizedName || x.Slug.ToLower() == normalizedName);
        if (tag is not null)
            return tag.Id;

        var slug = Slugify(tagName);
        var slugBase = slug;
        var suffix = 2;
        while (await _db.Tags.AnyAsync(x => x.Slug == slug))
        {
            slug = $"{slugBase}-{suffix++}";
        }

        tag = new Tag
        {
            Name = tagName.Trim(),
            Slug = slug,
            Description = string.Empty,
            ForPlace = true,
            CreatedAt = DateTime.UtcNow
        };

        await _db.Tags.AddAsync(tag);
        await _db.SaveChangesAsync();
        return tag.Id;
    }

    private async Task ReplaceAmenitiesAsync(int placeId, StayContribution contribution)
    {
        var existing = await _db.PlaceAmenities.Where(x => x.PlaceId == placeId).ToListAsync();
        _db.PlaceAmenities.RemoveRange(existing);

        var amenityIcons = DeserializeStringList(contribution.AmenitiesJson)
            .Select(MapAmenityIcon)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var icon in amenityIcons)
        {
            var amenity = await _db.Amenities.FirstOrDefaultAsync(x => x.Icon.ToLower() == icon.ToLowerInvariant());
            if (amenity is null)
            {
                amenity = new Amenity { Name = ToTitle(icon), Icon = icon };
                await _db.Amenities.AddAsync(amenity);
                await _db.SaveChangesAsync();
            }

            await _db.PlaceAmenities.AddAsync(new PlaceAmenity { PlaceId = placeId, AmenityId = amenity.Id });
        }
    }

    private async Task ReplaceBookingPartnersAsync(int placeId, StayContribution contribution)
    {
        var existing = await _db.PlaceBookingPartners.Where(x => x.PlaceId == placeId).ToListAsync();
        _db.PlaceBookingPartners.RemoveRange(existing);

        var partners = DeserializeBookingPartners(contribution.BookingPartnersJson)
            .Where(x => AllowedPartnerIds.Contains(x.Id))
            .DistinctBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var partner in partners)
        {
            var name = PartnerName(partner.Id);
            var normalizedName = name.ToLowerInvariant();
            var bookingPartner = await _db.BookingPartners.FirstOrDefaultAsync(x => x.Name.ToLower() == normalizedName);
            if (bookingPartner is null)
            {
                bookingPartner = new BookingPartner { Name = name, Variant = PartnerVariant(partner.Id) };
                await _db.BookingPartners.AddAsync(bookingPartner);
                await _db.SaveChangesAsync();
            }

            await _db.PlaceBookingPartners.AddAsync(new PlaceBookingPartner
            {
                PlaceId = placeId,
                BookingPartnerId = bookingPartner.Id
            });
        }
    }

    private async Task EnsureRegionalManagerCanReviewAsync(int reviewerUserId, StayContribution contribution)
    {
        var reviewer = await EnsureRegionalManagerAsync(reviewerUserId);

        var assignedRegions = reviewer.RegionalOrganization?.RegionalOrganizationRegions
            .Select(x => x.Region.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (assignedRegions.Count == 0 || string.IsNullOrWhiteSpace(contribution.Region))
            return;

        if (!assignedRegions.Contains(contribution.Region))
            throw new UnauthorizedAccessException("Regional manager is not assigned to this contribution's region.");
    }

    private async Task<User> EnsureRegionalManagerAsync(int reviewerUserId)
    {
        var reviewer = await _db.Users
            .Include(x => x.UserRoles).ThenInclude(x => x.Role)
            .Include(x => x.RegionalOrganization)
                .ThenInclude(x => x!.RegionalOrganizationRegions)
                .ThenInclude(x => x.Region)
            .FirstOrDefaultAsync(x => x.Id == reviewerUserId)
            ?? throw new UnauthorizedAccessException("Invalid user token");

        if (!reviewer.UserRoles.Any(x => x.Role.Name == "RegionalManager"))
            throw new UnauthorizedAccessException("Regional manager role is required.");

        return reviewer;
    }

    private static void Validate(UpsertStayContributionDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new InvalidOperationException("Stay name is required.");

        if (request.Name.Trim().Length > 200)
            throw new InvalidOperationException("Stay name is too long.");

        if (string.IsNullOrWhiteSpace(request.StayType) || !AllowedStayTypes.Contains(request.StayType.Trim()))
            throw new InvalidOperationException("Invalid stay type.");

        if (request.Address.Trim().Length > 500)
            throw new InvalidOperationException("Address is too long.");

        if (string.IsNullOrWhiteSpace(request.City))
            throw new InvalidOperationException("City is required.");

        if (request.City.Trim().Length > 120)
            throw new InvalidOperationException("City is too long.");

        if (request.Region.Trim().Length > 120)
            throw new InvalidOperationException("Region is too long.");

        if (string.IsNullOrWhiteSpace(request.Country))
            throw new InvalidOperationException("Country is required.");

        if (request.Country.Trim().Length > 120)
            throw new InvalidOperationException("Country is too long.");

        if (request is { Latitude: null, Longitude: not null } or { Latitude: not null, Longitude: null })
            throw new InvalidOperationException("Both latitude and longitude are required when setting coordinates.");

        if (request.Latitude is < -90 or > 90)
            throw new InvalidOperationException("Latitude must be between -90 and 90.");

        if (request.Longitude is < -180 or > 180)
            throw new InvalidOperationException("Longitude must be between -180 and 180.");

        if (request.Description.Trim().Length > 2000)
            throw new InvalidOperationException("Description is too long.");

        if (request.Phone.Trim().Length > 50)
            throw new InvalidOperationException("Phone number is too long.");

        if (request.Website.Trim().Length > 500)
            throw new InvalidOperationException("Website is too long.");

        _ = NormalizeAmenities(request.Amenities);
        _ = NormalizeBookingPartners(request.BookingPartners);
    }

    private static List<string> NormalizeTags(IEnumerable<string>? tags) =>
        (tags ?? Enumerable.Empty<string>())
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Where(x => x.Length <= 80)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

    private static List<string> NormalizeAmenities(IEnumerable<string>? amenities)
    {
        var normalized = (amenities ?? Enumerable.Empty<string>())
            .Select(x => x.Trim().ToLowerInvariant())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Any(x => !AllowedAmenities.Contains(x)))
            throw new InvalidOperationException("Invalid amenity.");

        return normalized.Take(20).ToList();
    }

    private static List<ContributionBookingPartnerDto> NormalizeBookingPartners(IEnumerable<ContributionBookingPartnerDto>? partners)
    {
        var normalized = (partners ?? Enumerable.Empty<ContributionBookingPartnerDto>())
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .Select(x => new ContributionBookingPartnerDto
            {
                Id = x.Id.Trim().ToLowerInvariant(),
                Url = x.Url.Trim()
            })
            .DistinctBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Any(x => !AllowedPartnerIds.Contains(x.Id)))
            throw new InvalidOperationException("Invalid booking partner.");

        if (normalized.Any(x => x.Url.Length > 500))
            throw new InvalidOperationException("Booking partner URL is too long.");

        return normalized.Take(10).ToList();
    }

    private static string NormalizeWebsite(string? website)
    {
        var trimmed = website?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return string.Empty;

        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"https://{trimmed}";
    }

    private static string NormalizeImagePosition(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 40 ? "50% 50%" : trimmed;
    }

    private static string NormalizeImageSize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 40 ? "cover" : trimmed;
    }

    private static string NormalizeReviewNote(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length > 1000 ? trimmed[..1000] : trimmed;
    }

    private static StayContributionStatus ParseStatus(string status)
    {
        var normalized = status.Trim().Replace(" ", "", StringComparison.OrdinalIgnoreCase).Replace("-", "", StringComparison.OrdinalIgnoreCase);
        return normalized.ToLowerInvariant() switch
        {
            "pending" or "underreview" or "pendingreview" => StayContributionStatus.PendingReview,
            "approved" => StayContributionStatus.Approved,
            "rejected" => StayContributionStatus.Rejected,
            _ => throw new InvalidOperationException("Invalid contribution status.")
        };
    }

    private static StayContributionListItemDto MapListItem(StayContribution contribution) => new()
    {
        Id = contribution.Id,
        EntityType = "Stay",
        Name = contribution.Name,
        StayType = contribution.StayType,
        Location = FormatLocation(contribution),
        CoverImageUrl = contribution.CoverImageUrl,
        Status = contribution.Status.ToString(),
        SubmittedAt = contribution.SubmittedAt,
        UpdatedAt = contribution.UpdatedAt,
        PublishedPlaceId = contribution.PublishedPlaceId
    };

    private static StayContributionDetailDto MapDetail(StayContribution contribution) => new()
    {
        Id = contribution.Id,
        EntityType = "Stay",
        Name = contribution.Name,
        StayType = contribution.StayType,
        Location = FormatLocation(contribution),
        CoverImageUrl = contribution.CoverImageUrl,
        Status = contribution.Status.ToString(),
        SubmittedAt = contribution.SubmittedAt,
        UpdatedAt = contribution.UpdatedAt,
        PublishedPlaceId = contribution.PublishedPlaceId,
        Address = contribution.Address,
        City = contribution.City,
        Region = contribution.Region,
        Country = contribution.Country,
        Latitude = contribution.Latitude,
        Longitude = contribution.Longitude,
        ImagePosition = contribution.ImagePosition,
        ImageSize = contribution.ImageSize,
        Description = contribution.Description,
        Phone = contribution.Phone,
        Website = contribution.Website,
        Tags = DeserializeStringList(contribution.TagsJson),
        Amenities = DeserializeStringList(contribution.AmenitiesJson),
        BookingPartners = DeserializeBookingPartners(contribution.BookingPartnersJson),
        ReviewNote = contribution.ReviewNote,
        ReviewedAt = contribution.ReviewedAt
    };

    private static string FormatLocation(StayContribution contribution) =>
        string.Join(", ", new[] { contribution.City, contribution.Region, contribution.Country }
            .Where(x => !string.IsNullOrWhiteSpace(x)));

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static List<string> DeserializeStringList(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private static List<ContributionBookingPartnerDto> DeserializeBookingPartners(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<ContributionBookingPartnerDto>>(json, JsonOptions) ?? new List<ContributionBookingPartnerDto>();
        }
        catch (JsonException)
        {
            return new List<ContributionBookingPartnerDto>();
        }
    }

    private static string MapAmenityIcon(string icon) =>
        string.Equals(icon, "dining", StringComparison.OrdinalIgnoreCase) ? "restaurant" : icon;

    private static string PartnerName(string id) => id.ToLowerInvariant() switch
    {
        "booking" => "Booking.com",
        "airbnb" => "Airbnb",
        "hotel" => "Hotel Website",
        "expedia" => "Expedia",
        _ => ToTitle(id)
    };

    private static string PartnerVariant(string id) => id.ToLowerInvariant() switch
    {
        "airbnb" => "brand",
        "hotel" => "muted",
        "expedia" => "muted",
        _ => "dark"
    };

    private static string Slugify(string value)
    {
        var slug = Regex.Replace(value.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? $"tag-{Guid.NewGuid():N}" : slug;
    }

    private static string ToTitle(string value) =>
        string.Join(" ", value.Split('-', '_', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => char.ToUpperInvariant(x[0]) + x[1..]));

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;
}

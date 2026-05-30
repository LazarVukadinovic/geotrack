using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.DTOs.Contributions;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Tags;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Geotrack.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class OwnershipContributionSnapshotTests
{
    [Fact]
    public async Task Request_ownership_copies_place_images_amenities_and_characteristics()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateClaimablePlaceAsync(app);

        using var client = await LoginAsync(app, "marko.p@example.com", "password123");
        var created = await CreateOwnershipClaimAsync(client, fixture.PlaceId);

        Assert.Equal("ClaimOwnership", created.Contribution.Type);
        Assert.Equal(fixture.PlaceName, created.Contribution.Name);
        Assert.Equal(fixture.CoverImageUrl, created.Contribution.CoverImageUrl);
        Assert.Equal(fixture.CardImageUrl, created.Contribution.CardImageUrl);
        Assert.Contains(created.Contribution.Images, image =>
            image.Url == fixture.CoverImageUrl &&
            image.IsMain &&
            image.ImageRole == EntityImageRole.Cover &&
            image.SortOrder == 0 &&
            image.AltText == "Ownership cover");
        Assert.Contains(created.Contribution.Images, image =>
            image.Url == fixture.CardImageUrl &&
            image.ImageRole == EntityImageRole.Card &&
            image.SortOrder == 1);
        Assert.Contains("Historic", created.Contribution.Tags);
        Assert.Contains("Family Friendly", created.Contribution.Tags);
        Assert.Contains("WiFi", created.Contribution.Amenities);
        Assert.Contains("Outdoor Seating", created.Contribution.Amenities);

        var mine = await client.GetFromJsonAsync<GetPlaceContributionsResponseDto>("/api/contributions");
        Assert.NotNull(mine);
        var listItem = Assert.Single(mine!.Contributions, item => item.Id == created.Contribution.Id);
        Assert.Equal(fixture.CoverImageUrl, listItem.CoverImageUrl);
        Assert.Equal(fixture.CardImageUrl, listItem.CardImageUrl);

        var detail = await client.GetFromJsonAsync<PlaceContributionResponseDto>(
            $"/api/contributions/{created.Contribution.Id}");
        Assert.NotNull(detail);
        Assert.Contains("Historic", detail!.Contribution.Tags);
        Assert.Contains("WiFi", detail.Contribution.Amenities);
        Assert.Contains(detail.Contribution.Images, image => image.Url == fixture.GalleryImageUrl);

        await AssertOriginalPlaceSnapshotAsync(app, fixture, expectedName: fixture.PlaceName);
    }

    [Fact]
    public async Task Request_ownership_for_stay_copies_stay_amenities()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateClaimablePlaceAsync(app, stay: true);

        using var client = await LoginAsync(app, "marko.p@example.com", "password123");
        var created = await CreateOwnershipClaimAsync(client, fixture.PlaceId);

        Assert.Equal("ClaimOwnership", created.Contribution.Type);
        Assert.Equal(fixture.StayTypeId, created.Contribution.StayTypeId);
        Assert.Contains("free-wifi", created.Contribution.StayAmenities);
        Assert.Contains("spa", created.Contribution.StayAmenities);
        Assert.Contains("Historic", created.Contribution.Tags);
        Assert.Contains(created.Contribution.Images, image => image.Url == fixture.CoverImageUrl);
    }

    [Fact]
    public async Task Approving_ownership_claim_applies_edits_and_preserves_images_amenities_and_characteristics()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateClaimablePlaceAsync(app);

        using var contributor = await LoginAsync(app, "marko.p@example.com", "password123");
        var created = await CreateOwnershipClaimAsync(contributor, fixture.PlaceId);

        var editedName = $"{fixture.PlaceName} Edited";
        var edited = await UpdateOwnershipClaimAsync(contributor, created.Contribution, fixture, editedName);
        Assert.Equal(editedName, edited.Contribution.Name);
        Assert.Contains(edited.Contribution.Images, image => image.Url == fixture.CoverImageUrl);
        Assert.Contains("WiFi", edited.Contribution.Amenities);

        using var admin = await RegisterAdminAndLoginAsync(app);
        var approvalResponse = await admin.PostAsJsonAsync(
            $"/api/contributions/{created.Contribution.Id}/approve",
            new ReviewPlaceContributionRequestDto { Note = "Ownership verified." });
        approvalResponse.EnsureSuccessStatusCode();

        var approved = await approvalResponse.Content.ReadFromJsonAsync<PlaceContributionResponseDto>();
        Assert.NotNull(approved);
        Assert.Equal("Approved", approved!.Contribution.Status);

        await AssertOriginalPlaceSnapshotAsync(app, fixture, expectedName: editedName, expectOwner: true);
    }

    [Fact]
    public async Task Rejecting_ownership_claim_does_not_modify_original_place_data()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateClaimablePlaceAsync(app);

        using var contributor = await LoginAsync(app, "marko.p@example.com", "password123");
        var created = await CreateOwnershipClaimAsync(contributor, fixture.PlaceId);
        await UpdateOwnershipClaimAsync(contributor, created.Contribution, fixture, $"{fixture.PlaceName} Rejected Edit");

        using var admin = await RegisterAdminAndLoginAsync(app);
        var rejectionResponse = await admin.PostAsJsonAsync(
            $"/api/contributions/{created.Contribution.Id}/reject",
            new ReviewPlaceContributionRequestDto { Note = "Not enough proof." });
        rejectionResponse.EnsureSuccessStatusCode();

        var rejected = await rejectionResponse.Content.ReadFromJsonAsync<PlaceContributionResponseDto>();
        Assert.NotNull(rejected);
        Assert.Equal("Rejected", rejected!.Contribution.Status);

        await AssertOriginalPlaceSnapshotAsync(app, fixture, expectedName: fixture.PlaceName, expectOwner: false);
    }

    [Fact]
    public async Task Duplicate_active_ownership_request_returns_existing_snapshot_without_creating_another_row()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateClaimablePlaceAsync(app);

        using var client = await LoginAsync(app, "marko.p@example.com", "password123");
        var first = await CreateOwnershipClaimAsync(client, fixture.PlaceId);
        var second = await CreateOwnershipClaimAsync(client, fixture.PlaceId);

        Assert.Equal(first.Contribution.Id, second.Contribution.Id);
        Assert.Contains(second.Contribution.Images, image => image.Url == fixture.CoverImageUrl);
        Assert.Contains("WiFi", second.Contribution.Amenities);
        Assert.Contains("Historic", second.Contribution.Tags);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var activeClaims = await db.PlaceContributions
            .CountAsync(contribution =>
                contribution.PlaceId == fixture.PlaceId &&
                contribution.Type == PlaceContributionType.ClaimOwnership &&
                contribution.Status == PlaceContributionStatus.PendingReview);
        Assert.Equal(1, activeClaims);
    }

    private static async Task<PlaceContributionResponseDto> CreateOwnershipClaimAsync(
        HttpClient client,
        int placeId)
    {
        using var formData = new MultipartFormDataContent();
        formData.Add(
            new StringContent(JsonSerializer.Serialize(new
            {
                type = "ClaimOwnership",
                placeId
            }), Encoding.UTF8, "application/json"),
            "payload");
        AddOwnershipProofDocument(formData);

        var response = await client.PostAsync("/api/contributions/places", formData);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<PlaceContributionResponseDto>();
        Assert.NotNull(created);
        return created!;
    }

    private static async Task<PlaceContributionResponseDto> UpdateOwnershipClaimAsync(
        HttpClient client,
        PlaceContributionDetailDto contribution,
        OwnershipPlaceFixture fixture,
        string name)
    {
        using var formData = new MultipartFormDataContent();
        formData.Add(
            new StringContent(JsonSerializer.Serialize(new
            {
                type = "ClaimOwnership",
                placeId = fixture.PlaceId,
                placeTypeId = fixture.PlaceTypeId,
                locationId = fixture.LocationId,
                name,
                subtitle = "Edited ownership subtitle",
                address = "Edited ownership address",
                description = "Edited ownership description.",
                latitude = fixture.Latitude,
                longitude = fixture.Longitude,
                phone = "+385999000",
                contactEmail = "edited-owner@example.com",
                website = "https://edited-owner.example",
                tags = new[] { "Historic", "Family Friendly" },
                amenities = new[] { "WiFi", "Outdoor Seating" },
                images = contribution.Images.Select(image => new
                {
                    image.Url,
                    image.AltText,
                    image.Caption,
                    image.IsMain,
                    image.ImageRole,
                    image.SortOrder,
                    image.ImagePosition,
                    image.ImageSize
                }).ToArray()
            }), Encoding.UTF8, "application/json"),
            "payload");

        var response = await client.PutAsync($"/api/contributions/places/{contribution.Id}", formData);
        response.EnsureSuccessStatusCode();

        var updated = await response.Content.ReadFromJsonAsync<PlaceContributionResponseDto>();
        Assert.NotNull(updated);
        return updated!;
    }

    private static async Task<OwnershipPlaceFixture> CreateClaimablePlaceAsync(
        GeotrackApiFactory app,
        bool stay = false)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var suffix = Guid.NewGuid().ToString("N");

        var location = await db.Locations
            .Include(item => item.Destination)
            .OrderBy(item => item.Id)
            .FirstAsync();

        var placeType = stay
            ? await GetOrCreatePlaceTypeAsync(db, "Stay", isOwnable: true)
            : await GetOrCreatePlaceTypeAsync(db, $"Ownership Test Venue {suffix}", isOwnable: true);

        var place = new Place
        {
            Name = $"Ownership Snapshot {suffix}",
            Subtitle = "Original ownership subtitle",
            Address = "Original ownership address",
            OpeningHours = "09:00-17:00",
            Description = "Original ownership description.",
            PlaceTypeId = placeType.Id,
            LocationId = location.Id,
            Latitude = 42.6507,
            Longitude = 18.0944,
            ContactEmail = "original-owner@example.com",
            Number = "+385123456",
            Website = "https://original-owner.example",
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Places.Add(place);
        await db.SaveChangesAsync();

        int? stayTypeId = null;
        if (stay)
        {
            var stayType = await GetOrCreateStayTypeAsync(db, $"Ownership Stay {suffix}", $"ownership-stay-{suffix}");
            db.Stays.Add(new Stay
            {
                PlaceId = place.Id,
                StayTypeId = stayType.Id,
                PricePerNight = 120m,
                CheckInTime = "14:00",
                CheckOutTime = "10:00"
            });
            stayTypeId = stayType.Id;

            var wifi = await GetOrCreateStayAmenityAsync(db, "Free Wi-Fi", "free-wifi", "wifi");
            var spa = await GetOrCreateStayAmenityAsync(db, "Spa", "spa", "spa");
            db.StayStayAmenities.AddRange(
                new StayStayAmenity { StayPlaceId = place.Id, StayAmenityId = wifi.Id },
                new StayStayAmenity { StayPlaceId = place.Id, StayAmenityId = spa.Id });
        }
        else
        {
            var wifi = await GetOrCreateAmenityAsync(db, "WiFi", "wifi");
            var outdoor = await GetOrCreateAmenityAsync(db, "Outdoor Seating", "outdoor");
            db.PlaceAmenities.AddRange(
                new PlaceAmenity { PlaceId = place.Id, AmenityId = wifi.Id },
                new PlaceAmenity { PlaceId = place.Id, AmenityId = outdoor.Id });
        }

        var historic = await GetOrCreateTagAsync(db, "Historic", "historic");
        var family = await GetOrCreateTagAsync(db, "Family Friendly", "family-friendly");
        db.PlaceTags.AddRange(
            new PlaceTag { PlaceId = place.Id, TagId = historic.Id },
            new PlaceTag { PlaceId = place.Id, TagId = family.Id });

        var coverUrl = $"/media/test/ownership-cover-{suffix}.svg";
        var cardUrl = $"/media/test/ownership-card-{suffix}.webp";
        var galleryUrl = $"/media/test/ownership-gallery-{suffix}.webp";
        db.EntityImages.AddRange(
            new EntityImage
            {
                TargetType = EntityImageTargetType.Place,
                PlaceId = place.Id,
                Url = coverUrl,
                AltText = "Ownership cover",
                Caption = "Cover caption",
                IsMain = true,
                ImageRole = EntityImageRole.Cover,
                SortOrder = 0,
                ImagePosition = "45% 40%",
                ImageSize = "cover",
                CreatedAt = now
            },
            new EntityImage
            {
                TargetType = EntityImageTargetType.Place,
                PlaceId = place.Id,
                Url = cardUrl,
                AltText = "Ownership card",
                Caption = "Card caption",
                IsMain = false,
                ImageRole = EntityImageRole.Card,
                SortOrder = 1,
                ImagePosition = "50% 50%",
                ImageSize = "cover",
                CreatedAt = now.AddSeconds(1)
            },
            new EntityImage
            {
                TargetType = EntityImageTargetType.Place,
                PlaceId = place.Id,
                Url = galleryUrl,
                AltText = "Ownership gallery",
                Caption = "Gallery caption",
                IsMain = false,
                ImageRole = EntityImageRole.Gallery,
                SortOrder = 2,
                ImagePosition = "50% 50%",
                ImageSize = "cover",
                CreatedAt = now.AddSeconds(2)
            });

        await db.SaveChangesAsync();

        return new OwnershipPlaceFixture(
            place.Id,
            place.Name,
            placeType.Id,
            location.Id,
            stayTypeId,
            place.Latitude,
            place.Longitude,
            coverUrl,
            cardUrl,
            galleryUrl);
    }

    private static async Task AssertOriginalPlaceSnapshotAsync(
        GeotrackApiFactory app,
        OwnershipPlaceFixture fixture,
        string expectedName,
        bool expectOwner = false)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var place = await db.Places
            .Include(item => item.Images)
            .Include(item => item.PlaceTags)
                .ThenInclude(placeTag => placeTag.Tag)
            .Include(item => item.PlaceAmenities)
                .ThenInclude(placeAmenity => placeAmenity.Amenity)
            .SingleAsync(item => item.Id == fixture.PlaceId);

        Assert.Equal(expectedName, place.Name);
        Assert.Contains(place.Images, image => image.Url == fixture.CoverImageUrl && image.IsMain);
        Assert.Contains(place.Images, image => image.Url == fixture.CardImageUrl);
        Assert.Contains(place.Images, image => image.Url == fixture.GalleryImageUrl);
        Assert.Contains(place.PlaceTags, placeTag => placeTag.Tag.Name == "Historic");

        if (!fixture.StayTypeId.HasValue)
        {
            Assert.Contains(place.PlaceAmenities, placeAmenity => placeAmenity.Amenity.Name == "WiFi");
            Assert.Contains(place.PlaceAmenities, placeAmenity => placeAmenity.Amenity.Name == "Outdoor Seating");
        }

        var marko = await db.Users.SingleAsync(user => user.Email == "marko.p@example.com");
        var ownerExists = await db.UserPlaces.AnyAsync(userPlace =>
            userPlace.UserId == marko.Id &&
            userPlace.PlaceId == fixture.PlaceId);
        Assert.Equal(expectOwner, ownerExists);
    }

    private static async Task<HttpClient> LoginAsync(GeotrackApiFactory app, string email, string password)
    {
        var client = app.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequestDto
            {
                Email = email,
                Password = password
            });
        loginResponse.EnsureSuccessStatusCode();

        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(login);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    private static async Task<HttpClient> RegisterAdminAndLoginAsync(GeotrackApiFactory app)
    {
        var email = $"ownership-admin-{Guid.NewGuid():N}@example.com";

        using var registrationClient = app.CreateClient();
        var registerResponse = await registrationClient.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequestDto
            {
                FullName = "Ownership Admin",
                Email = email,
                Password = "Password123!",
            });
        registerResponse.EnsureSuccessStatusCode();

        await GrantRoleAsync(app, email, "Admin");
        return await LoginAsync(app, email, "Password123!");
    }

    private static async Task GrantRoleAsync(GeotrackApiFactory app, string email, string roleName)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await db.Users
            .Include(existingUser => existingUser.UserRoles)
            .SingleAsync(existingUser => existingUser.Email == email);

        var role = await db.Roles.SingleAsync(existingRole => existingRole.Name == roleName);
        if (user.UserRoles.Any(existingRole => existingRole.RoleId == role.Id))
            return;

        user.UserRoles.Add(new UserRole
        {
            UserId = user.Id,
            RoleId = role.Id,
            User = user,
            Role = role
        });

        await db.SaveChangesAsync();
    }

    private static void AddOwnershipProofDocument(MultipartFormDataContent formData)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF-1.4\n% GeoTrack ownership proof test document\n"));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        formData.Add(content, "ownershipProofDocument", "ownership-proof.pdf");
    }

    private static async Task<PlaceType> GetOrCreatePlaceTypeAsync(
        AppDbContext db,
        string name,
        bool isOwnable)
    {
        var placeType = await db.PlaceTypes.FirstOrDefaultAsync(item => item.Name == name);
        if (placeType is not null)
        {
            placeType.IsOwnable = isOwnable;
            await db.SaveChangesAsync();
            return placeType;
        }

        placeType = new PlaceType
        {
            Name = name,
            Description = $"{name} ownership test type",
            IsOwnable = isOwnable,
            CreatedAt = DateTime.UtcNow
        };
        db.PlaceTypes.Add(placeType);
        await db.SaveChangesAsync();
        return placeType;
    }

    private static async Task<StayType> GetOrCreateStayTypeAsync(AppDbContext db, string name, string slug)
    {
        var stayType = await db.StayTypes.FirstOrDefaultAsync(item => item.Slug == slug);
        if (stayType is not null)
            return stayType;

        stayType = new StayType
        {
            Name = name,
            Slug = slug,
            Description = "Ownership test stay type",
            CreatedAt = DateTime.UtcNow
        };
        db.StayTypes.Add(stayType);
        await db.SaveChangesAsync();
        return stayType;
    }

    private static async Task<Amenity> GetOrCreateAmenityAsync(AppDbContext db, string name, string icon)
    {
        var amenity = await db.Amenities.FirstOrDefaultAsync(item => item.Name == name);
        if (amenity is not null)
            return amenity;

        amenity = new Amenity
        {
            Name = name,
            Icon = icon
        };
        db.Amenities.Add(amenity);
        await db.SaveChangesAsync();
        return amenity;
    }

    private static async Task<StayAmenity> GetOrCreateStayAmenityAsync(
        AppDbContext db,
        string name,
        string slug,
        string icon)
    {
        var amenity = await db.StayAmenities.FirstOrDefaultAsync(item => item.Slug == slug);
        if (amenity is not null)
            return amenity;

        amenity = new StayAmenity
        {
            Name = name,
            Slug = slug,
            IconKey = icon,
            Description = $"{name} ownership test amenity"
        };
        db.StayAmenities.Add(amenity);
        await db.SaveChangesAsync();
        return amenity;
    }

    private static async Task<Tag> GetOrCreateTagAsync(AppDbContext db, string name, string slug)
    {
        var tag = await db.Tags.FirstOrDefaultAsync(item => item.Slug == slug);
        if (tag is not null)
            return tag;

        tag = new Tag
        {
            Name = name,
            Slug = slug,
            Description = $"{name} ownership test tag",
            ForPlace = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Tags.Add(tag);
        await db.SaveChangesAsync();
        return tag;
    }

    private sealed record OwnershipPlaceFixture(
        int PlaceId,
        string PlaceName,
        int PlaceTypeId,
        int LocationId,
        int? StayTypeId,
        double Latitude,
        double Longitude,
        string CoverImageUrl,
        string CardImageUrl,
        string GalleryImageUrl);
}

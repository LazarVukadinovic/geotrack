using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Geotrack.Application.DTOs.Auth;
using Geotrack.Application.DTOs.Places;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Geotrack.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class OwnerUpdateHardeningTests
{
    [Fact]
    public async Task Owner_update_validates_urls_sanitizes_description_and_creates_audit_log()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        var fixture = await CreateOwnedPlaceAsync(app);
        using var owner = await LoginAsync(app, "marko.p@example.com", "password123");

        var invalidWebsite = await owner.PutAsJsonAsync(
            $"/api/places/{fixture.PlaceId}/owner-update",
            new OwnerUpdatePlaceRequestDto
            {
                Website = "javascript:alert(1)"
            });

        Assert.Equal(HttpStatusCode.BadRequest, invalidWebsite.StatusCode);

        var updateResponse = await owner.PutAsJsonAsync(
            $"/api/places/{fixture.PlaceId}/owner-update",
            new OwnerUpdatePlaceRequestDto
            {
                Description = """
                    <p onclick="alert(1)">Owner <strong>safe</strong></p>
                    <script>alert(1)</script>
                    <a href="javascript:alert(1)">bad</a>
                    """,
                Website = "https://owner-update.example",
                ContactEmail = "OWNER@EXAMPLE.COM"
            });

        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<PlaceDetailDto>();
        Assert.NotNull(updated);
        Assert.Contains("<strong>safe</strong>", updated!.About);
        Assert.DoesNotContain("<script", updated.About, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", updated.About, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", updated.About, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("https://owner-update.example/", updated.Website);
        Assert.Equal("owner@example.com", updated.ContactEmail);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var place = await db.Places.AsNoTracking().SingleAsync(item => item.Id == fixture.PlaceId);

        Assert.Contains("<strong>safe</strong>", place.Description);
        Assert.DoesNotContain("<script", place.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", place.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", place.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("https://owner-update.example/", place.Website);
        Assert.Equal("owner@example.com", place.ContactEmail);

        var audit = await db.ModerationAuditLogs
            .AsNoTracking()
            .SingleAsync(log =>
                log.TargetType == "Place" &&
                log.TargetId == fixture.PlaceId &&
                log.Action == "OwnerUpdatedPlace");

        Assert.Equal(fixture.OwnerUserId, audit.ModeratorUserId);
        Assert.Equal("Published", audit.PreviousStatus);
        Assert.Equal("Published", audit.NewStatus);
        Assert.Equal("OwnerDirectUpdate", audit.SourceArea);
        Assert.Contains("Description", audit.MetadataJson);
        Assert.Contains("Website", audit.MetadataJson);
        Assert.Contains("ContactEmail", audit.MetadataJson);
    }

    private static async Task<OwnedPlaceFixture> CreateOwnedPlaceAsync(GeotrackApiFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var owner = await db.Users.SingleAsync(user => user.Email == "marko.p@example.com");
        var regionId = await db.Regions.OrderBy(region => region.Id).Select(region => region.Id).FirstAsync();
        var destinationTypeId = await db.DestinationTypes.OrderBy(type => type.Id).Select(type => type.Id).FirstAsync();
        var locationTypeId = await db.LocationTypes.OrderBy(type => type.Id).Select(type => type.Id).FirstAsync();
        var placeTypeId = await db.PlaceTypes.OrderBy(type => type.Id).Select(type => type.Id).FirstAsync();
        var now = DateTime.UtcNow.AddDays(-1);

        var destination = new Destination
        {
            Name = $"Owner update destination {Guid.NewGuid():N}",
            Summary = "Owner update destination.",
            Description = "Owner update destination.",
            Status = "Published",
            DestinationTypeId = destinationTypeId,
            RegionId = regionId,
            Latitude = 44.1,
            Longitude = 20.1,
            CreatedAt = now,
            PublishedAt = now
        };
        db.Destinations.Add(destination);
        await db.SaveChangesAsync();

        var location = new Location
        {
            Name = $"Owner update location {Guid.NewGuid():N}",
            Summary = "Owner update location.",
            Description = "Owner update location.",
            Status = "Published",
            DestinationId = destination.Id,
            LocationTypeId = locationTypeId,
            Latitude = 44.2,
            Longitude = 20.2,
            CreatedAt = now,
            PublishedAt = now
        };
        db.Locations.Add(location);
        await db.SaveChangesAsync();

        var place = new Place
        {
            Name = $"Owner update place {Guid.NewGuid():N}",
            Subtitle = "Owner update place.",
            Description = "Original owner update description.",
            Status = "Published",
            LocationId = location.Id,
            PlaceTypeId = placeTypeId,
            Latitude = 44.3,
            Longitude = 20.3,
            CreatedAt = now,
            PublishedAt = now
        };
        db.Places.Add(place);
        await db.SaveChangesAsync();

        db.UserPlaces.Add(new UserPlace
        {
            UserId = owner.Id,
            PlaceId = place.Id,
            CreatedAt = now
        });
        await db.SaveChangesAsync();

        return new OwnedPlaceFixture(place.Id, owner.Id);
    }

    private static async Task<HttpClient> LoginAsync(GeotrackApiFactory app, string email, string password)
    {
        var client = app.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequestDto
            {
                Email = email,
                Password = password,
                RememberMe = false
            });
        response.EnsureSuccessStatusCode();

        var session = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(session);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session!.AccessToken);

        return client;
    }

    private sealed record OwnedPlaceFixture(int PlaceId, int OwnerUserId);
}

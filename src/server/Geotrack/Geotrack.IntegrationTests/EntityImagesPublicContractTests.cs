using System.Net.Http.Json;
using Geotrack.Application.DTOs.Events;
using Geotrack.Application.DTOs.Places;
using Geotrack.Domain.Entities.Core;
using Geotrack.Infrastructure;
using Geotrack.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geotrack.IntegrationTests;

public sealed class EntityImagesPublicContractTests
{
    [Fact]
    public async Task Detail_endpoints_return_ordered_images_and_hero_for_place_hotel_and_event()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        int placeId;
        int hotelId;
        int eventId;
        List<string> expectedPlaceImages;
        List<string> expectedHotelImages;
        List<string> expectedEventImages;

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;

            var eventEntity = await db.Events
                .SingleAsync(@event => @event.Name == "Dubrovnik Summer Festival");
            db.EntityImages.Add(new EntityImage
            {
                TargetType = EntityImageTargetType.Event,
                EventId = eventEntity.Id,
                Url = "/media/seed/dubrovnik-summer-festival-gallery.svg",
                AltText = "Festival audience",
                Caption = "Opening night",
                IsMain = false,
                SortOrder = 1,
                CreatedAt = now.AddMinutes(1),
            });
            await db.SaveChangesAsync();

            var place = await db.Places
                .Include(entity => entity.Images)
                .SingleAsync(entity => entity.Name == "Banje Beach");
            var hotel = await db.Places
                .Include(entity => entity.Images)
                .SingleAsync(entity => entity.Name == "Villa Adriatica Luxury Resort");
            eventEntity = await db.Events
                .Include(entity => entity.Images)
                .SingleAsync(entity => entity.Name == "Dubrovnik Summer Festival");

            placeId = place.Id;
            hotelId = hotel.Id;
            eventId = eventEntity.Id;
            expectedPlaceImages = OrderedImageUrls(place.Images);
            expectedHotelImages = OrderedImageUrls(hotel.Images);
            expectedEventImages = OrderedImageUrls(eventEntity.Images);
        }

        var placeDetail = await client.GetFromJsonAsync<PlaceDetailDto>($"/api/places/{placeId}");
        var hotelDetail = await client.GetFromJsonAsync<HotelDetailDto>($"/api/places/hotels/{hotelId}");
        var eventDetail = await client.GetFromJsonAsync<EventDetailDto>($"/api/events/{eventId}");

        Assert.NotNull(placeDetail);
        Assert.NotNull(hotelDetail);
        Assert.NotNull(eventDetail);

        Assert.Equal(expectedPlaceImages, placeDetail!.Images.Select(image => image.Url).ToList());
        Assert.Equal(expectedPlaceImages[0], placeDetail.HeroImage);
        Assert.True(placeDetail.Images[0].IsMain);

        Assert.Equal(expectedHotelImages, hotelDetail!.Images.Select(image => image.Url).ToList());
        Assert.Equal(expectedHotelImages[0], hotelDetail.HeroImage);
        Assert.True(hotelDetail.Images[0].IsMain);

        Assert.Equal(expectedEventImages, eventDetail!.Images.Select(image => image.Url).ToList());
        Assert.Equal(expectedEventImages[0], eventDetail.HeroImage);
        Assert.True(eventDetail.Images[0].IsMain);
    }

    [Fact]
    public async Task Place_detail_without_images_returns_empty_images_and_no_hero()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var placeId = await CreatePlaceAsync(app, "Image Contract No Images", Array.Empty<EntityImage>());

        var detail = await client.GetFromJsonAsync<PlaceDetailDto>($"/api/places/{placeId}");

        Assert.NotNull(detail);
        Assert.Empty(detail!.Images);
        Assert.Null(detail.HeroImage);
    }

    [Fact]
    public async Task Place_detail_without_main_image_uses_first_ordered_image_as_hero()
    {
        await using var app = await GeotrackApiFactory.CreateAsync();
        using var client = app.CreateClient();

        var now = DateTime.UtcNow;
        var placeId = await CreatePlaceAsync(
            app,
            "Image Contract No Main",
            new[]
            {
                new EntityImage
                {
                    TargetType = EntityImageTargetType.Place,
                    Url = "/media/test/no-main-second.svg",
                    IsMain = false,
                    SortOrder = 2,
                    CreatedAt = now,
                },
                new EntityImage
                {
                    TargetType = EntityImageTargetType.Place,
                    Url = "/media/test/no-main-first.svg",
                    IsMain = false,
                    SortOrder = 1,
                    CreatedAt = now,
                },
            });

        var detail = await client.GetFromJsonAsync<PlaceDetailDto>($"/api/places/{placeId}");

        Assert.NotNull(detail);
        Assert.Equal(
            new[] { "/media/test/no-main-first.svg", "/media/test/no-main-second.svg" },
            detail!.Images.Select(image => image.Url).ToArray());
        Assert.Equal("/media/test/no-main-first.svg", detail.HeroImage);
        Assert.All(detail.Images, image => Assert.False(image.IsMain));
    }

    private static List<string> OrderedImageUrls(IEnumerable<EntityImage> images) =>
        images
            .OrderByDescending(image => image.IsMain)
            .ThenBy(image => image.SortOrder)
            .ThenBy(image => image.CreatedAt)
            .ThenBy(image => image.Id)
            .Select(image => image.Url)
            .ToList();

    private static async Task<int> CreatePlaceAsync(
        GeotrackApiFactory app,
        string name,
        IEnumerable<EntityImage> images)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var location = await db.Locations.FirstAsync();
        var placeTypeId = await db.PlaceTypes
            .OrderBy(type => type.Id)
            .Select(type => type.Id)
            .FirstAsync();

        var place = new Place
        {
            Name = name,
            Description = "Image contract test place.",
            PlaceTypeId = placeTypeId,
            LocationId = location.Id,
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            CreatedAt = now,
        };

        foreach (var image in images)
        {
            image.Place = place;
            image.PlaceId = null;
            image.TargetType = EntityImageTargetType.Place;
            place.Images.Add(image);
        }

        db.Places.Add(place);
        await db.SaveChangesAsync();
        return place.Id;
    }
}

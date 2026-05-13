using Geotrack.Application.Services;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Tags;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Server;

public static class SeedData
{
    private static readonly System.Threading.SemaphoreSlim InitializationLock = new(1, 1);

    private sealed record SeedTagDefinition(
        string Name,
        string Slug,
        bool ForDestination = false,
        bool ForLocation = false,
        bool ForPlace = false,
        bool ForEvent = false,
        string Description = "",
        string? ImageUrl = null);

    private static string U(string photoId, int w = 800) =>
        $"https://images.unsplash.com/photo-{photoId}?ixlib=rb-4.0.3&auto=format&fit=crop&w={w}&q=75";

    private static string SeedMedia(string fileName) => $"/media/seed/{fileName}.svg";

    public static async Task InitializeAsync(AppDbContext db)
    {
        await InitializationLock.WaitAsync();
        try
        {
            await InitializeCoreAsync(db);
        }
        finally
        {
            InitializationLock.Release();
        }
    }

    private static async Task InitializeCoreAsync(AppDbContext db)
    {
        await db.Database.MigrateAsync();

        if (await db.Destinations.AnyAsync())
        {
            await EnsureRolesAsync(db);
            await EnsureDemoUsersAsync(db);
            await EnsureSeedTagTypesAsync(db);
            await EnsureSeedTagsAsync(db);
            await EnsureSeedPlaceTypesAsync(db);
            await EnsureSeedStayTypesAsync(db);
            await EnsureDefaultModerationTermsAsync(db);
            return;
        }

        var now = DateTime.UtcNow;

        // Territory and region
        var croatia = new Territory
        {
            Name = "Croatia",
            CreatedAt = now
        };

        db.Territories.Add(croatia);
        await db.SaveChangesAsync();

        AddEntityImage(
            db,
            EntityImageTargetType.Territory,
            U("1500530855697-b586d89ba3ee"),
            isMain: true,
            sortOrder: 0,
            createdAt: now,
            territoryId: croatia.Id);

        var dalmatia = new Region
        {
            Name = "Dalmatia",
            TerritoryId = croatia.Id,
            CreatedAt = now
        };

        db.Regions.Add(dalmatia);
        await db.SaveChangesAsync();

        AddEntityImage(
            db,
            EntityImageTargetType.Region,
            U("1507525428034-b723cf961d3e"),
            isMain: true,
            sortOrder: 0,
            createdAt: now,
            regionId: dalmatia.Id);

        // Destination type
        var cityType = new DestinationType
        {
            Name = "City",
            Description = "Urban destination",
            CreatedAt = now
        };

        db.DestinationTypes.Add(cityType);
        await db.SaveChangesAsync();

        // Destination
        var dubrovnik = new Destination
        {
            Name = "Dubrovnik",
            DestinationTypeId = cityType.Id,
            RegionId = dalmatia.Id,
            Description = "The Pearl of the Adriatic, known for its Old Town walls and coastal views.",
            Latitude = 42.6507,
            Longitude = 18.0944,
            CreatedAt = now
        };

        db.Destinations.Add(dubrovnik);
        await db.SaveChangesAsync();

        AddEntityImage(
            db,
            EntityImageTargetType.Destination,
            U("1555990793-77610ee3d7c2"),
            isMain: true,
            sortOrder: 0,
            createdAt: now,
            destinationId: dubrovnik.Id);

        AddEntityImage(
            db,
            EntityImageTargetType.Destination,
            SeedMedia("dubrovnik-old-town"),
            isMain: false,
            sortOrder: 1,
            createdAt: now,
            destinationId: dubrovnik.Id);

        // Location types
        var neighborhoodType = new LocationType
        {
            Name = "Neighborhood",
            Description = "City neighborhood",
            CreatedAt = now
        };

        var districtType = new LocationType
        {
            Name = "District",
            Description = "City district",
            CreatedAt = now
        };

        db.LocationTypes.AddRange(neighborhoodType, districtType);
        await db.SaveChangesAsync();

        // Locations
        var oldTown = new Location
        {
            Name = "Old Town",
            DestinationId = dubrovnik.Id,
            LocationTypeId = neighborhoodType.Id,
            Latitude = 42.6411,
            Longitude = 18.1083,
            CreatedAt = now
        };

        var lapad = new Location
        {
            Name = "Lapad",
            DestinationId = dubrovnik.Id,
            LocationTypeId = districtType.Id,
            Latitude = 42.6585,
            Longitude = 18.0686,
            CreatedAt = now
        };

        var gruz = new Location
        {
            Name = "Gruz",
            DestinationId = dubrovnik.Id,
            LocationTypeId = districtType.Id,
            Latitude = 42.6597,
            Longitude = 18.0778,
            CreatedAt = now
        };

        db.Locations.AddRange(oldTown, lapad, gruz);
        await db.SaveChangesAsync();

        AddEntityImage(db, EntityImageTargetType.Location, SeedMedia("dubrovnik-old-town"), true, 0, now, locationId: oldTown.Id);
        AddEntityImage(db, EntityImageTargetType.Location, U("1596275281743-e4c30b6e0e27"), true, 0, now, locationId: lapad.Id);
        AddEntityImage(db, EntityImageTargetType.Location, U("1500530855697-b586d89ba3ee"), true, 0, now, locationId: gruz.Id);

        // Place types
        await EnsureSeedPlaceTypesAsync(db);
        var ptBeach = await db.PlaceTypes.SingleAsync(type => type.Name == "Beach");
        var ptRestaurant = await db.PlaceTypes.SingleAsync(type => type.Name == "Restaurant");
        var ptCafe = await db.PlaceTypes.SingleAsync(type => type.Name == "Cafe");
        var ptLandmark = await db.PlaceTypes.SingleAsync(type => type.Name == "Landmark");
        var ptNature = await db.PlaceTypes.SingleAsync(type => type.Name == "Nature");
        var ptStay = await db.PlaceTypes.SingleAsync(type => type.Name == "Stay");

        // Restaurant types
        var rtFineDining = new RestaurantType
        {
            Name = "Fine Dining",
            Slug = "fine-dining",
            Description = "Upscale restaurant experience",
            CreatedAt = now
        };

        var rtTraditional = new RestaurantType
        {
            Name = "Traditional",
            Slug = "traditional",
            Description = "Traditional local cuisine",
            CreatedAt = now
        };

        var rtCafeBistro = new RestaurantType
        {
            Name = "Cafe Bistro",
            Slug = "cafe-bistro",
            Description = "Casual cafe or bistro",
            CreatedAt = now
        };

        db.RestaurantTypes.AddRange(rtFineDining, rtTraditional, rtCafeBistro);
        await db.SaveChangesAsync();

        // Stay types
        await EnsureSeedStayTypesAsync(db);
        var hotelStayType = await db.StayTypes.SingleAsync(type => type.Slug == "hotel");
        var apartmentStayType = await db.StayTypes.SingleAsync(type => type.Slug == "apartment");
        var villaStayType = await db.StayTypes.SingleAsync(type => type.Slug == "villa");

        // Stay amenities
        var stayAmenities = new List<StayAmenity>
        {
            new() { Name = "Free WiFi", Slug = "free-wifi", IconKey = "wifi", Description = "Wireless internet access", CreatedAt = now },
            new() { Name = "Swimming Pool", Slug = "swimming-pool", IconKey = "pool", Description = "Pool available for guests", CreatedAt = now },
            new() { Name = "Breakfast Included", Slug = "breakfast-included", IconKey = "breakfast", Description = "Breakfast included in the stay", CreatedAt = now },
            new() { Name = "Parking", Slug = "parking", IconKey = "parking", Description = "Parking available", CreatedAt = now },
            new() { Name = "Spa", Slug = "spa", IconKey = "spa", Description = "Spa and wellness facilities", CreatedAt = now },
            new() { Name = "Fitness Center", Slug = "fitness-center", IconKey = "gym", Description = "Gym or fitness center available", CreatedAt = now },
            new() { Name = "Restaurant", Slug = "restaurant", IconKey = "restaurant", Description = "On-site restaurant", CreatedAt = now },
            new() { Name = "Air Conditioning", Slug = "air-conditioning", IconKey = "ac", Description = "Air-conditioned rooms", CreatedAt = now },
            new() { Name = "Sea View", Slug = "sea-view", IconKey = "sea", Description = "Sea view from room or property", CreatedAt = now }
        };

        db.StayAmenities.AddRange(stayAmenities);
        await db.SaveChangesAsync();

        StayAmenity SA(string slug) => stayAmenities.First(x => x.Slug == slug);

        // Generic place amenities
        var amenities = new List<Amenity>
        {
            new() { Name = "Free WiFi", Icon = "wifi" },
            new() { Name = "Parking", Icon = "parking" },
            new() { Name = "Restaurant", Icon = "restaurant" },
            new() { Name = "Air Conditioning", Icon = "ac" }
        };

        db.Amenities.AddRange(amenities);
        await db.SaveChangesAsync();

        Amenity A(string icon) => amenities.First(x => x.Icon == icon);

        // Booking partners
        var bpBooking = new BookingPartner { Name = "Booking.com", Variant = "dark" };
        var bpAirbnb = new BookingPartner { Name = "Airbnb", Variant = "brand" };
        var bpExpedia = new BookingPartner { Name = "Expedia", Variant = "muted" };

        db.BookingPartners.AddRange(bpBooking, bpAirbnb, bpExpedia);
        await db.SaveChangesAsync();

        await EnsureSeedTagTypesAsync(db);

        // Tags
        await EnsureSeedTagsAsync(db);
        var tags = await db.Tags.ToListAsync();

        Tag T(string slug) => tags.First(t => t.Slug == slug);

        // Event types
        var etFestival = new EventType { Name = "Festival", Description = "Music and cultural festivals", CreatedAt = now };
        var etMarket = new EventType { Name = "Market", Description = "Food and craft markets", CreatedAt = now };
        var etOutdoor = new EventType { Name = "Outdoor", Description = "Outdoor activities", CreatedAt = now };

        db.EventTypes.AddRange(etFestival, etMarket, etOutdoor);
        await db.SaveChangesAsync();

        await EnsureRolesAsync(db);

        // Demo users
        var hasher = new PasswordHasher<User>();

        var marko = new User
        {
            Username = "marko",
            Email = "marko.p@example.com",
            FirstName = "Marko",
            LastName = "Petrovic",
            PasswordHash = "",
            AvatarUrl = U("1507003211169-0a1dd7228f2d", 200),
            DisplayName = "marko_p",
            Location = "Belgrade, Serbia",
            CreatedAt = now
        };

        marko.PasswordHash = hasher.HashPassword(marko, "password123");

        var elena = new User
        {
            Username = "elena",
            Email = "elena@example.com",
            FirstName = "Elena",
            LastName = "Rossi",
            PasswordHash = "",
            AvatarUrl = U("1494790108377-be9c29b29330", 200),
            DisplayName = "elena_r",
            CreatedAt = now
        };

        elena.PasswordHash = hasher.HashPassword(elena, "password123");

        db.Users.AddRange(marko, elena);
        await db.SaveChangesAsync();

        // Places
        var banjeBeach = new Place
        {
            Name = "Banje Beach",
            Subtitle = "Dubrovnik's premier beach",
            PlaceTypeId = ptBeach.Id,
            LocationId = oldTown.Id,
            Description = "A popular beach with clear water and views of the Old Town and Lokrum Island.",
            Latitude = 42.6399,
            Longitude = 18.1131,
            OpeningHours = "06:00 - 22:00",
            AverageRating = 4.8,
            ReviewCount = 136,
            CreatedAt = now
        };

        var nautika = new Place
        {
            Name = "Nautika Restaurant",
            Subtitle = "Fine dining by the sea",
            PlaceTypeId = ptRestaurant.Id,
            LocationId = oldTown.Id,
            Description = "Mediterranean fine dining with views of the Adriatic and Lovrijenac Fortress.",
            Latitude = 42.6414,
            Longitude = 18.1064,
            OpeningHours = "12:00 - 23:00",
            AverageRating = 4.9,
            ReviewCount = 205,
            CreatedAt = now
        };

        var cogito = new Place
        {
            Name = "Cogito Coffee",
            Subtitle = "Specialty coffee in Old Town",
            PlaceTypeId = ptCafe.Id,
            LocationId = oldTown.Id,
            Description = "Specialty coffee shop in the heart of Old Town.",
            Latitude = 42.6413,
            Longitude = 18.1089,
            OpeningHours = "07:30 - 21:00",
            AverageRating = 4.8,
            ReviewCount = 98,
            CreatedAt = now
        };

        var oldTownWalls = new Place
        {
            Name = "Dubrovnik Old Town Walls",
            Subtitle = "Iconic medieval fortifications",
            PlaceTypeId = ptLandmark.Id,
            LocationId = oldTown.Id,
            Description = "Medieval walls surrounding the Old Town with panoramic city and sea views.",
            Latitude = 42.6411,
            Longitude = 18.1083,
            OpeningHours = "08:00 - 18:30",
            AverageRating = 4.9,
            ReviewCount = 423,
            CreatedAt = now
        };

        var lokrumIsland = new Place
        {
            Name = "Lokrum Island",
            Subtitle = "Nature paradise nearby",
            PlaceTypeId = ptNature.Id,
            LocationId = oldTown.Id,
            Description = "A small island nature reserve a short boat ride from Dubrovnik Old Town.",
            Latitude = 42.6275,
            Longitude = 18.1208,
            OpeningHours = "09:00 - 18:00",
            AverageRating = 4.9,
            ReviewCount = 320,
            CreatedAt = now
        };

        var villaAdriatica = new Place
        {
            Name = "Villa Adriatica Luxury Resort",
            Subtitle = "5-star luxury resort",
            PlaceTypeId = ptStay.Id,
            LocationId = lapad.Id,
            Description = "Luxury resort overlooking the Adriatic Sea with spa, pool and sea-view rooms.",
            Latitude = 42.6560,
            Longitude = 18.0630,
            AverageRating = 4.8,
            ReviewCount = 342,
            CreatedAt = now
        };

        var hotelExcelsior = new Place
        {
            Name = "Hotel Excelsior",
            Subtitle = "Historic luxury hotel",
            PlaceTypeId = ptStay.Id,
            LocationId = oldTown.Id,
            Description = "Historic luxury hotel near the Old Town with sea views and premium amenities.",
            Latitude = 42.6395,
            Longitude = 18.1138,
            AverageRating = 4.7,
            ReviewCount = 287,
            CreatedAt = now
        };

        var oldTownApartment = new Place
        {
            Name = "Old Town Stone Apartment",
            Subtitle = "Private apartment inside the city walls",
            PlaceTypeId = ptStay.Id,
            LocationId = oldTown.Id,
            Description = "A cozy private apartment in a renovated stone building inside Dubrovnik Old Town.",
            Latitude = 42.6412,
            Longitude = 18.1095,
            AverageRating = 4.6,
            ReviewCount = 74,
            CreatedAt = now
        };

        db.Places.AddRange(
            banjeBeach,
            nautika,
            cogito,
            oldTownWalls,
            lokrumIsland,
            villaAdriatica,
            hotelExcelsior,
            oldTownApartment);

        await db.SaveChangesAsync();

        // Public entity images
        AddEntityImage(db, EntityImageTargetType.Place, U("1507525428034-b723cf961d3e"), true, 0, now, placeId: banjeBeach.Id);
        AddEntityImage(db, EntityImageTargetType.Place, U("1519046904884-53103b34b206"), false, 1, now, placeId: banjeBeach.Id);
        AddEntityImage(db, EntityImageTargetType.Place, U("1414235077428-338989a2e8c0"), true, 0, now, placeId: nautika.Id);
        AddEntityImage(db, EntityImageTargetType.Place, U("1495474472287-4d71bcdd2085"), true, 0, now, placeId: cogito.Id);
        AddEntityImage(db, EntityImageTargetType.Place, SeedMedia("dubrovnik-old-town"), true, 0, now, placeId: oldTownWalls.Id);
        AddEntityImage(db, EntityImageTargetType.Place, U("1441974231531-c6227db76b6e"), true, 0, now, placeId: lokrumIsland.Id);
        AddEntityImage(db, EntityImageTargetType.Place, U("1566073771259-6a6300d8226c"), true, 0, now, placeId: villaAdriatica.Id);
        AddEntityImage(db, EntityImageTargetType.Place, U("1564501049412-61c2a3083791"), false, 1, now, placeId: villaAdriatica.Id);
        AddEntityImage(db, EntityImageTargetType.Place, SeedMedia("hotel-excelsior"), true, 0, now, placeId: hotelExcelsior.Id);
        AddEntityImage(db, EntityImageTargetType.Place, U("1520250497591-112f2f40a3f4"), true, 0, now, placeId: oldTownApartment.Id);

        await db.SaveChangesAsync();

        // Stays
        var villaStay = new Stay
        {
            PlaceId = villaAdriatica.Id,
            StayTypeId = villaStayType.Id,
            PricePerNight = 189,
            CheckInTime = "14:00",
            CheckOutTime = "11:00",
            CreatedAt = now
        };

        var excelsiorStay = new Stay
        {
            PlaceId = hotelExcelsior.Id,
            StayTypeId = hotelStayType.Id,
            PricePerNight = 165,
            CheckInTime = "15:00",
            CheckOutTime = "11:00",
            CreatedAt = now
        };

        var apartmentStay = new Stay
        {
            PlaceId = oldTownApartment.Id,
            StayTypeId = apartmentStayType.Id,
            PricePerNight = 95,
            CheckInTime = "14:00",
            CheckOutTime = "10:00",
            CreatedAt = now
        };

        db.Stays.AddRange(villaStay, excelsiorStay, apartmentStay);
        await db.SaveChangesAsync();

        AddStayAmenities(db, villaStay, SA("free-wifi"), SA("swimming-pool"), SA("breakfast-included"), SA("parking"), SA("spa"), SA("fitness-center"), SA("restaurant"), SA("air-conditioning"), SA("sea-view"));
        AddStayAmenities(db, excelsiorStay, SA("free-wifi"), SA("breakfast-included"), SA("spa"), SA("restaurant"), SA("air-conditioning"), SA("sea-view"));
        AddStayAmenities(db, apartmentStay, SA("free-wifi"), SA("air-conditioning"), SA("sea-view"));

        await db.SaveChangesAsync();

        // Place amenities and booking partners
        db.PlaceAmenities.AddRange(
            new PlaceAmenity { PlaceId = nautika.Id, AmenityId = A("wifi").Id },
            new PlaceAmenity { PlaceId = cogito.Id, AmenityId = A("wifi").Id }
        );

        db.PlaceBookingPartners.AddRange(
            new PlaceBookingPartner { PlaceId = villaAdriatica.Id, BookingPartnerId = bpBooking.Id, Url = "https://example.com/villa-adriatica-booking" },
            new PlaceBookingPartner { PlaceId = villaAdriatica.Id, BookingPartnerId = bpExpedia.Id, Url = "https://example.com/villa-adriatica-expedia" },
            new PlaceBookingPartner { PlaceId = hotelExcelsior.Id, BookingPartnerId = bpBooking.Id, Url = "https://example.com/hotel-excelsior-booking" },
            new PlaceBookingPartner { PlaceId = hotelExcelsior.Id, BookingPartnerId = bpExpedia.Id, Url = "https://example.com/hotel-excelsior-expedia" },
            new PlaceBookingPartner { PlaceId = oldTownApartment.Id, BookingPartnerId = bpAirbnb.Id, Url = "https://example.com/old-town-apartment-airbnb" }
        );

        await db.SaveChangesAsync();

        // UserPlace - if row exists, user can manage this place
        db.UserPlaces.AddRange(
            new UserPlace
            {
                UserId = marko.Id,
                PlaceId = villaAdriatica.Id,
                CreatedAt = now
            },
            new UserPlace
            {
                UserId = marko.Id,
                PlaceId = oldTownApartment.Id,
                CreatedAt = now
            }
        );

        await db.SaveChangesAsync();

        // Place tags
        AddPlaceTags(db, banjeBeach, T("beaches"), T("outdoor"), T("scenic-views"));
        AddPlaceTags(db, nautika, T("food-drink"), T("fine-dining"), T("sea-view"));
        AddPlaceTags(db, cogito, T("food-drink"));
        AddPlaceTags(db, oldTownWalls, T("history"), T("landmarks"), T("scenic-views"), T("culture"));
        AddPlaceTags(db, lokrumIsland, T("nature"), T("islands"), T("outdoor"));
        AddPlaceTags(db, villaAdriatica, T("luxury"), T("spa"), T("pool"), T("sea-view"), T("breakfast"));
        AddPlaceTags(db, hotelExcelsior, T("luxury"), T("sea-view"), T("spa"), T("breakfast"));
        AddPlaceTags(db, oldTownApartment, T("boutique"), T("sea-view"), T("budget"));

        await db.SaveChangesAsync();

        // Events
        var summerFest = new Event
        {
            Name = "Dubrovnik Summer Festival",
            EventTypeId = etFestival.Id,
            DestinationId = oldTown.DestinationId,
            LocationId = oldTown.Id,
            Summary = "A cultural summer festival with performances across historic Old Town venues.",
            Description = "A cultural summer festival with performances across historic venues in the Old Town.",
            StartDate = now.AddDays(5),
            EndDate = now.AddDays(47),
            TicketPrice = "EUR 25 - EUR 100",
            IsFree = false,
            Duration = "42 days",
            HostedBy = "Dubrovnik Summer Festival Foundation",
            VenueDescription = "Multiple venues across Old Town.",
            ArrivalTips = "Arrive early. Parking is limited near Old Town.",
            Accessibility = "Some venues offer wheelchair accessibility.",
            CreatedAt = now
        };

        var foodMarket = new Event
        {
            Name = "Old Town Food Market",
            EventTypeId = etMarket.Id,
            DestinationId = oldTown.DestinationId,
            LocationId = oldTown.Id,
            Summary = "Open-air food market with local produce, olive oils, cheeses and wines.",
            Description = "Open-air food market with local products, olive oils, cheeses and wines.",
            StartDate = now.AddDays(2),
            EndDate = now.AddDays(2).AddHours(8),
            TicketPrice = "Free entry",
            IsFree = true,
            Duration = "8 hours",
            HostedBy = "Dubrovnik City Market Association",
            CreatedAt = now
        };

        var kayakTour = new Event
        {
            Name = "Sea Kayaking Adventure",
            EventTypeId = etOutdoor.Id,
            DestinationId = oldTown.DestinationId,
            LocationId = oldTown.Id,
            Summary = "Guided sea kayaking along the Dubrovnik coastline.",
            Description = "Guided sea kayaking tour along the Dubrovnik coastline.",
            StartDate = now.AddDays(4),
            EndDate = now.AddDays(4).AddHours(4),
            TicketPrice = "EUR 35",
            IsFree = false,
            Duration = "4 hours",
            HostedBy = "Adriatic Kayak Tours",
            CreatedAt = now
        };

        db.Events.AddRange(summerFest, foodMarket, kayakTour);
        await db.SaveChangesAsync();

        AddEntityImage(db, EntityImageTargetType.Event, SeedMedia("dubrovnik-summer-festival"), true, 0, now, eventId: summerFest.Id);
        AddEntityImage(db, EntityImageTargetType.Event, U("1555939594-58d7cb561ad1"), true, 0, now, eventId: foodMarket.Id);
        AddEntityImage(db, EntityImageTargetType.Event, U("1544551763-46a013bb70d5"), true, 0, now, eventId: kayakTour.Id);

        await db.SaveChangesAsync();

        AddEventTags(db, summerFest, T("festivals"), T("culture"), T("outdoor"));
        AddEventTags(db, foodMarket, T("food-market"), T("food-drink"), T("outdoor"));
        AddEventTags(db, kayakTour, T("outdoor"), T("adventure"));

        await db.SaveChangesAsync();

        // Reviews
        db.PlaceReviews.AddRange(
            new PlaceReview
            {
                PlaceId = banjeBeach.Id,
                UserId = marko.Id,
                Rating = 5,
                Comment = "Beautiful beach with clear water and amazing views of the Old Town.",
                CreatedAt = now.AddDays(-10)
            },
            new PlaceReview
            {
                PlaceId = oldTownWalls.Id,
                UserId = elena.Id,
                Rating = 5,
                Comment = "Walking the walls was the highlight of the trip.",
                CreatedAt = now.AddDays(-8)
            },
            new PlaceReview
            {
                PlaceId = villaAdriatica.Id,
                UserId = elena.Id,
                Rating = 5,
                Comment = "Excellent stay, great sea view and very comfortable rooms.",
                CreatedAt = now.AddDays(-6)
            }
        );

        await db.SaveChangesAsync();

        // Destination tags
        db.DestinationTags.AddRange(
            new DestinationTag { DestinationId = dubrovnik.Id, TagId = T("history").Id },
            new DestinationTag { DestinationId = dubrovnik.Id, TagId = T("culture").Id },
            new DestinationTag { DestinationId = dubrovnik.Id, TagId = T("nature").Id },
            new DestinationTag { DestinationId = dubrovnik.Id, TagId = T("beaches").Id }
        );

        await db.SaveChangesAsync();

        // Demo pending contribution for a new restaurant
        var newRestaurantContribution = new PlaceContribution
        {
            Type = PlaceContributionType.CreatePlace,
            Status = PlaceContributionStatus.PendingReview,
            UserId = marko.Id,

            PlaceTypeId = ptRestaurant.Id,
            LocationId = oldTown.Id,
            RestaurantTypeId = rtTraditional.Id,

            Name = "Konoba Mare",
            Subtitle = "Traditional Dalmatian restaurant",
            Address = "Old Town, Dubrovnik",
            OpeningHours = "11:00 - 23:00",
            Description = "A proposed traditional restaurant with local seafood, olive oil and regional wines.",
            Latitude = 42.6410,
            Longitude = 18.1102,
            ContactEmail = "info@konobamare.example",
            PhoneNumber = "+385 20 000 000",
            Website = "https://example.com/konoba-mare",

            TagsJson = "[\"food-drink\",\"traditional\",\"sea-view\"]",
            AmenitiesJson = "[\"wifi\",\"restaurant\"]",
            BookingPartnersJson = "[]",

            SubmittedAt = now,
            CreatedAt = now
        };

        db.PlaceContributions.Add(newRestaurantContribution);
        await db.SaveChangesAsync();

        db.PlaceContributionImages.AddRange(
            new PlaceContributionImage
            {
                PlaceContributionId = newRestaurantContribution.Id,
                Url = U("1555396273-367ea4eb4db5"),
                IsMain = true,
                SortOrder = 0,
                CreatedAt = now
            },
            new PlaceContributionImage
            {
                PlaceContributionId = newRestaurantContribution.Id,
                Url = U("1559339352-11d035aa65de"),
                IsMain = false,
                SortOrder = 1,
                CreatedAt = now
            }
        );

        await db.SaveChangesAsync();

        // Demo pending place contribution that updates an existing stay.
        var updatePlaceContribution = new PlaceContribution
        {
            Type = PlaceContributionType.UpdatePlace,
            Status = PlaceContributionStatus.PendingReview,
            UserId = marko.Id,

            PlaceId = villaAdriatica.Id,
            PlaceTypeId = ptStay.Id,
            LocationId = lapad.Id,
            StayTypeId = villaStayType.Id,

            OriginalSnapshotJson = """
            {
              "name": "Villa Adriatica Luxury Resort",
              "pricePerNight": 189
            }
            """,

            Name = "Villa Adriatica Luxury Resort & Spa",
            Subtitle = "Updated luxury resort with wellness focus",
            Address = "Lapad, Dubrovnik",
            OpeningHours = "Open 24h",
            Description = "Updated description proposed by the linked user. Luxury resort with spa, sea views and premium wellness services.",
            Latitude = 42.6560,
            Longitude = 18.0630,
            ContactEmail = "reservations@villa-adriatica.example",
            PhoneNumber = "+385 20 111 111",
            Website = "https://example.com/villa-adriatica",
            PricePerNight = 210,
            CheckInTime = "15:00",
            CheckOutTime = "11:00",

            TagsJson = "[\"luxury\",\"spa\",\"sea-view\",\"breakfast\"]",
            StayAmenitiesJson = "[\"free-wifi\",\"spa\",\"sea-view\",\"breakfast-included\",\"air-conditioning\"]",
            BookingPartnersJson = "[\"booking\",\"expedia\"]",

            SubmittedAt = now,
            CreatedAt = now
        };

        db.PlaceContributions.Add(updatePlaceContribution);
        await db.SaveChangesAsync();

        db.PlaceContributionImages.AddRange(
            new PlaceContributionImage
            {
                PlaceContributionId = updatePlaceContribution.Id,
                Url = U("1566073771259-6a6300d8226c"),
                IsMain = true,
                SortOrder = 0,
                CreatedAt = now
            },
            new PlaceContributionImage
            {
                PlaceContributionId = updatePlaceContribution.Id,
                Url = U("1564501049412-61c2a3083791"),
                IsMain = false,
                SortOrder = 1,
                CreatedAt = now
            }
        );

        await db.SaveChangesAsync();
        await EnsureDefaultModerationTermsAsync(db);
    }

    private static void AddEntityImage(
        AppDbContext db,
        EntityImageTargetType targetType,
        string url,
        bool isMain,
        int sortOrder,
        DateTime createdAt,
        int? placeId = null,
        int? eventId = null,
        int? regionId = null,
        int? destinationId = null,
        int? territoryId = null,
        int? locationId = null)
    {
        db.EntityImages.Add(new EntityImage
        {
            TargetType = targetType,
            PlaceId = placeId,
            EventId = eventId,
            RegionId = regionId,
            DestinationId = destinationId,
            TerritoryId = territoryId,
            LocationId = locationId,
            Url = url,
            IsMain = isMain,
            SortOrder = sortOrder,
            CreatedAt = createdAt
        });
    }

    private static void AddStayAmenities(AppDbContext db, Stay stay, params StayAmenity[] amenities)
    {
        foreach (var amenity in amenities)
        {
            db.StayStayAmenities.Add(new StayStayAmenity
            {
                StayPlaceId = stay.PlaceId,
                StayAmenityId = amenity.Id
            });
        }
    }

    private static void AddPlaceTags(AppDbContext db, Place place, params Tag[] tags)
    {
        foreach (var tag in tags)
        {
            db.PlaceTags.Add(new PlaceTag
            {
                PlaceId = place.Id,
                TagId = tag.Id
            });
        }
    }

    private static void AddEventTags(AppDbContext db, Event eventEntity, params Tag[] tags)
    {
        foreach (var tag in tags)
        {
            db.EventTags.Add(new EventTag
            {
                EventId = eventEntity.Id,
                TagId = tag.Id
            });
        }
    }

    private static async Task EnsureRolesAsync(AppDbContext db)
    {
        await EnsureRoleAsync(db, "Admin", "Can manage users and administrative configuration.");
        await EnsureRoleAsync(db, "RegionalManager", "Can review and approve regional user contributions.");
        await EnsureRoleAsync(db, "TerritoryManager", "Can manage territories and assigned regions.");
        await EnsureRoleAsync(db, "Publisher", "Can publish and manage assigned places.");
        await EnsureRoleAsync(db, "RegisteredUser", "Regular authenticated user.");
    }

    private static async Task EnsureRoleAsync(AppDbContext db, string name, string description)
    {
        if (await db.Roles.AnyAsync(x => x.Name == name))
        {
            return;
        }

        db.Roles.Add(new Role
        {
            Name = name,
            Description = description,
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }

    private static async Task EnsureDefaultModerationTermsAsync(AppDbContext db)
    {
        var defaults = new (string Term, string LanguageCode, ModerationTermCategory Category, ModerationTermMatchType MatchType, int Severity)[]
        {
            ("shit", "en", ModerationTermCategory.Profanity, ModerationTermMatchType.Exact, 2),
            ("jebeno", "sr", ModerationTermCategory.Profanity, ModerationTermMatchType.Exact, 2)
        };

        var now = DateTime.UtcNow;
        var changed = false;

        foreach (var rule in defaults)
        {
            var normalized = ModerationTextNormalizer.NormalizeForModeration(rule.Term);
            var exists = await db.ModerationTerms.AnyAsync(term =>
                term.LanguageCode == rule.LanguageCode &&
                term.NormalizedTerm == normalized &&
                term.Category == rule.Category &&
                term.MatchType == rule.MatchType);

            if (exists)
                continue;

            db.ModerationTerms.Add(new ModerationTerm
            {
                Term = rule.Term,
                NormalizedTerm = normalized,
                LanguageCode = rule.LanguageCode,
                Category = rule.Category,
                MatchType = rule.MatchType,
                IsActive = true,
                Severity = rule.Severity,
                CreatedAt = now
            });
            changed = true;
        }

        if (changed)
        {
            await db.SaveChangesAsync();
        }
    }

    private static async Task EnsureDemoUsersAsync(AppDbContext db)
    {
        var hasher = new PasswordHasher<User>();
        var now = DateTime.UtcNow;

        if (!await db.Users.AnyAsync(x => x.Email == "marko.p@example.com"))
        {
            var marko = new User
            {
                Username = "marko",
                Email = "marko.p@example.com",
                FirstName = "Marko",
                LastName = "Petrovic",
                PasswordHash = "",
                AvatarUrl = U("1507003211169-0a1dd7228f2d", 200),
                DisplayName = "marko_p",
                Location = "Belgrade, Serbia",
                CreatedAt = now
            };

            marko.PasswordHash = hasher.HashPassword(marko, "password123");
            db.Users.Add(marko);
        }

        if (!await db.Users.AnyAsync(x => x.Email == "elena@example.com"))
        {
            var elena = new User
            {
                Username = "elena",
                Email = "elena@example.com",
                FirstName = "Elena",
                LastName = "Rossi",
                PasswordHash = "",
                AvatarUrl = U("1494790108377-be9c29b29330", 200),
                DisplayName = "elena_r",
                CreatedAt = now
            };

            elena.PasswordHash = hasher.HashPassword(elena, "password123");
            db.Users.Add(elena);
        }

        await db.SaveChangesAsync();
    }

    private static async Task EnsureSeedTagsAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        var existingTags = await db.Tags.ToListAsync();
        var existingBySlug = existingTags.ToDictionary(x => x.Slug, StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var definition in BuildSeedTagDefinitions())
        {
            if (!existingBySlug.TryGetValue(definition.Slug, out var tag))
            {
                db.Tags.Add(new Tag
                {
                    Name = definition.Name,
                    Slug = definition.Slug,
                    ForDestination = definition.ForDestination,
                    ForLocation = definition.ForLocation,
                    ForPlace = definition.ForPlace,
                    ForEvent = definition.ForEvent,
                    Description = definition.Description,
                    ImageUrl = definition.ImageUrl,
                    CreatedAt = now
                });
                changed = true;
                continue;
            }

            var nextForDestination = tag.ForDestination || definition.ForDestination;
            var nextForLocation = tag.ForLocation || definition.ForLocation;
            var nextForPlace = tag.ForPlace || definition.ForPlace;
            var nextForEvent = tag.ForEvent || definition.ForEvent;

            if (tag.ForDestination == nextForDestination &&
                tag.ForLocation == nextForLocation &&
                tag.ForPlace == nextForPlace &&
                tag.ForEvent == nextForEvent)
            {
                continue;
            }

            tag.ForDestination = nextForDestination;
            tag.ForLocation = nextForLocation;
            tag.ForPlace = nextForPlace;
            tag.ForEvent = nextForEvent;
            tag.UpdatedAt = now;
            changed = true;
        }

        if (changed)
        {
            await db.SaveChangesAsync();
        }
    }

    private static async Task EnsureSeedTagTypesAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        var defaults = new[]
        {
            new TagType
            {
                Name = "Destination",
                Slug = "destination",
                Description = "Tags that can classify destinations.",
                ForDestination = true,
                CreatedAt = now
            },
            new TagType
            {
                Name = "Location",
                Slug = "location",
                Description = "Tags that can classify locations.",
                ForLocation = true,
                CreatedAt = now
            },
            new TagType
            {
                Name = "Place",
                Slug = "place",
                Description = "Tags that can classify places.",
                ForPlace = true,
                CreatedAt = now
            },
            new TagType
            {
                Name = "Event",
                Slug = "event",
                Description = "Tags that can classify events.",
                ForEvent = true,
                CreatedAt = now
            }
        };

        var existingTypes = await db.TagTypes.ToListAsync();
        var existingBySlug = existingTypes.ToDictionary(x => x.Slug, StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var seed in defaults)
        {
            if (!existingBySlug.TryGetValue(seed.Slug, out var tagType))
            {
                db.TagTypes.Add(seed);
                changed = true;
                continue;
            }

            var updated = false;
            if (!tagType.ForDestination && seed.ForDestination)
            {
                tagType.ForDestination = true;
                updated = true;
            }
            if (!tagType.ForLocation && seed.ForLocation)
            {
                tagType.ForLocation = true;
                updated = true;
            }
            if (!tagType.ForPlace && seed.ForPlace)
            {
                tagType.ForPlace = true;
                updated = true;
            }
            if (!tagType.ForEvent && seed.ForEvent)
            {
                tagType.ForEvent = true;
                updated = true;
            }

            if (!updated)
            {
                continue;
            }

            tagType.UpdatedAt = now;
            changed = true;
        }

        if (changed)
        {
            await db.SaveChangesAsync();
        }
    }

    private static async Task EnsureSeedPlaceTypesAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        var defaults = new (string Name, string Description)[]
        {
            ("Beach", "Beaches and coastal areas"),
            ("Restaurant", "Dining establishments"),
            ("Cafe", "Cafes and coffee shops"),
            ("Landmark", "Landmarks and monuments"),
            ("Nature", "Nature spots"),
            ("Stay", "Hotels, apartments, villas and other accommodation"),
            ("Museum", "Museums and galleries"),
            ("Park", "Parks, gardens and green spaces")
        };

        var existing = await db.PlaceTypes.ToListAsync();
        var existingNames = existing
            .Select(type => type.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var (name, description) in defaults)
        {
            if (existingNames.Contains(name))
            {
                continue;
            }

            db.PlaceTypes.Add(new PlaceType
            {
                Name = name,
                Description = description,
                CreatedAt = now
            });
            existingNames.Add(name);
            changed = true;
        }

        if (changed)
        {
            await db.SaveChangesAsync();
        }
    }

    private static async Task EnsureSeedStayTypesAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        var defaults = new (string Name, string Slug, string Description)[]
        {
            ("Hotel", "hotel", "Hotel accommodation"),
            ("Apartment", "apartment", "Private apartment accommodation"),
            ("Villa", "villa", "Private villa accommodation")
        };

        var existing = await db.StayTypes.ToListAsync();
        var existingSlugs = existing
            .Select(type => type.Slug)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var (name, slug, description) in defaults)
        {
            if (existingSlugs.Contains(slug))
            {
                continue;
            }

            db.StayTypes.Add(new StayType
            {
                Name = name,
                Slug = slug,
                Description = description,
                CreatedAt = now
            });
            existingSlugs.Add(slug);
            changed = true;
        }

        if (changed)
        {
            await db.SaveChangesAsync();
        }
    }

    private static List<Tag> BuildSeedTags(DateTime createdAt)
    {
        return BuildSeedTagDefinitions()
            .Select(definition => new Tag
            {
                Name = definition.Name,
                Slug = definition.Slug,
                ForDestination = definition.ForDestination,
                ForLocation = definition.ForLocation,
                ForPlace = definition.ForPlace,
                ForEvent = definition.ForEvent,
                Description = definition.Description,
                ImageUrl = definition.ImageUrl,
                CreatedAt = createdAt
            })
            .ToList();
    }

    private static List<SeedTagDefinition> BuildSeedTagDefinitions()
    {
        return new List<SeedTagDefinition>
        {
            new("Food & Drink", "food-drink", ForPlace: true, ForEvent: true, ImageUrl: U("1414235077428-338989a2e8c0")),
            new("Restaurant", "restaurant", ForPlace: true),
            new("Cafe", "cafe", ForPlace: true),
            new("Accommodation", "accommodation", ForPlace: true),
            new("Nature & Outdoors", "nature-outdoors", ForDestination: true, ForLocation: true, ForPlace: true, ForEvent: true),
            new("Culture & Heritage", "culture-heritage", ForDestination: true, ForLocation: true, ForPlace: true, ForEvent: true),
            new("Urban & City", "urban-city", ForDestination: true, ForLocation: true, ForPlace: true),
            new("Urban Center", "urban-center", ForLocation: true, ForPlace: true),
            new("Mountain & Forest", "mountain-forest", ForDestination: true, ForLocation: true, ForPlace: true),
            new("National Park Access", "national-park-access", ForDestination: true, ForLocation: true, ForPlace: true),
            new("Adventure & Sport", "adventure-sport", ForDestination: true, ForLocation: true, ForPlace: true, ForEvent: true),
            new("Family Friendly", "family-friendly", ForDestination: true, ForLocation: true, ForPlace: true, ForEvent: true),
            new("Accessibility Friendly", "accessibility-friendly", ForDestination: true, ForLocation: true, ForPlace: true),
            new("Historic", "historic", ForDestination: true, ForLocation: true, ForPlace: true),
            new("Coastal", "coastal", ForDestination: true, ForLocation: true, ForPlace: true),
            new("Food & Wine", "food-wine", ForDestination: true, ForLocation: true, ForPlace: true, ForEvent: true),
            new("Museums", "museums", ForDestination: true, ForLocation: true, ForPlace: true),
            new("Wellness", "wellness", ForDestination: true, ForLocation: true, ForPlace: true),
            new("History", "history", ForDestination: true, ForPlace: true, ForEvent: true, ImageUrl: SeedMedia("dubrovnik-old-town")),
            new("Beaches", "beaches", ForDestination: true, ForPlace: true, ImageUrl: U("1507525428034-b723cf961d3e")),
            new("Nature", "nature", ForDestination: true, ForLocation: true, ForPlace: true, ImageUrl: U("1441974231531-c6227db76b6e")),
            new("Culture", "culture", ForDestination: true, ForPlace: true, ForEvent: true),
            new("Outdoor", "outdoor", ForLocation: true, ForPlace: true, ForEvent: true),
            new("Adventure", "adventure", ForPlace: true, ForEvent: true),
            new("Music", "music", ForEvent: true),
            new("Festivals", "festivals", ForEvent: true),
            new("Festival", "festival", ForEvent: true),
            new("Concert", "concert", ForEvent: true),
            new("Sports", "sports", ForEvent: true),
            new("Exhibition", "exhibition", ForEvent: true),
            new("Food & Market", "food-market", ForEvent: true),
            new("Nightlife", "nightlife", ForEvent: true),
            new("Workshop", "workshop", ForEvent: true),
            new("Community", "community", ForEvent: true),
            new("Art", "art", ForEvent: true),
            new("Seasonal", "seasonal", ForEvent: true),
            new("Free Entry", "free-entry", ForEvent: true),
            new("Ticketed", "ticketed", ForEvent: true),
            new("Fine Dining", "fine-dining", ForPlace: true),
            new("Landmarks", "landmarks", ForLocation: true, ForPlace: true),
            new("Islands", "islands", ForDestination: true, ForPlace: true),
            new("Scenic Views", "scenic-views", ForDestination: true, ForLocation: true, ForPlace: true),
            new("Park", "park", ForLocation: true, ForPlace: true),
            new("Museum", "museum", ForLocation: true, ForPlace: true),
            new("Cultural Site", "cultural-site", ForLocation: true, ForPlace: true),
            new("Urban Area", "urban-area", ForLocation: true, ForPlace: true),
            new("Old Town", "old-town", ForLocation: true, ForPlace: true),
            new("Waterfront", "waterfront", ForLocation: true, ForPlace: true),
            new("Walking Area", "walking-area", ForLocation: true, ForPlace: true),
            new("Religious Heritage", "religious-heritage", ForLocation: true, ForPlace: true),
            new("Landmark", "landmark", ForLocation: true, ForPlace: true),
            new("Luxury", "luxury", ForPlace: true),
            new("Budget", "budget", ForPlace: true),
            new("Budget-Friendly", "budget-friendly", ForPlace: true),
            new("Boutique", "boutique", ForPlace: true),
            new("Romantic", "romantic", ForPlace: true),
            new("Pet Friendly", "pet-friendly", ForPlace: true),
            new("Spa", "spa", ForPlace: true),
            new("Pool", "pool", ForPlace: true),
            new("Breakfast", "breakfast", ForPlace: true),
            new("Sea View", "sea-view", ForPlace: true),
            new("Traditional", "traditional", ForPlace: true)
        };
    }
}

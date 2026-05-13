using Geotrack.Application.Services;
using Geotrack.Domain.Entities;
using Geotrack.Domain.Entities.Analytics;
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
    private static readonly SemaphoreSlim InitializationLock = new(1, 1);

    private sealed record SeedTagTypeDefinition(
        string Name,
        string Slug,
        bool ForDestination = false,
        bool ForLocation = false,
        bool ForPlace = false,
        bool ForEvent = false,
        string Description = "",
        string[]? AllowedPlaceTypes = null);

    private sealed record SeedTagDefinition(
        string Name,
        string Slug,
        string TagTypeSlug,
        bool ForDestination = false,
        bool ForLocation = false,
        bool ForPlace = false,
        bool ForEvent = false,
        bool CanGenerateExploreSection = false,
        string Description = "");

    private static string Unsplash(string photoId, int width = 900) =>
        $"https://images.unsplash.com/photo-{photoId}?auto=format&fit=crop&w={width}&q=80";

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
            var tagLookup1 = await EnsureSmartTagTaxonomyAsync(db);
            await EnsureExploreDemoPlaceTagsAsync(db, tagLookup1);
            await EnsureAdditionalExploreDemoDataAsync(db, tagLookup1);
            await EnsureCoreEntityImagesAsync(db);
            await EnsureDefaultModerationTermsAsync(db);
            return;
        }

        var now = DateTime.UtcNow;

        var croatia = new Territory
        {
            Name = "Croatia",
            CreatedAt = now
        };

        db.Territories.Add(croatia);
        await db.SaveChangesAsync();

        var dalmatia = new Region
        {
            Name = "Dalmatia",
            TerritoryId = croatia.Id,
            CreatedAt = now
        };

        db.Regions.Add(dalmatia);
        await db.SaveChangesAsync();

        var cityType = new DestinationType
        {
            Name = "City",
            Description = "Urban destination",
            CreatedAt = now
        };

        db.DestinationTypes.Add(cityType);
        await db.SaveChangesAsync();

        var dubrovnik = new Destination
        {
            Name = "Dubrovnik",
            DestinationTypeId = cityType.Id,
            RegionId = dalmatia.Id,
            Description = "A compact Adriatic heritage city known for stone walls, coastal views and island escapes.",
            Latitude = 42.6507,
            Longitude = 18.0944,
            CreatedAt = now
        };

        db.Destinations.Add(dubrovnik);
        await db.SaveChangesAsync();

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

        var harborType = new LocationType
        {
            Name = "Harbor Area",
            Description = "Harbor or port-side location",
            CreatedAt = now
        };

        db.LocationTypes.AddRange(neighborhoodType, districtType, harborType);
        await db.SaveChangesAsync();

        var oldTown = new Location
        {
            Name = "Old Town",
            DestinationId = dubrovnik.Id,
            LocationTypeId = neighborhoodType.Id,
            Latitude = 42.6413,
            Longitude = 18.1090,
            CreatedAt = now
        };

        var ploce = new Location
        {
            Name = "Ploce",
            DestinationId = dubrovnik.Id,
            LocationTypeId = districtType.Id,
            Latitude = 42.6422,
            Longitude = 18.1180,
            CreatedAt = now
        };

        var lapad = new Location
        {
            Name = "Lapad",
            DestinationId = dubrovnik.Id,
            LocationTypeId = districtType.Id,
            Latitude = 42.6559,
            Longitude = 18.0712,
            CreatedAt = now
        };

        var gruz = new Location
        {
            Name = "Gruz",
            DestinationId = dubrovnik.Id,
            LocationTypeId = harborType.Id,
            Latitude = 42.6592,
            Longitude = 18.0863,
            CreatedAt = now
        };

        db.Locations.AddRange(oldTown, ploce, lapad, gruz);
        await db.SaveChangesAsync();

        var ptBeach = new PlaceType { Name = "Beach", Description = "Beaches and coastal swimming areas", CreatedAt = now };
        var ptRestaurant = new PlaceType { Name = "Restaurant", Description = "Dining establishments", CreatedAt = now };
        var ptCafe = new PlaceType { Name = "Cafe", Description = "Cafes and coffee shops", CreatedAt = now };
        var ptLandmark = new PlaceType { Name = "Landmark", Description = "Landmarks, monuments and historical places", CreatedAt = now };
        var ptNature = new PlaceType { Name = "Nature", Description = "Nature spots and island escapes", CreatedAt = now };
        var ptStay = new PlaceType { Name = "Stay", Description = "Hotels, apartments, villas and other accommodation", CreatedAt = now };
        var ptActivity = new PlaceType { Name = "Activity", Description = "Activity providers, tours and outdoor experiences", CreatedAt = now };
        var ptBar = new PlaceType { Name = "Bar", Description = "Bars, wine bars and nightlife venues", CreatedAt = now };
        var ptMuseum = new PlaceType { Name = "Museum", Description = "Museums and cultural institutions", CreatedAt = now };
        var ptGallery = new PlaceType { Name = "Gallery", Description = "Art galleries and exhibition spaces", CreatedAt = now };

        db.PlaceTypes.AddRange(ptBeach, ptRestaurant, ptCafe, ptLandmark, ptNature, ptStay, ptActivity, ptBar, ptMuseum, ptGallery);
        await db.SaveChangesAsync();

        var tagLookup = await EnsureSmartTagTaxonomyAsync(db);

        var hotelStayType = new StayType
        {
            Name = "Hotel",
            Slug = "hotel",
            Description = "Hotel accommodation",
            CreatedAt = now
        };

        var apartmentStayType = new StayType
        {
            Name = "Apartment",
            Slug = "apartment",
            Description = "Private apartment accommodation",
            CreatedAt = now
        };

        var villaStayType = new StayType
        {
            Name = "Villa",
            Slug = "villa",
            Description = "Private villa accommodation",
            CreatedAt = now
        };

        db.StayTypes.AddRange(hotelStayType, apartmentStayType, villaStayType);
        await db.SaveChangesAsync();

        var stayAmenities = new List<StayAmenity>
        {
            new() { Name = "Free WiFi", Slug = "free-wifi", IconKey = "wifi", Description = "Wireless internet access", CreatedAt = now },
            new() { Name = "Swimming Pool", Slug = "swimming-pool", IconKey = "pool", Description = "Pool available for guests", CreatedAt = now },
            new() { Name = "Breakfast Included", Slug = "breakfast-included", IconKey = "breakfast", Description = "Breakfast included in the stay", CreatedAt = now },
            new() { Name = "Parking", Slug = "parking", IconKey = "parking", Description = "Parking available", CreatedAt = now },
            new() { Name = "Spa", Slug = "spa", IconKey = "spa", Description = "Spa and wellness facilities", CreatedAt = now },
            new() { Name = "Air Conditioning", Slug = "air-conditioning", IconKey = "ac", Description = "Air-conditioned rooms", CreatedAt = now }
        };

        db.StayAmenities.AddRange(stayAmenities);
        await db.SaveChangesAsync();

        StayAmenity SA(string slug) => stayAmenities.First(x => x.Slug == slug);

        var amenities = new List<Amenity>
        {
            new() { Name = "Free WiFi", Icon = "wifi" },
            new() { Name = "Outdoor Seating", Icon = "outdoor" },
            new() { Name = "Sea View", Icon = "sea" },
            new() { Name = "Family Friendly", Icon = "family" }
        };

        db.Amenities.AddRange(amenities);
        await db.SaveChangesAsync();

        Amenity A(string icon) => amenities.First(x => x.Icon == icon);

        var bpBooking = new BookingPartner { Name = "Booking.com", Variant = "dark" };
        var bpAirbnb = new BookingPartner { Name = "Airbnb", Variant = "brand" };
        var bpExpedia = new BookingPartner { Name = "Expedia", Variant = "muted" };

        db.BookingPartners.AddRange(bpBooking, bpAirbnb, bpExpedia);
        await db.SaveChangesAsync();

        var etFestival = new EventType { Name = "Festival", Description = "Music and cultural festivals", CreatedAt = now };
        var etMarket = new EventType { Name = "Market", Description = "Food and craft markets", CreatedAt = now };
        var etOutdoor = new EventType { Name = "Outdoor", Description = "Outdoor activities", CreatedAt = now };
        var etMusic = new EventType { Name = "Music", Description = "Music and live performance events", CreatedAt = now };

        db.EventTypes.AddRange(etFestival, etMarket, etOutdoor, etMusic);
        await db.SaveChangesAsync();

        await EnsureRolesAsync(db);

        var hasher = new PasswordHasher<User>();

        var marko = new User
        {
            Username = "marko",
            Email = "marko.p@example.com",
            FirstName = "Marko",
            LastName = "Petrovic",
            PasswordHash = "",
            DisplayName = "marko_p",
            Location = "Belgrade, Serbia",
            NeedsInterestsOnboarding = false,
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
            DisplayName = "elena_r",
            NeedsInterestsOnboarding = false,
            CreatedAt = now
        };
        elena.PasswordHash = hasher.HashPassword(elena, "password123");

        db.Users.AddRange(marko, elena);
        await db.SaveChangesAsync();

        var banjeBeach = new Place
        {
            Name = "Banje Beach",
            Subtitle = "City beach near Ploce",
            Address = "Banje Beach, Dubrovnik",
            PlaceTypeId = ptBeach.Id,
            LocationId = ploce.Id,
            Description = "A popular pebble beach near the Old Town with clear water and views toward Lokrum.",
            Latitude = 42.6413,
            Longitude = 18.1179,
            OpeningHours = "06:00 - 22:00",
            AverageRating = 4.8,
            ReviewCount = 136,
            CreatedAt = now
        };

        var nautika = new Place
        {
            Name = "Nautika Restaurant",
            Subtitle = "Fine dining by Pile Gate",
            Address = "Brsalje ul. 3, Dubrovnik",
            PlaceTypeId = ptRestaurant.Id,
            LocationId = oldTown.Id,
            Description = "Mediterranean fine dining near the western entrance of the Old Town and Lovrijenac Fortress.",
            Latitude = 42.6415,
            Longitude = 18.1063,
            OpeningHours = "12:00 - 23:00",
            AverageRating = 4.9,
            ReviewCount = 205,
            CreatedAt = now
        };

        var konobaPupo = new Place
        {
            Name = "Konoba Pupo",
            Subtitle = "Traditional local restaurant",
            Address = "Old Town, Dubrovnik",
            PlaceTypeId = ptRestaurant.Id,
            LocationId = oldTown.Id,
            Description = "Casual traditional restaurant in the Old Town with Dalmatian dishes and a local feel.",
            Latitude = 42.6414,
            Longitude = 18.1092,
            OpeningHours = "11:00 - 23:00",
            AverageRating = 4.5,
            ReviewCount = 112,
            CreatedAt = now
        };

        var cogito = new Place
        {
            Name = "Cogito Coffee",
            Subtitle = "Specialty coffee in Old Town",
            Address = "Old Town, Dubrovnik",
            PlaceTypeId = ptCafe.Id,
            LocationId = oldTown.Id,
            Description = "Small specialty coffee stop in the pedestrian Old Town core.",
            Latitude = 42.6413,
            Longitude = 18.1089,
            OpeningHours = "07:30 - 21:00",
            AverageRating = 4.8,
            ReviewCount = 98,
            CreatedAt = now
        };

        var cityWalls = new Place
        {
            Name = "Dubrovnik City Walls",
            Subtitle = "Historic walls around Old Town",
            Address = "Old Town, Dubrovnik",
            PlaceTypeId = ptLandmark.Id,
            LocationId = oldTown.Id,
            Description = "Medieval defensive walls surrounding the Old Town with panoramic city and sea views.",
            Latitude = 42.6420,
            Longitude = 18.1116,
            OpeningHours = "08:00 - 18:30",
            AverageRating = 4.9,
            ReviewCount = 423,
            CreatedAt = now
        };

        var lokrumIsland = new Place
        {
            Name = "Lokrum Island",
            Subtitle = "Island nature escape",
            Address = "Lokrum Island, Dubrovnik",
            PlaceTypeId = ptNature.Id,
            LocationId = oldTown.Id,
            Description = "A nearby island nature reserve reached by boat from the Old Town harbor.",
            Latitude = 42.6268,
            Longitude = 18.1222,
            OpeningHours = "09:00 - 18:00",
            AverageRating = 4.9,
            ReviewCount = 320,
            CreatedAt = now
        };

        var gruzFishingPier = new Place
        {
            Name = "Gruz Fishing Pier",
            Subtitle = "Quiet harbor-side fishing spot",
            Address = "Gruz Harbor, Dubrovnik",
            PlaceTypeId = ptActivity.Id,
            LocationId = gruz.Id,
            Description = "A low-key harbor-side spot for a slow evening walk and fishing atmosphere.",
            Latitude = 42.6588,
            Longitude = 18.0866,
            OpeningHours = "Open 24h",
            AverageRating = 4.2,
            ReviewCount = 34,
            CreatedAt = now
        };

        var lapadBay = new Place
        {
            Name = "Lapad Bay Promenade",
            Subtitle = "Waterfront walk and sunset views",
            Address = "Lapad Bay, Dubrovnik",
            PlaceTypeId = ptNature.Id,
            LocationId = lapad.Id,
            Description = "A relaxed bay-side promenade with beach access, cafes and sunset views.",
            Latitude = 42.6554,
            Longitude = 18.0669,
            OpeningHours = "Open 24h",
            AverageRating = 4.6,
            ReviewCount = 158,
            CreatedAt = now
        };

        var villaAdriatica = new Place
        {
            Name = "Villa Adriatica Luxury Resort",
            Subtitle = "Fictional 5-star luxury resort in Lapad",
            Address = "Lapad, Dubrovnik",
            PlaceTypeId = ptStay.Id,
            LocationId = lapad.Id,
            Description = "Luxury resort in Lapad with spa, pool and sea-view rooms.",
            Latitude = 42.6566,
            Longitude = 18.0716,
            AverageRating = 4.8,
            ReviewCount = 342,
            CreatedAt = now
        };

        var hotelExcelsior = new Place
        {
            Name = "Hotel Excelsior",
            Subtitle = "Historic luxury hotel near Ploce",
            Address = "Frana Supila 12, Dubrovnik",
            PlaceTypeId = ptStay.Id,
            LocationId = ploce.Id,
            Description = "Historic luxury hotel east of the Old Town with sea views and premium amenities.",
            Latitude = 42.64125,
            Longitude = 18.11868,
            AverageRating = 4.7,
            ReviewCount = 287,
            CreatedAt = now
        };

        var oldTownApartment = new Place
        {
            Name = "Old Town Stone Apartment",
            Subtitle = "Private apartment inside the city walls",
            Address = "Old Town, Dubrovnik",
            PlaceTypeId = ptStay.Id,
            LocationId = oldTown.Id,
            Description = "A cozy private apartment in a renovated stone building inside Dubrovnik Old Town.",
            Latitude = 42.6416,
            Longitude = 18.1092,
            AverageRating = 4.6,
            ReviewCount = 74,
            CreatedAt = now
        };

        db.Places.AddRange(
            banjeBeach,
            nautika,
            konobaPupo,
            cogito,
            cityWalls,
            lokrumIsland,
            gruzFishingPier,
            lapadBay,
            villaAdriatica,
            hotelExcelsior,
            oldTownApartment);

        await db.SaveChangesAsync();

        db.Restaurants.AddRange(
            new Restaurant { PlaceId = nautika.Id },
            new Restaurant { PlaceId = konobaPupo.Id });

        var villaStay = new Stay
        {
            PlaceId = villaAdriatica.Id,
            StayTypeId = villaStayType.Id,
            PricePerNight = 189,
            CheckInTime = "14:00",
            CheckOutTime = "11:00",
        };

        var excelsiorStay = new Stay
        {
            PlaceId = hotelExcelsior.Id,
            StayTypeId = hotelStayType.Id,
            PricePerNight = 165,
            CheckInTime = "15:00",
            CheckOutTime = "11:00",
        };

        var apartmentStay = new Stay
        {
            PlaceId = oldTownApartment.Id,
            StayTypeId = apartmentStayType.Id,
            PricePerNight = 95,
            CheckInTime = "14:00",
            CheckOutTime = "10:00",
        };

        db.Stays.AddRange(villaStay, excelsiorStay, apartmentStay);
        await db.SaveChangesAsync();

        AddStayAmenities(db, villaStay, SA("free-wifi"), SA("swimming-pool"), SA("breakfast-included"), SA("parking"), SA("spa"), SA("air-conditioning"));
        AddStayAmenities(db, excelsiorStay, SA("free-wifi"), SA("breakfast-included"), SA("spa"), SA("air-conditioning"));
        AddStayAmenities(db, apartmentStay, SA("free-wifi"), SA("air-conditioning"));

        db.PlaceAmenities.AddRange(
            new PlaceAmenity { PlaceId = nautika.Id, AmenityId = A("sea").Id },
            new PlaceAmenity { PlaceId = konobaPupo.Id, AmenityId = A("outdoor").Id },
            new PlaceAmenity { PlaceId = cogito.Id, AmenityId = A("wifi").Id },
            new PlaceAmenity { PlaceId = lapadBay.Id, AmenityId = A("family").Id });

        db.PlaceBookingPartners.AddRange(
            new PlaceBookingPartner { PlaceId = villaAdriatica.Id, BookingPartnerId = bpBooking.Id, Url = "https://example.com/villa-adriatica-booking" },
            new PlaceBookingPartner { PlaceId = villaAdriatica.Id, BookingPartnerId = bpExpedia.Id, Url = "https://example.com/villa-adriatica-expedia" },
            new PlaceBookingPartner { PlaceId = hotelExcelsior.Id, BookingPartnerId = bpBooking.Id, Url = "https://example.com/hotel-excelsior-booking" },
            new PlaceBookingPartner { PlaceId = oldTownApartment.Id, BookingPartnerId = bpAirbnb.Id, Url = "https://example.com/old-town-apartment-airbnb" });

        db.UserPlaces.AddRange(
            new UserPlace { UserId = marko.Id, PlaceId = villaAdriatica.Id, CreatedAt = now },
            new UserPlace { UserId = marko.Id, PlaceId = oldTownApartment.Id, CreatedAt = now });

        await db.SaveChangesAsync();

        Tag T(string slug) => tagLookup[slug];

        AddDestinationTags(db, dubrovnik,
            T("adriatic-coast"),
            T("heritage-city"),
            T("walkable-city"),
            T("summer-destination"));

        AddLocationTags(db, oldTown, T("old-town-core"), T("walkable-area"));
        AddLocationTags(db, ploce, T("waterfront-area"), T("beach-access"));
        AddLocationTags(db, lapad, T("beach-district"), T("relaxed-area"));
        AddLocationTags(db, gruz, T("harbor-area"), T("local-area"));

        AddPlaceTags(db, banjeBeach, T("pebble-beach"), T("swimming"), T("sea-view"), T("popular-spot"));
        AddPlaceTags(db, nautika, T("seafood"), T("mediterranean"), T("fine-dining"), T("sea-view"), T("premium"));
        AddPlaceTags(db, konobaPupo, T("dalmatian"), T("traditional"), T("casual"), T("local-favorite"));
        AddPlaceTags(db, cogito, T("specialty-coffee"), T("casual"), T("old-town"), T("budget"));
        AddPlaceTags(db, cityWalls, T("medieval-walls"), T("history"), T("culture"), T("panoramic-view"), T("walking-route"));
        AddPlaceTags(db, lokrumIsland, T("island-escape"), T("nature"), T("nature-reserve"), T("walking-route"), T("swimming"));
        AddPlaceTags(db, gruzFishingPier, T("fishing"), T("waterfront"), T("quiet-area"), T("local-favorite"));
        AddPlaceTags(db, lapadBay, T("waterfront"), T("family-friendly"), T("sunset-view"), T("walking-route"));
        AddPlaceTags(db, villaAdriatica, T("luxury-stay"), T("sea-view"), T("wellness"), T("premium"));
        AddPlaceTags(db, hotelExcelsior, T("luxury-stay"), T("sea-view"), T("old-town-nearby"), T("wellness"));
        AddPlaceTags(db, oldTownApartment, T("apartment-stay"), T("old-town"), T("boutique-stay"), T("budget"));

        await db.SaveChangesAsync();

        db.UserTags.AddRange(
            new UserTag { UserId = marko.Id, TagId = T("fishing").Id },
            new UserTag { UserId = marko.Id, TagId = T("seafood").Id },
            new UserTag { UserId = marko.Id, TagId = T("waterfront").Id },
            new UserTag { UserId = elena.Id, TagId = T("history").Id },
            new UserTag { UserId = elena.Id, TagId = T("fine-dining").Id },
            new UserTag { UserId = elena.Id, TagId = T("luxury-stay").Id });

        db.UserInterestSignals.AddRange(
            new UserInterestSignal
            {
                UserId = marko.Id,
                TagId = T("kayaking").Id,
                Score = 18,
                Source = "inferred",
                LastInteractionAt = now.AddDays(-1),
                LastReason = "Opened outdoor and waterfront map markers."
            },
            new UserInterestSignal
            {
                UserId = marko.Id,
                TagId = T("local-favorite").Id,
                Score = 12,
                Source = "inferred",
                LastInteractionAt = now.AddDays(-2),
                LastReason = "Saved local places near Gruz."
            },
            new UserInterestSignal
            {
                UserId = elena.Id,
                TagId = T("sea-view").Id,
                Score = 14,
                Source = "inferred",
                LastInteractionAt = now.AddDays(-1),
                LastReason = "Viewed stays and restaurants with sea views."
            });

        await db.SaveChangesAsync();

        var summerFest = new Event
        {
            Name = "Dubrovnik Summer Festival",
            EventTypeId = etFestival.Id,
            LocationId = oldTown.Id,
            Description = "Cultural summer festival with theatre, music and performances across historic venues.",
            StartDate = now.AddDays(5),
            EndDate = now.AddDays(47),
            TicketPrice = "EUR 25 - EUR 100",
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
            LocationId = oldTown.Id,
            Description = "Open-air food market with local products, olive oils, cheeses and seafood snacks.",
            StartDate = now.AddDays(2),
            EndDate = now.AddDays(2).AddHours(8),
            TicketPrice = "Free entry",
            Duration = "8 hours",
            HostedBy = "Dubrovnik City Market Association",
            CreatedAt = now
        };

        var kayakTour = new Event
        {
            Name = "Sea Kayaking Adventure",
            EventTypeId = etOutdoor.Id,
            LocationId = ploce.Id,
            PlaceId = banjeBeach.Id,
            Description = "Guided sea kayaking tour along the Dubrovnik coastline.",
            StartDate = now.AddDays(4),
            EndDate = now.AddDays(4).AddHours(4),
            TicketPrice = "EUR 35",
            Duration = "4 hours",
            HostedBy = "Adriatic Kayak Tours",
            CreatedAt = now
        };

        var jazzEvening = new Event
        {
            Name = "Lapad Jazz Evening",
            EventTypeId = etMusic.Id,
            LocationId = lapad.Id,
            Description = "Relaxed live jazz evening near the promenade.",
            StartDate = now.AddDays(8),
            EndDate = now.AddDays(8).AddHours(3),
            TicketPrice = "EUR 15",
            Duration = "3 hours",
            HostedBy = "Lapad Cultural Club",
            CreatedAt = now
        };

        db.Events.AddRange(summerFest, foodMarket, kayakTour, jazzEvening);
        await db.SaveChangesAsync();

        AddEventTags(db, summerFest, T("cultural-festival"), T("ticketed-event"), T("summer-event"));
        AddEventTags(db, foodMarket, T("food-market-event"), T("free-entry-event"), T("local-event"));
        AddEventTags(db, kayakTour, T("outdoor-tour-event"), T("guided-event"), T("ticketed-event"));
        AddEventTags(db, jazzEvening, T("live-music-event"), T("ticketed-event"), T("evening-event"));

        await db.SaveChangesAsync();

        await EnsureCoreEntityImagesAsync(db);

        db.PlaceReviews.AddRange(
            new PlaceReview
            {
                PlaceId = banjeBeach.Id,
                UserId = marko.Id,
                Rating = 5,
                Comment = "Clear water and great views of Old Town.",
                CreatedAt = now.AddDays(-10)
            },
            new PlaceReview
            {
                PlaceId = cityWalls.Id,
                UserId = elena.Id,
                Rating = 5,
                Comment = "The walk around the walls was the highlight of the trip.",
                CreatedAt = now.AddDays(-8)
            },
            new PlaceReview
            {
                PlaceId = villaAdriatica.Id,
                UserId = elena.Id,
                Rating = 5,
                Comment = "Excellent stay, sea view and comfortable rooms.",
                CreatedAt = now.AddDays(-6)
            },
            new PlaceReview
            {
                PlaceId = gruzFishingPier.Id,
                UserId = marko.Id,
                Rating = 4,
                Comment = "Quiet, local and useful for a slow evening near the harbor.",
                CreatedAt = now.AddDays(-4)
            });

        await db.SaveChangesAsync();

        var newRestaurantContribution = new PlaceContribution
        {
            Type = PlaceContributionType.CreatePlace,
            Status = PlaceContributionStatus.PendingReview,
            UserId = marko.Id,
            PlaceTypeId = ptRestaurant.Id,
            LocationId = oldTown.Id,
            Name = "Konoba Mare",
            Subtitle = "Traditional seafood restaurant",
            Address = "Old Town, Dubrovnik",
            OpeningHours = "11:00 - 23:00",
            Description = "A proposed seafood-focused restaurant with local fish, olive oil and Dalmatian wines.",
            Latitude = 42.6410,
            Longitude = 18.1102,
            ContactEmail = "info@konobamare.example",
            PhoneNumber = "+385 20 000 000",
            Website = "https://example.com/konoba-mare",
            TagsJson = "[\"seafood\",\"traditional\",\"local-favorite\"]",
            AmenitiesJson = "[\"wifi\",\"outdoor\"]",
            BookingPartnersJson = "[]",
            SubmittedAt = now,
            CreatedAt = now
        };

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
            Latitude = 42.6566,
            Longitude = 18.0716,
            ContactEmail = "reservations@villa-adriatica.example",
            PhoneNumber = "+385 20 111 111",
            Website = "https://example.com/villa-adriatica",
            PricePerNight = 210,
            CheckInTime = "15:00",
            CheckOutTime = "11:00",
            TagsJson = "[\"luxury-stay\",\"sea-view\",\"wellness\",\"premium\"]",
            StayAmenitiesJson = "[\"free-wifi\",\"spa\",\"sea-view\",\"breakfast-included\",\"air-conditioning\"]",
            BookingPartnersJson = "[\"booking\",\"expedia\"]",
            SubmittedAt = now,
            CreatedAt = now
        };

        db.PlaceContributions.AddRange(newRestaurantContribution, updatePlaceContribution);
        await db.SaveChangesAsync();

        await EnsureAdditionalExploreDemoDataAsync(db, tagLookup);
        await EnsureDefaultModerationTermsAsync(db);
    }

    private static async Task EnsureCoreEntityImagesAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        var added = false;

        async Task AddTerritoryImagesIfMissingAsync(string name, params string[] urls)
        {
            var territory = await db.Territories.FirstOrDefaultAsync(x => x.Name == name);
            if (territory is null ||
                await db.EntityImages.AnyAsync(x => x.TargetType == EntityImageTargetType.Territory && x.TerritoryId == territory.Id))
            {
                return;
            }

            AddImages(urls, (url, index) => new EntityImage
            {
                TargetType = EntityImageTargetType.Territory,
                TerritoryId = territory.Id,
                Url = url,
                IsMain = index == 0,
                SortOrder = index,
                CreatedAt = now.AddMilliseconds(index)
            });
        }

        async Task AddRegionImagesIfMissingAsync(string name, params string[] urls)
        {
            var region = await db.Regions.FirstOrDefaultAsync(x => x.Name == name);
            if (region is null ||
                await db.EntityImages.AnyAsync(x => x.TargetType == EntityImageTargetType.Region && x.RegionId == region.Id))
            {
                return;
            }

            AddImages(urls, (url, index) => new EntityImage
            {
                TargetType = EntityImageTargetType.Region,
                RegionId = region.Id,
                Url = url,
                IsMain = index == 0,
                SortOrder = index,
                CreatedAt = now.AddMilliseconds(index)
            });
        }

        async Task AddDestinationImagesIfMissingAsync(string name, params string[] urls)
        {
            var destination = await db.Destinations.FirstOrDefaultAsync(x => x.Name == name);
            if (destination is null ||
                await db.EntityImages.AnyAsync(x => x.TargetType == EntityImageTargetType.Destination && x.DestinationId == destination.Id))
            {
                return;
            }

            AddImages(urls, (url, index) => new EntityImage
            {
                TargetType = EntityImageTargetType.Destination,
                DestinationId = destination.Id,
                Url = url,
                IsMain = index == 0,
                SortOrder = index,
                CreatedAt = now.AddMilliseconds(index)
            });
        }

        async Task AddLocationImagesIfMissingAsync(string name, params string[] urls)
        {
            var location = await db.Locations.FirstOrDefaultAsync(x => x.Name == name);
            if (location is null ||
                await db.EntityImages.AnyAsync(x => x.TargetType == EntityImageTargetType.Location && x.LocationId == location.Id))
            {
                return;
            }

            AddImages(urls, (url, index) => new EntityImage
            {
                TargetType = EntityImageTargetType.Location,
                LocationId = location.Id,
                Url = url,
                IsMain = index == 0,
                SortOrder = index,
                CreatedAt = now.AddMilliseconds(index)
            });
        }

        async Task AddPlaceImagesIfMissingAsync(string name, params string[] urls)
        {
            var place = await db.Places.FirstOrDefaultAsync(x => x.Name == name);
            if (place is null ||
                await db.EntityImages.AnyAsync(x => x.TargetType == EntityImageTargetType.Place && x.PlaceId == place.Id))
            {
                return;
            }

            AddImages(urls, (url, index) => new EntityImage
            {
                TargetType = EntityImageTargetType.Place,
                PlaceId = place.Id,
                Url = url,
                AltText = place.Name,
                IsMain = index == 0,
                SortOrder = index,
                CreatedAt = now.AddMilliseconds(index)
            });
        }

        async Task AddEventImagesIfMissingAsync(string name, params string[] urls)
        {
            var eventEntity = await db.Events.FirstOrDefaultAsync(x => x.Name == name);
            if (eventEntity is null ||
                await db.EntityImages.AnyAsync(x => x.TargetType == EntityImageTargetType.Event && x.EventId == eventEntity.Id))
            {
                return;
            }

            AddImages(urls, (url, index) => new EntityImage
            {
                TargetType = EntityImageTargetType.Event,
                EventId = eventEntity.Id,
                Url = url,
                AltText = eventEntity.Name,
                IsMain = index == 0,
                SortOrder = index,
                CreatedAt = now.AddMilliseconds(index)
            });
        }

        void AddImages(IReadOnlyList<string> urls, Func<string, int, EntityImage> createImage)
        {
            for (var i = 0; i < urls.Count; i++)
            {
                db.EntityImages.Add(createImage(urls[i], i));
                added = true;
            }
        }

        await AddTerritoryImagesIfMissingAsync("Croatia", Unsplash("1500530855697-b586d89ba3ee"));
        await AddRegionImagesIfMissingAsync("Dalmatia", Unsplash("1507525428034-b723cf961d3e"));
        await AddDestinationImagesIfMissingAsync("Dubrovnik", Unsplash("1555990793-77610ee3d7c2"), SeedMedia("dubrovnik-old-town"));

        await AddLocationImagesIfMissingAsync("Old Town", SeedMedia("dubrovnik-old-town"));
        await AddLocationImagesIfMissingAsync("Lapad", Unsplash("1596275281743-e4c30b6e0e27"));
        await AddLocationImagesIfMissingAsync("Gruz", Unsplash("1500530855697-b586d89ba3ee"));

        await AddPlaceImagesIfMissingAsync("Banje Beach", Unsplash("1507525428034-b723cf961d3e"), Unsplash("1519046904884-53103b34b206"));
        await AddPlaceImagesIfMissingAsync("Nautika Restaurant", Unsplash("1414235077428-338989a2e8c0"));
        await AddPlaceImagesIfMissingAsync("Cogito Coffee", Unsplash("1495474472287-4d71bcdd2085"));
        await AddPlaceImagesIfMissingAsync("Dubrovnik City Walls", SeedMedia("dubrovnik-old-town"));
        await AddPlaceImagesIfMissingAsync("Lokrum Island", Unsplash("1441974231531-c6227db76b6e"));
        await AddPlaceImagesIfMissingAsync("Villa Adriatica Luxury Resort", Unsplash("1566073771259-6a6300d8226c"), Unsplash("1564501049412-61c2a3083791"));
        await AddPlaceImagesIfMissingAsync("Hotel Excelsior", SeedMedia("hotel-excelsior"));
        await AddPlaceImagesIfMissingAsync("Old Town Stone Apartment", Unsplash("1520250497591-112f2f40a3f4"));

        await AddEventImagesIfMissingAsync("Dubrovnik Summer Festival", SeedMedia("dubrovnik-summer-festival"));
        await AddEventImagesIfMissingAsync("Old Town Food Market", Unsplash("1555939594-58d7cb561ad1"));
        await AddEventImagesIfMissingAsync("Sea Kayaking Adventure", Unsplash("1544551763-46a013bb70d5"));

        if (added)
        {
            await db.SaveChangesAsync();
        }
    }

    private static async Task<Dictionary<string, Tag>> EnsureSmartTagTaxonomyAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;

        var placeTypesByName = await db.PlaceTypes
            .ToDictionaryAsync(x => NormalizeKey(x.Name), x => x);

        var tagTypesBySlug = await db.TagTypes
            .Include(x => x.PlaceTypes)
            .ToDictionaryAsync(x => x.Slug, StringComparer.OrdinalIgnoreCase);

        foreach (var definition in BuildSeedTagTypes())
        {
            if (!tagTypesBySlug.TryGetValue(definition.Slug, out var tagType))
            {
                tagType = new TagType
                {
                    Name = definition.Name,
                    Slug = definition.Slug,
                    Description = definition.Description,
                    ForDestination = definition.ForDestination,
                    ForLocation = definition.ForLocation,
                    ForPlace = definition.ForPlace,
                    ForEvent = definition.ForEvent,
                    CreatedAt = now
                };

                db.TagTypes.Add(tagType);
                tagTypesBySlug[definition.Slug] = tagType;
            }
            else
            {
                tagType.Name = definition.Name;
                tagType.Description = definition.Description;
                tagType.ForDestination = definition.ForDestination;
                tagType.ForLocation = definition.ForLocation;
                tagType.ForPlace = definition.ForPlace;
                tagType.ForEvent = definition.ForEvent;
                tagType.UpdatedAt = now;
            }
        }

        await db.SaveChangesAsync();

        foreach (var definition in BuildSeedTagTypes().Where(x => x.ForPlace && x.AllowedPlaceTypes is { Length: > 0 }))
        {
            var tagType = tagTypesBySlug[definition.Slug];
            var existingPlaceTypeIds = await db.TagTypePlaceTypes
                .Where(x => x.TagTypeId == tagType.Id)
                .Select(x => x.PlaceTypeId)
                .ToListAsync();

            foreach (var allowedPlaceType in definition.AllowedPlaceTypes!)
            {
                if (!placeTypesByName.TryGetValue(NormalizeKey(allowedPlaceType), out var placeType))
                {
                    continue;
                }

                if (existingPlaceTypeIds.Contains(placeType.Id))
                {
                    continue;
                }

                db.TagTypePlaceTypes.Add(new TagTypePlaceType
                {
                    TagTypeId = tagType.Id,
                    PlaceTypeId = placeType.Id
                });
            }
        }

        await db.SaveChangesAsync();

        var tagsBySlug = await db.Tags
            .Include(x => x.TagType)
            .ToDictionaryAsync(x => x.Slug, StringComparer.OrdinalIgnoreCase);

        foreach (var definition in BuildSeedTags())
        {
            var tagType = tagTypesBySlug[definition.TagTypeSlug];

            if (!tagsBySlug.TryGetValue(definition.Slug, out var tag))
            {
                tag = new Tag
                {
                    Name = definition.Name,
                    Slug = definition.Slug,
                    Description = definition.Description,
                    TagTypeId = tagType.Id,
                    ForDestination = definition.ForDestination,
                    ForLocation = definition.ForLocation,
                    ForPlace = definition.ForPlace,
                    ForEvent = definition.ForEvent,
                    CanGenerateExploreSection = definition.CanGenerateExploreSection,
                    CreatedAt = now
                };

                db.Tags.Add(tag);
                tagsBySlug[definition.Slug] = tag;
            }
            else
            {
                tag.Name = definition.Name;
                tag.Description = definition.Description;
                tag.TagTypeId = tagType.Id;
                tag.ForDestination = definition.ForDestination;
                tag.ForLocation = definition.ForLocation;
                tag.ForPlace = definition.ForPlace;
                tag.ForEvent = definition.ForEvent;
                tag.CanGenerateExploreSection = definition.CanGenerateExploreSection;
                tag.UpdatedAt = now;
            }
        }

        await db.SaveChangesAsync();

        return await db.Tags
            .Include(x => x.TagType)
            .ToDictionaryAsync(x => x.Slug, StringComparer.OrdinalIgnoreCase);
    }

    private static List<SeedTagTypeDefinition> BuildSeedTagTypes() =>
    [
        new("Destination Theme", "destination-theme", ForDestination: true, Description: "High-level destination positioning."),
        new("Location Character", "location-character", ForLocation: true, Description: "Area-level character and context."),

        new("Cuisine", "cuisine", ForPlace: true, Description: "Food cuisine tags for restaurants and cafes.", AllowedPlaceTypes: ["Restaurant", "Cafe", "Bar"]),
        new("Dining Style", "dining-style", ForPlace: true, Description: "Dining style and service level.", AllowedPlaceTypes: ["Restaurant", "Cafe", "Bar"]),
        new("Stay Style", "stay-style", ForPlace: true, Description: "Accommodation style and positioning.", AllowedPlaceTypes: ["Stay"]),

        new("Activity", "activity", ForPlace: true, Description: "Activity tags that can become Explore homepage sections.", AllowedPlaceTypes: ["Beach", "Nature", "Landmark", "Activity", "Museum", "Gallery"]),
        new("Explore Topic", "explore-topic", ForPlace: true, Description: "Broad place topics that can become Explore homepage sections.", AllowedPlaceTypes: ["Beach", "Nature", "Landmark", "Activity", "Museum", "Gallery", "Attraction"]),
        new("Setting", "setting", ForPlace: true, Description: "Physical setting or atmosphere.", AllowedPlaceTypes: ["Beach", "Restaurant", "Cafe", "Bar", "Landmark", "Nature", "Stay", "Activity", "Museum", "Gallery"]),
        new("Price Level", "price-level", ForPlace: true, Description: "Simple price positioning.", AllowedPlaceTypes: ["Restaurant", "Cafe", "Bar", "Stay"]),

        new("Event Category", "event-category", ForEvent: true, Description: "Event category and format."),
        new("Event Access", "event-access", ForEvent: true, Description: "Ticketing and access model.")
    ];

    private static List<SeedTagDefinition> BuildSeedTags() =>
    [
        new("Adriatic Coast", "adriatic-coast", "destination-theme", ForDestination: true),
        new("Heritage City", "heritage-city", "destination-theme", ForDestination: true),
        new("Walkable City", "walkable-city", "destination-theme", ForDestination: true),
        new("Summer Destination", "summer-destination", "destination-theme", ForDestination: true),

        new("Old Town Core", "old-town-core", "location-character", ForLocation: true),
        new("Beach District", "beach-district", "location-character", ForLocation: true),
        new("Harbor Area", "harbor-area", "location-character", ForLocation: true),
        new("Waterfront Area", "waterfront-area", "location-character", ForLocation: true),
        new("Walkable Area", "walkable-area", "location-character", ForLocation: true),
        new("Beach Access", "beach-access", "location-character", ForLocation: true),
        new("Relaxed Area", "relaxed-area", "location-character", ForLocation: true),
        new("Local Area", "local-area", "location-character", ForLocation: true),

        new("Seafood", "seafood", "cuisine", ForPlace: true),
        new("Mediterranean", "mediterranean", "cuisine", ForPlace: true),
        new("Dalmatian", "dalmatian", "cuisine", ForPlace: true),
        new("Italian", "italian", "cuisine", ForPlace: true),
        new("Vegetarian Friendly", "vegetarian-friendly", "cuisine", ForPlace: true),

        new("Fine Dining", "fine-dining", "dining-style", ForPlace: true),
        new("Traditional", "traditional", "dining-style", ForPlace: true),
        new("Casual", "casual", "dining-style", ForPlace: true),
        new("Specialty Coffee", "specialty-coffee", "dining-style", ForPlace: true),

        new("Luxury Stay", "luxury-stay", "stay-style", ForPlace: true),
        new("Boutique Stay", "boutique-stay", "stay-style", ForPlace: true),
        new("Apartment Stay", "apartment-stay", "stay-style", ForPlace: true),

        new("Swimming", "swimming", "activity", ForPlace: true, CanGenerateExploreSection: true),
        new("Kayaking", "kayaking", "activity", ForPlace: true, CanGenerateExploreSection: true),
        new("Fishing", "fishing", "activity", ForPlace: true, CanGenerateExploreSection: true),
        new("Sight-seeing", "walking-route", "activity", ForPlace: true, CanGenerateExploreSection: true),

        new("Culture", "culture", "explore-topic", ForPlace: true, CanGenerateExploreSection: true),
        new("History", "history", "explore-topic", ForPlace: true, CanGenerateExploreSection: true),
        new("Art", "art", "explore-topic", ForPlace: true, CanGenerateExploreSection: true),
        new("Nature", "nature", "explore-topic", ForPlace: true, CanGenerateExploreSection: true),

        new("Pebble Beach", "pebble-beach", "setting", ForPlace: true),
        new("Sea View", "sea-view", "setting", ForPlace: true),
        new("Old Town", "old-town", "setting", ForPlace: true),
        new("Old Town Nearby", "old-town-nearby", "setting", ForPlace: true),
        new("Waterfront", "waterfront", "setting", ForPlace: true),
        new("Quiet Area", "quiet-area", "setting", ForPlace: true),
        new("Island Escape", "island-escape", "setting", ForPlace: true),
        new("Nature Reserve", "nature-reserve", "setting", ForPlace: true),
        new("Panoramic View", "panoramic-view", "setting", ForPlace: true),
        new("Medieval Walls", "medieval-walls", "setting", ForPlace: true),
        new("Popular Spot", "popular-spot", "setting", ForPlace: true),
        new("Local Favorite", "local-favorite", "setting", ForPlace: true),
        new("Family Friendly", "family-friendly", "setting", ForPlace: true),
        new("Sunset View", "sunset-view", "setting", ForPlace: true),
        new("Wellness", "wellness", "setting", ForPlace: true),
        new("Rooftop", "rooftop", "setting", ForPlace: true),
        new("Hidden Gem", "hidden-gem", "setting", ForPlace: true),
        new("Harbor View", "harbor-view", "setting", ForPlace: true),
        new("Cave Beach", "cave-beach", "setting", ForPlace: true),
        new("Botanical Garden", "botanical-garden", "setting", ForPlace: true),
        new("Museum", "museum", "setting", ForPlace: true),
        new("Fortress", "fortress", "setting", ForPlace: true),

        new("Croatian", "croatian", "cuisine", ForPlace: true),
        new("Grill", "grill", "cuisine", ForPlace: true),
        new("Wine", "wine", "cuisine", ForPlace: true),
        new("Dessert", "dessert", "cuisine", ForPlace: true),
        new("Ice Cream", "ice-cream", "cuisine", ForPlace: true),
        new("Breakfast", "breakfast", "dining-style", ForPlace: true),
        new("Wine Bar", "wine-bar", "dining-style", ForPlace: true),
        new("Waterfront Dining", "waterfront-dining", "dining-style", ForPlace: true),

        new("Guesthouse", "guesthouse-stay", "stay-style", ForPlace: true),
        new("Family Stay", "family-stay", "stay-style", ForPlace: true),
        new("Harbor Stay", "harbor-stay", "stay-style", ForPlace: true),

        new("Hiking", "hiking", "activity", ForPlace: true, CanGenerateExploreSection: true),
        new("Boat Tour", "boat-tour", "activity", ForPlace: true, CanGenerateExploreSection: true),
        new("Island Hopping", "island-hopping", "activity", ForPlace: true, CanGenerateExploreSection: true),
        new("Photo Walk", "photo-walk", "activity", ForPlace: true, CanGenerateExploreSection: true),
        new("Diving & Snorkeling", "diving-snorkeling", "activity", ForPlace: true, CanGenerateExploreSection: true),
        new("Cable Car", "cable-car", "activity", ForPlace: true, CanGenerateExploreSection: true),
        new("Guided Tour", "guided-tour", "activity", ForPlace: true, CanGenerateExploreSection: true),

        new("Architecture", "architecture", "explore-topic", ForPlace: true, CanGenerateExploreSection: true),

        new("Budget", "budget", "price-level", ForPlace: true),
        new("Premium", "premium", "price-level", ForPlace: true),

        new("Cultural Festival", "cultural-festival", "event-category", ForEvent: true),
        new("Food Market Event", "food-market-event", "event-category", ForEvent: true),
        new("Outdoor Tour Event", "outdoor-tour-event", "event-category", ForEvent: true),
        new("Live Music Event", "live-music-event", "event-category", ForEvent: true),
        new("Guided Event", "guided-event", "event-category", ForEvent: true),
        new("Local Event", "local-event", "event-category", ForEvent: true),
        new("Summer Event", "summer-event", "event-category", ForEvent: true),
        new("Evening Event", "evening-event", "event-category", ForEvent: true),
        new("Walking Tour Event", "walking-tour-event", "event-category", ForEvent: true),
        new("Nature Walk Event", "nature-walk-event", "event-category", ForEvent: true),
        new("Boat Trip Event", "boat-trip-event", "event-category", ForEvent: true),
        new("Classical Music Event", "classical-music-event", "event-category", ForEvent: true),
        new("Photography Event", "photography-event", "event-category", ForEvent: true),

        new("Free Entry Event", "free-entry-event", "event-access", ForEvent: true),
        new("Ticketed Event", "ticketed-event", "event-access", ForEvent: true)
    ];


    private static async Task EnsureAdditionalExploreDemoDataAsync(AppDbContext db, IReadOnlyDictionary<string, Tag> tagLookup)
    {
        var now = DateTime.UtcNow;

        var dubrovnik = await db.Destinations
            .FirstOrDefaultAsync(destination => destination.Name == "Dubrovnik");

        if (dubrovnik is null)
        {
            return;
        }

        async Task<LocationType> EnsureLocationTypeAsync(string name, string description)
        {
            var locationType = await db.LocationTypes.FirstOrDefaultAsync(x => x.Name == name);
            if (locationType is not null)
            {
                return locationType;
            }

            locationType = new LocationType
            {
                Name = name,
                Description = description,
                CreatedAt = now
            };

            db.LocationTypes.Add(locationType);
            await db.SaveChangesAsync();
            return locationType;
        }

        async Task<Location> EnsureLocationAsync(string name, LocationType locationType, double latitude, double longitude)
        {
            var location = await db.Locations.FirstOrDefaultAsync(x =>
                x.DestinationId == dubrovnik.Id &&
                x.Name == name);

            if (location is not null)
            {
                return location;
            }

            location = new Location
            {
                Name = name,
                DestinationId = dubrovnik.Id,
                LocationTypeId = locationType.Id,
                Latitude = latitude,
                Longitude = longitude,
                CreatedAt = now
            };

            db.Locations.Add(location);
            await db.SaveChangesAsync();
            return location;
        }

        async Task<PlaceType> EnsurePlaceTypeAsync(string name, string description)
        {
            var placeType = await db.PlaceTypes.FirstOrDefaultAsync(x => x.Name == name);
            if (placeType is not null)
            {
                return placeType;
            }

            placeType = new PlaceType
            {
                Name = name,
                Description = description,
                CreatedAt = now
            };

            db.PlaceTypes.Add(placeType);
            await db.SaveChangesAsync();
            return placeType;
        }

        async Task<StayType> EnsureStayTypeAsync(string name, string slug, string description)
        {
            var stayType = await db.StayTypes.FirstOrDefaultAsync(x => x.Slug == slug);
            if (stayType is not null)
            {
                return stayType;
            }

            stayType = new StayType
            {
                Name = name,
                Slug = slug,
                Description = description,
                CreatedAt = now
            };

            db.StayTypes.Add(stayType);
            await db.SaveChangesAsync();
            return stayType;
        }

        async Task<EventType> EnsureEventTypeAsync(string name, string description)
        {
            var eventType = await db.EventTypes.FirstOrDefaultAsync(x => x.Name == name);
            if (eventType is not null)
            {
                return eventType;
            }

            eventType = new EventType
            {
                Name = name,
                Description = description,
                CreatedAt = now
            };

            db.EventTypes.Add(eventType);
            await db.SaveChangesAsync();
            return eventType;
        }

        async Task<StayAmenity> EnsureStayAmenityAsync(string name, string slug, string iconKey, string description)
        {
            var amenity = await db.StayAmenities.FirstOrDefaultAsync(x => x.Slug == slug);
            if (amenity is not null)
            {
                return amenity;
            }

            amenity = new StayAmenity
            {
                Name = name,
                Slug = slug,
                IconKey = iconKey,
                Description = description,
                CreatedAt = now
            };

            db.StayAmenities.Add(amenity);
            await db.SaveChangesAsync();
            return amenity;
        }

        async Task<Amenity> EnsureAmenityAsync(string name, string icon)
        {
            var amenity = await db.Amenities.FirstOrDefaultAsync(x => x.Name == name);
            if (amenity is not null)
            {
                return amenity;
            }

            amenity = new Amenity { Name = name, Icon = icon };
            db.Amenities.Add(amenity);
            await db.SaveChangesAsync();
            return amenity;
        }

        async Task<BookingPartner> EnsureBookingPartnerAsync(string name, string variant)
        {
            var partner = await db.BookingPartners.FirstOrDefaultAsync(x => x.Name == name);
            if (partner is not null)
            {
                return partner;
            }

            partner = new BookingPartner { Name = name, Variant = variant };
            db.BookingPartners.Add(partner);
            await db.SaveChangesAsync();
            return partner;
        }

        async Task<Place> EnsurePlaceAsync(
            string name,
            string subtitle,
            string address,
            PlaceType placeType,
            Location location,
            string description,
            double latitude,
            double longitude,
            string openingHours,
            double rating,
            int reviewCount,
            string? contactEmail = null,
            string? number = null)
        {
            var place = await db.Places.FirstOrDefaultAsync(x => x.Name == name);
            if (place is not null)
            {
                return place;
            }

            place = new Place
            {
                Name = name,
                Subtitle = subtitle,
                Address = address,
                PlaceTypeId = placeType.Id,
                LocationId = location.Id,
                Description = description,
                Latitude = latitude,
                Longitude = longitude,
                OpeningHours = openingHours,
                AverageRating = rating,
                ReviewCount = reviewCount,
                ContactEmail = contactEmail,
                Number = number,
                CreatedAt = now
            };

            db.Places.Add(place);
            await db.SaveChangesAsync();
            return place;
        }

        async Task EnsureRestaurantAsync(Place place)
        {
            if (await db.Restaurants.AnyAsync(x => x.PlaceId == place.Id))
            {
                return;
            }

            db.Restaurants.Add(new Restaurant { PlaceId = place.Id });
            await db.SaveChangesAsync();
        }

        async Task<Stay> EnsureStayAsync(Place place, StayType stayType, decimal pricePerNight, string checkIn, string checkOut)
        {
            var stay = await db.Stays.FirstOrDefaultAsync(x => x.PlaceId == place.Id);
            if (stay is not null)
            {
                return stay;
            }

            stay = new Stay
            {
                PlaceId = place.Id,
                StayTypeId = stayType.Id,
                PricePerNight = pricePerNight,
                CheckInTime = checkIn,
                CheckOutTime = checkOut
            };

            db.Stays.Add(stay);
            await db.SaveChangesAsync();
            return stay;
        }

        async Task<Event> EnsureEventAsync(
            string name,
            EventType eventType,
            Location location,
            string description,
            DateTime startDate,
            DateTime endDate,
            string ticketPrice,
            string duration,
            string hostedBy,
            Place? place = null)
        {
            var eventEntity = await db.Events.FirstOrDefaultAsync(x => x.Name == name);
            if (eventEntity is not null)
            {
                return eventEntity;
            }

            eventEntity = new Event
            {
                Name = name,
                EventTypeId = eventType.Id,
                LocationId = location.Id,
                PlaceId = place?.Id,
                Description = description,
                StartDate = startDate,
                EndDate = endDate,
                TicketPrice = ticketPrice,
                Duration = duration,
                HostedBy = hostedBy,
                VenueDescription = place is null ? location.Name : place.Name,
                ArrivalTips = "Use public transport or walk where possible; parking near Old Town is limited.",
                Accessibility = "Check the organizer details before attending.",
                CreatedAt = now
            };

            db.Events.Add(eventEntity);
            await db.SaveChangesAsync();
            return eventEntity;
        }

        async Task AddPlaceTagsIfMissingAsync(Place place, params string[] tagSlugs)
        {
            foreach (var tagSlug in tagSlugs)
            {
                if (!tagLookup.TryGetValue(tagSlug, out var tag))
                {
                    continue;
                }

                var exists = await db.PlaceTags.AnyAsync(x => x.PlaceId == place.Id && x.TagId == tag.Id);
                if (exists)
                {
                    continue;
                }

                db.PlaceTags.Add(new PlaceTag { PlaceId = place.Id, TagId = tag.Id });
            }

            await db.SaveChangesAsync();
        }

        async Task AddLocationTagsIfMissingAsync(Location location, params string[] tagSlugs)
        {
            foreach (var tagSlug in tagSlugs)
            {
                if (!tagLookup.TryGetValue(tagSlug, out var tag))
                {
                    continue;
                }

                var exists = await db.LocationTags.AnyAsync(x => x.LocationId == location.Id && x.TagId == tag.Id);
                if (exists)
                {
                    continue;
                }

                db.LocationTags.Add(new LocationTag { LocationId = location.Id, TagId = tag.Id });
            }

            await db.SaveChangesAsync();
        }

        async Task AddEventTagsIfMissingAsync(Event eventEntity, params string[] tagSlugs)
        {
            foreach (var tagSlug in tagSlugs)
            {
                if (!tagLookup.TryGetValue(tagSlug, out var tag))
                {
                    continue;
                }

                var exists = await db.EventTags.AnyAsync(x => x.EventId == eventEntity.Id && x.TagId == tag.Id);
                if (exists)
                {
                    continue;
                }

                db.EventTags.Add(new EventTag { EventId = eventEntity.Id, TagId = tag.Id });
            }

            await db.SaveChangesAsync();
        }

        async Task AddStayAmenitiesIfMissingAsync(Stay stay, params StayAmenity[] amenities)
        {
            foreach (var amenity in amenities)
            {
                var exists = await db.StayStayAmenities.AnyAsync(x =>
                    x.StayPlaceId == stay.PlaceId &&
                    x.StayAmenityId == amenity.Id);

                if (exists)
                {
                    continue;
                }

                db.StayStayAmenities.Add(new StayStayAmenity
                {
                    StayPlaceId = stay.PlaceId,
                    StayAmenityId = amenity.Id
                });
            }

            await db.SaveChangesAsync();
        }

        async Task AddPlaceAmenityIfMissingAsync(Place place, Amenity amenity)
        {
            var exists = await db.PlaceAmenities.AnyAsync(x =>
                x.PlaceId == place.Id &&
                x.AmenityId == amenity.Id);

            if (exists)
            {
                return;
            }

            db.PlaceAmenities.Add(new PlaceAmenity { PlaceId = place.Id, AmenityId = amenity.Id });
            await db.SaveChangesAsync();
        }

        async Task AddBookingPartnerIfMissingAsync(Place place, BookingPartner partner, string url)
        {
            var exists = await db.PlaceBookingPartners.AnyAsync(x =>
                x.PlaceId == place.Id &&
                x.BookingPartnerId == partner.Id);

            if (exists)
            {
                return;
            }

            db.PlaceBookingPartners.Add(new PlaceBookingPartner
            {
                PlaceId = place.Id,
                BookingPartnerId = partner.Id,
                Url = url
            });

            await db.SaveChangesAsync();
        }

        async Task AddPlaceImagesIfMissingAsync(Place place, params (string Url, string Caption)[] images)
        {
            if (await db.EntityImages.AnyAsync(x => x.TargetType == EntityImageTargetType.Place && x.PlaceId == place.Id))
            {
                return;
            }

            for (var i = 0; i < images.Length; i++)
            {
                db.EntityImages.Add(new EntityImage
                {
                    TargetType = EntityImageTargetType.Place,
                    PlaceId = place.Id,
                    Url = images[i].Url,
                    Caption = images[i].Caption,
                    AltText = place.Name,
                    IsMain = i == 0,
                    SortOrder = i,
                    CreatedAt = now
                });
            }

            await db.SaveChangesAsync();
        }

        async Task AddEventImagesIfMissingAsync(Event eventEntity, params (string Url, string Caption)[] images)
        {
            if (await db.EntityImages.AnyAsync(x => x.TargetType == EntityImageTargetType.Event && x.EventId == eventEntity.Id))
            {
                return;
            }

            for (var i = 0; i < images.Length; i++)
            {
                db.EntityImages.Add(new EntityImage
                {
                    TargetType = EntityImageTargetType.Event,
                    EventId = eventEntity.Id,
                    Url = images[i].Url,
                    Caption = images[i].Caption,
                    AltText = eventEntity.Name,
                    IsMain = i == 0,
                    SortOrder = i,
                    CreatedAt = now
                });
            }

            await db.SaveChangesAsync();
        }

        async Task AddReviewIfMissingAsync(Place place, User user, int rating, string comment, int daysAgo)
        {
            var exists = await db.PlaceReviews.AnyAsync(x =>
                x.PlaceId == place.Id &&
                x.UserId == user.Id &&
                x.Comment == comment);

            if (exists)
            {
                return;
            }

            db.PlaceReviews.Add(new PlaceReview
            {
                PlaceId = place.Id,
                UserId = user.Id,
                Rating = rating,
                Comment = comment,
                CreatedAt = now.AddDays(-daysAgo)
            });

            await db.SaveChangesAsync();
        }

        var neighborhoodType = await EnsureLocationTypeAsync("Neighborhood", "City neighborhood");
        var districtType = await EnsureLocationTypeAsync("District", "City district");
        var viewpointType = await EnsureLocationTypeAsync("Viewpoint Area", "Hill, viewpoint or scenic surrounding area");
        var islandType = await EnsureLocationTypeAsync("Island Area", "Island or island-connected area");

        var oldTown = await EnsureLocationAsync("Old Town", neighborhoodType, 42.6413, 18.1090);
        var ploce = await EnsureLocationAsync("Ploce", districtType, 42.6422, 18.1180);
        var lapad = await EnsureLocationAsync("Lapad", districtType, 42.6559, 18.0712);
        var gruz = await EnsureLocationAsync("Gruz", districtType, 42.6592, 18.0863);
        var pile = await EnsureLocationAsync("Pile", neighborhoodType, 42.6423, 18.1051);
        var srd = await EnsureLocationAsync("Mount Srd", viewpointType, 42.6492, 18.1112);
        var lokrum = await EnsureLocationAsync("Lokrum", islandType, 42.6268, 18.1222);
        var trsteno = await EnsureLocationAsync("Trsteno", districtType, 42.7158, 17.9784);

        await AddLocationTagsIfMissingAsync(pile, "old-town-nearby", "walkable-area");
        await AddLocationTagsIfMissingAsync(srd, "panoramic-view", "nature");
        await AddLocationTagsIfMissingAsync(lokrum, "island-escape", "nature");
        await AddLocationTagsIfMissingAsync(trsteno, "relaxed-area", "nature");

        var ptBeach = await EnsurePlaceTypeAsync("Beach", "Beaches and coastal swimming areas");
        var ptRestaurant = await EnsurePlaceTypeAsync("Restaurant", "Dining establishments");
        var ptCafe = await EnsurePlaceTypeAsync("Cafe", "Cafes and coffee shops");
        var ptBar = await EnsurePlaceTypeAsync("Bar", "Bars, wine bars and nightlife venues");
        var ptLandmark = await EnsurePlaceTypeAsync("Landmark", "Landmarks, monuments and historical places");
        var ptNature = await EnsurePlaceTypeAsync("Nature", "Nature spots and island escapes");
        var ptStay = await EnsurePlaceTypeAsync("Stay", "Hotels, apartments, villas and other accommodation");
        var ptActivity = await EnsurePlaceTypeAsync("Activity", "Activity providers, tours and outdoor experiences");
        var ptMuseum = await EnsurePlaceTypeAsync("Museum", "Museums and cultural institutions");
        var ptGallery = await EnsurePlaceTypeAsync("Gallery", "Art galleries and exhibition spaces");

        // New place types may not have existed when the taxonomy was first ensured,
        // so run the taxonomy sync once more to attach allowed TagType <-> PlaceType links.
        await EnsureSmartTagTaxonomyAsync(db);

        var hotelStayType = await EnsureStayTypeAsync("Hotel", "hotel", "Hotel accommodation");
        var apartmentStayType = await EnsureStayTypeAsync("Apartment", "apartment", "Private apartment accommodation");
        var guesthouseStayType = await EnsureStayTypeAsync("Guesthouse", "guesthouse", "Guesthouse accommodation");
        var villaStayType = await EnsureStayTypeAsync("Villa", "villa", "Private villa accommodation");

        var wifi = await EnsureStayAmenityAsync("Free WiFi", "free-wifi", "wifi", "Wireless internet access");
        var breakfast = await EnsureStayAmenityAsync("Breakfast Included", "breakfast-included", "breakfast", "Breakfast included in the stay");
        var parking = await EnsureStayAmenityAsync("Parking", "parking", "parking", "Parking available");
        var pool = await EnsureStayAmenityAsync("Swimming Pool", "swimming-pool", "pool", "Pool available for guests");
        var spa = await EnsureStayAmenityAsync("Spa", "spa", "spa", "Spa and wellness facilities");
        var airConditioning = await EnsureStayAmenityAsync("Air Conditioning", "air-conditioning", "ac", "Air-conditioned rooms");

        var outdoor = await EnsureAmenityAsync("Outdoor Seating", "outdoor");
        var seaView = await EnsureAmenityAsync("Sea View", "sea");
        var family = await EnsureAmenityAsync("Family Friendly", "family");
        var wifiAmenity = await EnsureAmenityAsync("Free WiFi", "wifi");

        var booking = await EnsureBookingPartnerAsync("Booking.com", "dark");
        var airbnb = await EnsureBookingPartnerAsync("Airbnb", "brand");
        var direct = await EnsureBookingPartnerAsync("Direct booking", "muted");

        var festivalType = await EnsureEventTypeAsync("Festival", "Music and cultural festivals");
        var marketType = await EnsureEventTypeAsync("Market", "Food and craft markets");
        var outdoorType = await EnsureEventTypeAsync("Outdoor", "Outdoor activities");
        var musicType = await EnsureEventTypeAsync("Music", "Music and live performance events");
        var tourType = await EnsureEventTypeAsync("Tour", "Guided tours and local experiences");

        var proto = await EnsurePlaceAsync(
            "Proto Fish Restaurant",
            "Classic seafood restaurant in Old Town",
            "Siroka ul. 1, Dubrovnik",
            ptRestaurant,
            oldTown,
            "A long-running seafood restaurant in the Old Town, useful for visitors who want a polished Dalmatian dinner.",
            42.6416,
            18.1084,
            "12:00 - 23:00",
            4.7,
            184,
            "reservations@proto.example");

        var tajMahal = await EnsurePlaceAsync(
            "Taj Mahal Old Town",
            "Traditional comfort food in the city walls",
            "Nikole Gucetica 2, Dubrovnik",
            ptRestaurant,
            oldTown,
            "A cozy Old Town restaurant with hearty regional dishes, warm interiors and a casual evening atmosphere.",
            42.6408,
            18.1094,
            "11:00 - 23:30",
            4.6,
            231);

        var arsenal = await EnsurePlaceAsync(
            "Gradska Kavana Arsenal",
            "Waterfront cafe and restaurant by the old port",
            "Pred Dvorom 1, Dubrovnik",
            ptCafe,
            oldTown,
            "A central cafe and restaurant beside the old port, good for breakfast, coffee and people-watching.",
            42.6404,
            18.1114,
            "07:00 - 00:00",
            4.5,
            310);

        var dvino = await EnsurePlaceAsync(
            "D'vino Wine Bar",
            "Small wine bar for Croatian labels",
            "Palmoticeva ul. 4a, Dubrovnik",
            ptBar,
            oldTown,
            "A compact wine bar focused on Croatian wines, tasting boards and relaxed late evenings.",
            42.6419,
            18.1081,
            "16:00 - 00:00",
            4.8,
            428);

        var peppino = await EnsurePlaceAsync(
            "Peppino's Gelato",
            "Gelato stop near the Stradun",
            "Old Town, Dubrovnik",
            ptCafe,
            oldTown,
            "A quick dessert stop for gelato while walking through the Old Town core.",
            42.6414,
            18.1086,
            "10:00 - 23:00",
            4.7,
            156);

        var orsan = await EnsurePlaceAsync(
            "Orsan Yacht Club Restaurant",
            "Casual seafood by the marina",
            "Ivana pl. Zajca 4, Dubrovnik",
            ptRestaurant,
            gruz,
            "A relaxed marina-side restaurant with seafood, grilled dishes and harbor views away from the Old Town crowds.",
            42.6633,
            18.0756,
            "12:00 - 23:00",
            4.5,
            147);

        var pantarul = await EnsurePlaceAsync(
            "Pantarul",
            "Modern local restaurant in Lapad",
            "Kralja Tomislava 1, Dubrovnik",
            ptRestaurant,
            lapad,
            "A modern local restaurant in Lapad with seasonal plates, casual service and a neighborhood feel.",
            42.6560,
            18.0748,
            "12:00 - 23:00",
            4.7,
            262);

        var rectorsPalace = await EnsurePlaceAsync(
            "Rector's Palace",
            "Gothic-Renaissance palace museum",
            "Pred Dvorom 3, Dubrovnik",
            ptMuseum,
            oldTown,
            "A historic palace museum that connects Dubrovnik's civic history, architecture and cultural collections.",
            42.6403,
            18.1105,
            "09:00 - 18:00",
            4.7,
            294);

        var sponzaPalace = await EnsurePlaceAsync(
            "Sponza Palace",
            "Historic palace and archive building",
            "Stradun, Dubrovnik",
            ptLandmark,
            oldTown,
            "A landmark palace near the old port, known for its arcades, history and central location.",
            42.6411,
            18.1107,
            "09:00 - 18:00",
            4.6,
            215);

        var franciscan = await EnsurePlaceAsync(
            "Franciscan Monastery Museum",
            "Monastery museum beside Pile Gate",
            "Placa 2, Dubrovnik",
            ptMuseum,
            oldTown,
            "A monastery museum with cloisters, historic pharmacy exhibits and quiet corners close to the main street.",
            42.6419,
            18.1075,
            "09:00 - 18:00",
            4.5,
            190);

        var warPhoto = await EnsurePlaceAsync(
            "War Photo Limited",
            "Documentary photography gallery",
            "Antuninska ul. 6, Dubrovnik",
            ptGallery,
            oldTown,
            "A focused gallery for documentary photography and powerful visual exhibitions.",
            42.6417,
            18.1083,
            "10:00 - 22:00",
            4.8,
            168);

        var lovrijenac = await EnsurePlaceAsync(
            "Lovrijenac Fortress",
            "Fortress above the western cove",
            "Ul. od Tabakarije 29, Dubrovnik",
            ptLandmark,
            pile,
            "A dramatic stone fortress outside the western gate with views over the walls, sea and small harbor below.",
            42.6406,
            18.1033,
            "08:00 - 19:00",
            4.8,
            350);

        var mountSrd = await EnsurePlaceAsync(
            "Mount Srd Viewpoint",
            "Panoramic hill above Dubrovnik",
            "Mount Srd, Dubrovnik",
            ptNature,
            srd,
            "A high viewpoint above Dubrovnik with broad views of the Old Town, Lokrum and the Adriatic coastline.",
            42.6492,
            18.1112,
            "Open 24h",
            4.9,
            501);

        var trstenoArboretum = await EnsurePlaceAsync(
            "Trsteno Arboretum",
            "Historic botanical garden near Dubrovnik",
            "Trsteno, Dubrovnik-Neretva County",
            ptNature,
            trsteno,
            "A historic coastal arboretum with old trees, stone paths, fountains and sea-facing terraces.",
            42.7158,
            17.9784,
            "08:00 - 19:00",
            4.6,
            143);

        var betina = await EnsurePlaceAsync(
            "Betina Cave Beach",
            "Small cave beach below cliffs",
            "Near Ploce, Dubrovnik",
            ptBeach,
            ploce,
            "A smaller beach reached from the sea, often visited by kayak and boat tours.",
            42.6370,
            18.1215,
            "Open 24h",
            4.7,
            88);

        var copacabana = await EnsurePlaceAsync(
            "Copacabana Beach Dubrovnik",
            "Large beach on Babin Kuk",
            "Babin Kuk, Dubrovnik",
            ptBeach,
            lapad,
            "A larger family-friendly beach with shallow water, services and views toward the bridge.",
            42.6614,
            18.0571,
            "08:00 - 22:00",
            4.4,
            206);

        var bellevueBeach = await EnsurePlaceAsync(
            "Bellevue Beach",
            "Cliff-backed beach below hotels",
            "Montovjerna, Dubrovnik",
            ptBeach,
            pile,
            "A small cliff-backed beach west of the Old Town, good for a shorter swimming break.",
            42.6468,
            18.0919,
            "Open 24h",
            4.5,
            129);

        var cableCar = await EnsurePlaceAsync(
            "Dubrovnik Cable Car",
            "Quick ride to Mount Srd views",
            "Petra Kresimira IV, Dubrovnik",
            ptActivity,
            ploce,
            "A short scenic ride up to Mount Srd for views over the city, islands and sunset.",
            42.6427,
            18.1118,
            "09:00 - 21:00",
            4.6,
            412);

        var blueCaveTour = await EnsurePlaceAsync(
            "Blue Cave Boat Tour",
            "Boat trip to caves and swimming spots",
            "Gruz Harbor, Dubrovnik",
            ptActivity,
            gruz,
            "A small-group boat tour for island coves, cave swimming and relaxed Adriatic views.",
            42.6590,
            18.0863,
            "09:00 - 18:00",
            4.8,
            174);

        var elaphitiTrip = await EnsurePlaceAsync(
            "Elaphiti Islands Day Trip",
            "Island-hopping boat day from Gruz",
            "Gruz Harbor, Dubrovnik",
            ptActivity,
            gruz,
            "A day trip connecting Kolocep, Lopud and Sipan with beaches, village walks and boat time.",
            42.6594,
            18.0865,
            "08:30 - 18:00",
            4.7,
            218);

        var lokrumKayak = await EnsurePlaceAsync(
            "Lokrum Kayak Route",
            "Sea kayaking route toward Lokrum",
            "Banje launch point, Dubrovnik",
            ptActivity,
            ploce,
            "A sea-kayaking route from the city side toward Lokrum with cliffs, caves and swimming stops.",
            42.6410,
            18.1168,
            "09:00 - 17:00",
            4.7,
            132);

        var photoWalk = await EnsurePlaceAsync(
            "Old Town Photo Walk",
            "Guided photography walk through stone streets",
            "Pile Gate, Dubrovnik",
            ptActivity,
            oldTown,
            "A guided walking route for viewpoints, alleys, architecture and early evening light around Old Town.",
            42.6418,
            18.1078,
            "08:00 - 20:00",
            4.9,
            71);

        var fishingCharter = await EnsurePlaceAsync(
            "Dubrovnik Fishing Charter",
            "Morning fishing trips from Gruz",
            "Gruz Harbor, Dubrovnik",
            ptActivity,
            gruz,
            "A private or small-group fishing charter from the harbor, focused on relaxed morning trips.",
            42.6590,
            18.0869,
            "06:00 - 13:00",
            4.6,
            44);

        var stariGrad = await EnsurePlaceAsync(
            "Boutique Hotel Stari Grad",
            "Small boutique stay inside Old Town",
            "Od Sigurate 4, Dubrovnik",
            ptStay,
            oldTown,
            "A small boutique hotel in the stone lanes of Old Town, close to restaurants, museums and city gates.",
            42.6418,
            18.1082,
            "Open 24h",
            4.7,
            121);

        var lapadSunset = await EnsurePlaceAsync(
            "Lapad Sunset Apartment",
            "Sea-view apartment near the promenade",
            "Lapad, Dubrovnik",
            ptStay,
            lapad,
            "A private apartment close to beaches and sunset walks in Lapad Bay.",
            42.6555,
            18.0674,
            "Open 24h",
            4.6,
            63);

        var harborGuesthouse = await EnsurePlaceAsync(
            "Gruz Harbor Guesthouse",
            "Budget-friendly stay near ferries",
            "Gruz, Dubrovnik",
            ptStay,
            gruz,
            "A practical guesthouse near the ferry terminal, bus station and local markets.",
            42.6598,
            18.0860,
            "Open 24h",
            4.4,
            58);

        var familyBeachHotel = await EnsurePlaceAsync(
            "Lapad Family Beach Hotel",
            "Family stay close to beach paths",
            "Lapad Bay, Dubrovnik",
            ptStay,
            lapad,
            "A family-friendly hotel near beach paths, cafes and easy bus connections to Old Town.",
            42.6562,
            18.0686,
            "Open 24h",
            4.5,
            97);

        foreach (var restaurant in new[] { proto, tajMahal, orsan, pantarul })
        {
            await EnsureRestaurantAsync(restaurant);
        }

        await AddPlaceAmenityIfMissingAsync(proto, seaView);
        await AddPlaceAmenityIfMissingAsync(tajMahal, outdoor);
        await AddPlaceAmenityIfMissingAsync(arsenal, seaView);
        await AddPlaceAmenityIfMissingAsync(arsenal, wifiAmenity);
        await AddPlaceAmenityIfMissingAsync(dvino, outdoor);
        await AddPlaceAmenityIfMissingAsync(peppino, family);
        await AddPlaceAmenityIfMissingAsync(orsan, seaView);
        await AddPlaceAmenityIfMissingAsync(pantarul, outdoor);

        var stariStay = await EnsureStayAsync(stariGrad, hotelStayType, 145, "15:00", "11:00");
        var lapadStay = await EnsureStayAsync(lapadSunset, apartmentStayType, 110, "14:00", "10:00");
        var harborStay = await EnsureStayAsync(harborGuesthouse, guesthouseStayType, 78, "14:00", "10:00");
        var beachStay = await EnsureStayAsync(familyBeachHotel, hotelStayType, 132, "15:00", "11:00");

        await AddStayAmenitiesIfMissingAsync(stariStay, wifi, breakfast, airConditioning);
        await AddStayAmenitiesIfMissingAsync(lapadStay, wifi, airConditioning, parking);
        await AddStayAmenitiesIfMissingAsync(harborStay, wifi, airConditioning);
        await AddStayAmenitiesIfMissingAsync(beachStay, wifi, breakfast, pool, parking, airConditioning);

        await AddBookingPartnerIfMissingAsync(stariGrad, booking, "https://example.com/boutique-hotel-stari-grad-booking");
        await AddBookingPartnerIfMissingAsync(stariGrad, direct, "https://example.com/boutique-hotel-stari-grad");
        await AddBookingPartnerIfMissingAsync(lapadSunset, airbnb, "https://example.com/lapad-sunset-apartment-airbnb");
        await AddBookingPartnerIfMissingAsync(harborGuesthouse, booking, "https://example.com/gruz-harbor-guesthouse-booking");
        await AddBookingPartnerIfMissingAsync(familyBeachHotel, booking, "https://example.com/lapad-family-beach-hotel-booking");
        await AddBookingPartnerIfMissingAsync(familyBeachHotel, direct, "https://example.com/lapad-family-beach-hotel");

        await AddPlaceTagsIfMissingAsync(proto, "seafood", "mediterranean", "croatian", "fine-dining", "premium", "old-town");
        await AddPlaceTagsIfMissingAsync(tajMahal, "traditional", "casual", "croatian", "old-town", "local-favorite");
        await AddPlaceTagsIfMissingAsync(arsenal, "breakfast", "specialty-coffee", "waterfront-dining", "sea-view", "old-town");
        await AddPlaceTagsIfMissingAsync(dvino, "wine", "wine-bar", "casual", "hidden-gem", "old-town");
        await AddPlaceTagsIfMissingAsync(peppino, "dessert", "ice-cream", "casual", "budget", "old-town");
        await AddPlaceTagsIfMissingAsync(orsan, "seafood", "grill", "waterfront-dining", "harbor-view", "local-favorite");
        await AddPlaceTagsIfMissingAsync(pantarul, "croatian", "mediterranean", "casual", "local-favorite");

        await AddPlaceTagsIfMissingAsync(rectorsPalace, "history", "culture", "art", "architecture", "museum", "old-town");
        await AddPlaceTagsIfMissingAsync(sponzaPalace, "history", "culture", "architecture", "old-town", "walking-route");
        await AddPlaceTagsIfMissingAsync(franciscan, "history", "culture", "art", "museum", "old-town");
        await AddPlaceTagsIfMissingAsync(warPhoto, "art", "history", "museum", "hidden-gem", "old-town");
        await AddPlaceTagsIfMissingAsync(lovrijenac, "history", "culture", "fortress", "panoramic-view", "walking-route");
        await AddPlaceTagsIfMissingAsync(mountSrd, "nature", "hiking", "panoramic-view", "sunset-view", "walking-route");
        await AddPlaceTagsIfMissingAsync(trstenoArboretum, "nature", "botanical-garden", "walking-route", "quiet-area");
        await AddPlaceTagsIfMissingAsync(betina, "swimming", "kayaking", "cave-beach", "nature", "hidden-gem");
        await AddPlaceTagsIfMissingAsync(copacabana, "swimming", "family-friendly", "popular-spot", "waterfront");
        await AddPlaceTagsIfMissingAsync(bellevueBeach, "swimming", "cave-beach", "quiet-area", "waterfront");
        await AddPlaceTagsIfMissingAsync(cableCar, "cable-car", "panoramic-view", "walking-route", "sunset-view");
        await AddPlaceTagsIfMissingAsync(blueCaveTour, "boat-tour", "diving-snorkeling", "swimming", "nature", "island-hopping");
        await AddPlaceTagsIfMissingAsync(elaphitiTrip, "boat-tour", "island-hopping", "nature", "swimming");
        await AddPlaceTagsIfMissingAsync(lokrumKayak, "kayaking", "swimming", "nature", "island-escape");
        await AddPlaceTagsIfMissingAsync(photoWalk, "photo-walk", "walking-route", "architecture", "old-town", "culture");
        await AddPlaceTagsIfMissingAsync(fishingCharter, "fishing", "boat-tour", "harbor-view", "local-favorite");

        await AddPlaceTagsIfMissingAsync(stariGrad, "boutique-stay", "old-town", "premium", "architecture");
        await AddPlaceTagsIfMissingAsync(lapadSunset, "apartment-stay", "sea-view", "sunset-view", "family-stay");
        await AddPlaceTagsIfMissingAsync(harborGuesthouse, "guesthouse-stay", "harbor-stay", "budget", "local-area");
        await AddPlaceTagsIfMissingAsync(familyBeachHotel, "family-stay", "beach-district", "breakfast", "family-friendly");

        var imageA = "https://images.unsplash.com/photo-1516483638261-f4dbaf036963?auto=format&fit=crop&w=900&q=80";
        var imageB = "https://images.unsplash.com/photo-1507525428034-b723cf961d3e?auto=format&fit=crop&w=900&q=80";
        var imageC = "https://images.unsplash.com/photo-1500530855697-b586d89ba3ee?auto=format&fit=crop&w=900&q=80";
        var imageD = "https://images.unsplash.com/photo-1523906834658-6e24ef2386f9?auto=format&fit=crop&w=900&q=80";
        var imageE = "https://images.unsplash.com/photo-1517248135467-4c7edcad34c4?auto=format&fit=crop&w=900&q=80";
        var imageF = "https://images.unsplash.com/photo-1520250497591-112f2f40a3f4?auto=format&fit=crop&w=900&q=80";
        var imageG = "https://images.unsplash.com/photo-1493246507139-91e8fad9978e?auto=format&fit=crop&w=900&q=80";
        var imageH = "https://images.unsplash.com/photo-1470229722913-7c0e2dbbafd3?auto=format&fit=crop&w=900&q=80";

        foreach (var place in new[] { proto, tajMahal, arsenal, dvino, peppino, orsan, pantarul })
        {
            await AddPlaceImagesIfMissingAsync(place, (imageE, place.Subtitle ?? place.Name));
        }

        foreach (var place in new[] { rectorsPalace, sponzaPalace, franciscan, warPhoto, lovrijenac, photoWalk })
        {
            await AddPlaceImagesIfMissingAsync(place, (imageD, place.Subtitle ?? place.Name));
        }

        foreach (var place in new[] { mountSrd, trstenoArboretum, betina, copacabana, bellevueBeach, cableCar, blueCaveTour, elaphitiTrip, lokrumKayak, fishingCharter })
        {
            await AddPlaceImagesIfMissingAsync(place, (imageB, place.Subtitle ?? place.Name), (imageG, "Outdoor detail"));
        }

        foreach (var place in new[] { stariGrad, lapadSunset, harborGuesthouse, familyBeachHotel })
        {
            await AddPlaceImagesIfMissingAsync(place, (imageF, place.Subtitle ?? place.Name), (imageC, "Stay detail"));
        }

        var marko = await db.Users.FirstOrDefaultAsync(x => x.Email == "marko.p@example.com");
        var elena = await db.Users.FirstOrDefaultAsync(x => x.Email == "elena@example.com");

        if (marko is not null && elena is not null)
        {
            await AddReviewIfMissingAsync(proto, elena, 5, "Polished seafood dinner and a great Old Town location.", 12);
            await AddReviewIfMissingAsync(rectorsPalace, elena, 5, "Useful history stop, especially if you like architecture and museums.", 9);
            await AddReviewIfMissingAsync(mountSrd, marko, 5, "Best sunset view over Dubrovnik.", 7);
            await AddReviewIfMissingAsync(blueCaveTour, marko, 5, "Great water, relaxed boat crew and enough swimming time.", 5);
            await AddReviewIfMissingAsync(stariGrad, elena, 4, "Small, central and easy to use as a base for Old Town walks.", 6);
            await AddReviewIfMissingAsync(fishingCharter, marko, 5, "Good morning route from Gruz and very calm water.", 4);
        }

        var stradunClassical = await EnsureEventAsync(
            "Stradun Classical Night",
            musicType,
            oldTown,
            "Evening classical music performance in an atmospheric Old Town setting.",
            now.AddDays(6).Date.AddHours(20),
            now.AddDays(6).Date.AddHours(22),
            "EUR 20",
            "2 hours",
            "Dubrovnik Chamber Ensemble");

        var gundulicMarket = await EnsureEventAsync(
            "Gundulic Square Farmers Market",
            marketType,
            oldTown,
            "Morning market with fruit, lavender, olive oils and local snacks.",
            now.AddDays(1).Date.AddHours(8),
            now.AddDays(1).Date.AddHours(13),
            "Free entry",
            "5 hours",
            "Local Producers Association");

        var lokrumNatureWalk = await EnsureEventAsync(
            "Lokrum Nature Walk",
            outdoorType,
            lokrum,
            "Guided walk through Lokrum's shaded paths, gardens and coastline.",
            now.AddDays(3).Date.AddHours(10),
            now.AddDays(3).Date.AddHours(13),
            "EUR 12",
            "3 hours",
            "Lokrum Reserve Guides");

        var wallsSunset = await EnsureEventAsync(
            "City Walls Sunset Tour",
            tourType,
            oldTown,
            "Small guided walk around the walls with photo stops and historic context.",
            now.AddDays(4).Date.AddHours(18),
            now.AddDays(4).Date.AddHours(20),
            "EUR 30",
            "2 hours",
            "Old Town Walking Guides");

        var elaphitiBoatDay = await EnsureEventAsync(
            "Elaphiti Boat Day",
            outdoorType,
            gruz,
            "Full-day island-hopping boat event with swim stops and village walks.",
            now.AddDays(9).Date.AddHours(9),
            now.AddDays(9).Date.AddHours(17),
            "EUR 55",
            "8 hours",
            "Gruz Boat Club",
            elaphitiTrip);

        var fishermansEvening = await EnsureEventAsync(
            "Fisherman's Evening in Gruz",
            festivalType,
            gruz,
            "Local harbor evening with grilled fish, simple music and a relaxed waterfront atmosphere.",
            now.AddDays(10).Date.AddHours(19),
            now.AddDays(10).Date.AddHours(23),
            "Free entry",
            "4 hours",
            "Gruz Harbor Community");

        var photoEvent = await EnsureEventAsync(
            "Golden Hour Photo Walk",
            tourType,
            pile,
            "Guided photo walk through Pile, Lovrijenac viewpoints and Old Town streets.",
            now.AddDays(7).Date.AddHours(17),
            now.AddDays(7).Date.AddHours(19),
            "EUR 18",
            "2 hours",
            "Dubrovnik Photo Guides",
            photoWalk);

        await AddEventTagsIfMissingAsync(stradunClassical, "classical-music-event", "ticketed-event", "evening-event");
        await AddEventTagsIfMissingAsync(gundulicMarket, "food-market-event", "free-entry-event", "local-event");
        await AddEventTagsIfMissingAsync(lokrumNatureWalk, "nature-walk-event", "guided-event", "ticketed-event");
        await AddEventTagsIfMissingAsync(wallsSunset, "walking-tour-event", "guided-event", "ticketed-event");
        await AddEventTagsIfMissingAsync(elaphitiBoatDay, "boat-trip-event", "guided-event", "ticketed-event");
        await AddEventTagsIfMissingAsync(fishermansEvening, "local-event", "free-entry-event", "evening-event");
        await AddEventTagsIfMissingAsync(photoEvent, "photography-event", "walking-tour-event", "ticketed-event");

        foreach (var eventEntity in new[] { stradunClassical, fishermansEvening })
        {
            await AddEventImagesIfMissingAsync(eventEntity, (imageH, eventEntity.Name));
        }

        foreach (var eventEntity in new[] { gundulicMarket, lokrumNatureWalk, wallsSunset, elaphitiBoatDay, photoEvent })
        {
            await AddEventImagesIfMissingAsync(eventEntity, (imageA, eventEntity.Name));
        }
    }

    private static async Task EnsureExploreDemoPlaceTagsAsync(AppDbContext db, IReadOnlyDictionary<string, Tag> tagLookup)
    {
        var placesByName = await db.Places
            .ToDictionaryAsync(place => place.Name, StringComparer.OrdinalIgnoreCase);

        async Task AddIfExistsAsync(string placeName, params string[] tagSlugs)
        {
            if (!placesByName.TryGetValue(placeName, out var place))
            {
                return;
            }

            foreach (var tagSlug in tagSlugs)
            {
                if (!tagLookup.TryGetValue(tagSlug, out var tag))
                {
                    continue;
                }

                var exists = await db.PlaceTags.AnyAsync(placeTag =>
                    placeTag.PlaceId == place.Id &&
                    placeTag.TagId == tag.Id);

                if (exists)
                {
                    continue;
                }

                db.PlaceTags.Add(new PlaceTag
                {
                    PlaceId = place.Id,
                    TagId = tag.Id
                });
            }
        }

        await AddIfExistsAsync("Dubrovnik City Walls", "history", "culture", "walking-route");
        await AddIfExistsAsync("Lokrum Island", "nature", "walking-route", "swimming");
        await AddIfExistsAsync("Gruz Fishing Pier", "fishing");
        await AddIfExistsAsync("Lapad Bay Promenade", "walking-route");

        await db.SaveChangesAsync();
    }

    private static void AddDestinationTags(AppDbContext db, Destination destination, params Tag[] tags)
    {
        foreach (var tag in tags)
        {
            db.DestinationTags.Add(new DestinationTag
            {
                DestinationId = destination.Id,
                TagId = tag.Id
            });
        }
    }

    private static void AddLocationTags(AppDbContext db, Location location, params Tag[] tags)
    {
        foreach (var tag in tags)
        {
            db.LocationTags.Add(new LocationTag
            {
                LocationId = location.Id,
                TagId = tag.Id
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
                DisplayName = "marko_p",
                Location = "Belgrade, Serbia",
                NeedsInterestsOnboarding = false,
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
                DisplayName = "elena_r",
                NeedsInterestsOnboarding = false,
                CreatedAt = now
            };

            elena.PasswordHash = hasher.HashPassword(elena, "password123");
            db.Users.Add(elena);
        }

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
            {
                continue;
            }

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

    private static string NormalizeKey(string value) =>
        value.Trim().ToLowerInvariant().Replace(" ", "-", StringComparison.Ordinal);
}

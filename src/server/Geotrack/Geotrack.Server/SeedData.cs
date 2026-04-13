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
    private static string U(string photoId, int w = 800) =>
        $"https://images.unsplash.com/photo-{photoId}?ixlib=rb-4.0.3&auto=format&fit=crop&w={w}&q=75";

    public static async Task InitializeAsync(AppDbContext db)
    {
        if (await db.Destinations.AnyAsync()) return; // Already seeded

        // ── Region ──
        var region = new Region { Name = "Dalmatia", CreatedAt = DateTime.UtcNow };
        db.Regions.Add(region);
        await db.SaveChangesAsync();

        // ── Destination Type ──
        var cityType = new DestinationType { Name = "City", Description = "Urban destination", CreatedAt = DateTime.UtcNow };
        db.DestinationTypes.Add(cityType);
        await db.SaveChangesAsync();

        // ── Destination ──
        var dubrovnik = new Destination
        {
            Name = "Dubrovnik",
            DestinationTypeId = cityType.Id,
            RegionId = region.Id,
            Description = "The Pearl of the Adriatic, known for its distinctive Old Town walls and stunning coastal views.",
            Latitude = 42.6507,
            Longitude = 18.0944,
            ImageUrl = U("1555990793-77610ee3d7c2"),
            CreatedAt = DateTime.UtcNow
        };
        db.Destinations.Add(dubrovnik);
        await db.SaveChangesAsync();

        // ── Location Types ──
        var neighborhoodType = new LocationType { Name = "Neighborhood", Description = "City neighborhood", CreatedAt = DateTime.UtcNow };
        var districtType = new LocationType { Name = "District", Description = "City district", CreatedAt = DateTime.UtcNow };
        db.LocationTypes.AddRange(neighborhoodType, districtType);
        await db.SaveChangesAsync();

        // ── Locations ──
        var oldTown = new Location { Name = "Old Town", DestinationId = dubrovnik.Id, LocationTypeId = neighborhoodType.Id, Latitude = 42.6411, Longitude = 18.1083, ImageUrl = U("1560703650-1e76a3b39c9c"), CreatedAt = DateTime.UtcNow };
        var lapad = new Location { Name = "Lapad", DestinationId = dubrovnik.Id, LocationTypeId = districtType.Id, Latitude = 42.6585, Longitude = 18.0686, ImageUrl = U("1596275281743-e4c30b6e0e27"), CreatedAt = DateTime.UtcNow };
        var gruz = new Location { Name = "Gruž", DestinationId = dubrovnik.Id, LocationTypeId = districtType.Id, Latitude = 42.6597, Longitude = 18.0778, CreatedAt = DateTime.UtcNow };
        db.Locations.AddRange(oldTown, lapad, gruz);
        await db.SaveChangesAsync();

        // ── Place Types ──
        var ptBeach = new PlaceType { Name = "Beach", Description = "Beaches and coastal areas", CreatedAt = DateTime.UtcNow };
        var ptRestaurant = new PlaceType { Name = "Restaurant", Description = "Dining establishments", CreatedAt = DateTime.UtcNow };
        var ptBar = new PlaceType { Name = "Bar", Description = "Bars and pubs", CreatedAt = DateTime.UtcNow };
        var ptMuseum = new PlaceType { Name = "Museum", Description = "Museums and galleries", CreatedAt = DateTime.UtcNow };
        var ptLandmark = new PlaceType { Name = "Landmark", Description = "Landmarks and monuments", CreatedAt = DateTime.UtcNow };
        var ptHotel = new PlaceType { Name = "Hotel", Description = "Hotels and accommodations", CreatedAt = DateTime.UtcNow };
        var ptAttraction = new PlaceType { Name = "Attraction", Description = "Tourist attractions", CreatedAt = DateTime.UtcNow };
        var ptNature = new PlaceType { Name = "Nature", Description = "Nature spots", CreatedAt = DateTime.UtcNow };
        var ptCafe = new PlaceType { Name = "Cafe", Description = "Cafes and coffee shops", CreatedAt = DateTime.UtcNow };
        var ptActivity = new PlaceType { Name = "Activity", Description = "Activities and experiences", CreatedAt = DateTime.UtcNow };
        db.PlaceTypes.AddRange(ptBeach, ptRestaurant, ptBar, ptMuseum, ptLandmark, ptHotel, ptAttraction, ptNature, ptCafe, ptActivity);
        await db.SaveChangesAsync();

        // ── Tags (interest tags + entity tags) ──
        var tags = new List<Tag>
        {
            new() { Name = "Food & Drink", Slug = "food-drink", ForPlace = true, ForEvent = true, CreatedAt = DateTime.UtcNow, ImageUrl = U("1414235077428-338989a2e8c0") },
            new() { Name = "History", Slug = "history", ForPlace = true, ForDestination = true, CreatedAt = DateTime.UtcNow, ImageUrl = U("1560703650-1e76a3b39c9c") },
            new() { Name = "Beaches", Slug = "beaches", ForPlace = true, CreatedAt = DateTime.UtcNow, ImageUrl = U("1507525428034-b723cf961d3e") },
            new() { Name = "Nature", Slug = "nature", ForPlace = true, CreatedAt = DateTime.UtcNow, ImageUrl = U("1441974231531-c6227db76b6e") },
            new() { Name = "Museums", Slug = "museums", ForPlace = true, CreatedAt = DateTime.UtcNow, ImageUrl = U("1564399580075-3af6f48fa3e2") },
            new() { Name = "Local Events", Slug = "local-events", ForEvent = true, CreatedAt = DateTime.UtcNow },
            new() { Name = "Nightlife", Slug = "nightlife", ForPlace = true, ForEvent = true, CreatedAt = DateTime.UtcNow },
            new() { Name = "Festivals", Slug = "festivals", ForEvent = true, CreatedAt = DateTime.UtcNow },
            new() { Name = "Adventure", Slug = "adventure", ForPlace = true, CreatedAt = DateTime.UtcNow },
            new() { Name = "Scenic Walks", Slug = "scenic-walks", ForPlace = true, CreatedAt = DateTime.UtcNow },
            new() { Name = "Culture", Slug = "culture", ForPlace = true, ForDestination = true, CreatedAt = DateTime.UtcNow },
            new() { Name = "Shopping", Slug = "shopping", ForPlace = true, CreatedAt = DateTime.UtcNow },
            // Hotel-specific tags
            new() { Name = "Luxury", Slug = "luxury", ForPlace = true, CreatedAt = DateTime.UtcNow },
            new() { Name = "Budget", Slug = "budget", ForPlace = true, CreatedAt = DateTime.UtcNow },
            new() { Name = "Family Friendly", Slug = "family-friendly", ForPlace = true, ForEvent = true, CreatedAt = DateTime.UtcNow },
            new() { Name = "Spa", Slug = "spa", ForPlace = true, CreatedAt = DateTime.UtcNow },
            new() { Name = "Pool", Slug = "pool", ForPlace = true, CreatedAt = DateTime.UtcNow },
            new() { Name = "Breakfast", Slug = "breakfast", ForPlace = true, CreatedAt = DateTime.UtcNow },
            new() { Name = "Sea View", Slug = "sea-view", ForPlace = true, CreatedAt = DateTime.UtcNow },
            // Event-specific
            new() { Name = "Live Music", Slug = "live-music", ForEvent = true, CreatedAt = DateTime.UtcNow },
            new() { Name = "Cultural", Slug = "cultural", ForEvent = true, ForPlace = true, CreatedAt = DateTime.UtcNow },
            new() { Name = "Outdoor", Slug = "outdoor", ForEvent = true, ForPlace = true, CreatedAt = DateTime.UtcNow },
            new() { Name = "Food & Market", Slug = "food-market", ForEvent = true, CreatedAt = DateTime.UtcNow },
            // Additional
            new() { Name = "Fine Dining", Slug = "fine-dining", ForPlace = true, CreatedAt = DateTime.UtcNow },
            new() { Name = "Beach Bars", Slug = "beach-bars", ForPlace = true, CreatedAt = DateTime.UtcNow },
            new() { Name = "Landmarks", Slug = "landmarks", ForPlace = true, CreatedAt = DateTime.UtcNow },
            new() { Name = "Districts", Slug = "districts", ForPlace = true, CreatedAt = DateTime.UtcNow },
            new() { Name = "Galleries", Slug = "galleries", ForPlace = true, CreatedAt = DateTime.UtcNow },
            new() { Name = "Exhibitions", Slug = "exhibitions", ForPlace = true, CreatedAt = DateTime.UtcNow },
            new() { Name = "Historic Sites", Slug = "historic-sites", ForPlace = true, CreatedAt = DateTime.UtcNow },
            new() { Name = "Islands", Slug = "islands", ForPlace = true, CreatedAt = DateTime.UtcNow },
            new() { Name = "Parks", Slug = "parks", ForPlace = true, CreatedAt = DateTime.UtcNow },
            new() { Name = "Mountains", Slug = "mountains", ForPlace = true, CreatedAt = DateTime.UtcNow },
            new() { Name = "Hiking", Slug = "hiking", ForPlace = true, CreatedAt = DateTime.UtcNow },
            new() { Name = "Scenic Views", Slug = "scenic-views", ForPlace = true, CreatedAt = DateTime.UtcNow },
            new() { Name = "Boutique", Slug = "boutique", ForPlace = true, CreatedAt = DateTime.UtcNow },
        };
        db.Tags.AddRange(tags);
        await db.SaveChangesAsync();

        // Helper to find tag by slug
        Tag T(string slug) => tags.First(t => t.Slug == slug);

        // ── Event Types ──
        var etFestival = new EventType { Name = "Festival", Description = "Music and cultural festivals", CreatedAt = DateTime.UtcNow };
        var etConcert = new EventType { Name = "Concert", Description = "Live music performances", CreatedAt = DateTime.UtcNow };
        var etMarket = new EventType { Name = "Market", Description = "Food and craft markets", CreatedAt = DateTime.UtcNow };
        var etCultural = new EventType { Name = "Cultural", Description = "Cultural events", CreatedAt = DateTime.UtcNow };
        var etOutdoor = new EventType { Name = "Outdoor", Description = "Outdoor activities", CreatedAt = DateTime.UtcNow };
        db.EventTypes.AddRange(etFestival, etConcert, etMarket, etCultural, etOutdoor);
        await db.SaveChangesAsync();

        // ── Amenities ──
        var amenities = new List<Amenity>
        {
            new() { Name = "Swimming Pool", Icon = "pool" },
            new() { Name = "Free WiFi", Icon = "wifi" },
            new() { Name = "Breakfast Included", Icon = "breakfast" },
            new() { Name = "Parking", Icon = "parking" },
            new() { Name = "Spa & Wellness", Icon = "spa" },
            new() { Name = "Fitness Center", Icon = "gym" },
            new() { Name = "Restaurant", Icon = "restaurant" },
            new() { Name = "Air Conditioning", Icon = "ac" },
        };
        db.Amenities.AddRange(amenities);
        await db.SaveChangesAsync();

        Amenity A(string icon) => amenities.First(a => a.Icon == icon);

        // ── Booking Partners ──
        var bpBooking = new BookingPartner { Name = "Booking.com", Variant = "dark" };
        var bpAirbnb = new BookingPartner { Name = "Airbnb", Variant = "brand" };
        var bpExpedia = new BookingPartner { Name = "Expedia", Variant = "muted" };
        db.BookingPartners.AddRange(bpBooking, bpAirbnb, bpExpedia);
        await db.SaveChangesAsync();

        // ── Demo User ──
        var hasher = new PasswordHasher<User>();
        var demoUser = new User
        {
            Username = "marko",
            Email = "marko.p@example.com",
            FirstName = "Marko",
            LastName = "Petrović",
            PasswordHash = "",
            AvatarUrl = U("1507003211169-0a1dd7228f2d", 200),
            DisplayName = "marko_p",
            Location = "San Francisco, CA",
            CreatedAt = DateTime.UtcNow
        };
        demoUser.PasswordHash = hasher.HashPassword(demoUser, "password123");
        db.Users.Add(demoUser);

        var reviewerElena = new User
        {
            Username = "elena",
            Email = "elena@example.com",
            FirstName = "Elena",
            LastName = "Rossi",
            PasswordHash = "",
            AvatarUrl = U("1494790108377-be9c29b29330", 200),
            CreatedAt = DateTime.UtcNow
        };
        reviewerElena.PasswordHash = hasher.HashPassword(reviewerElena, "password123");
        db.Users.Add(reviewerElena);
        await db.SaveChangesAsync();

        // ═══════════════════════════════════
        // PLACES - Beaches
        // ═══════════════════════════════════
        var banjeBeach = new Place
        {
            Name = "Banje Beach", Subtitle = "Dubrovnik's Premier Beach", PlaceTypeId = ptBeach.Id, LocationId = oldTown.Id,
            Description = "Known as one of the most beautiful beaches in Dubrovnik, Banje Beach offers stunning views of the Old Town and Lokrum Island. Enjoy crystal-clear waters, sun loungers, and a vibrant beachfront restaurant. Perfect for both relaxation and vibrant coastal nightlife.",
            ImageUrl = U("1507525428034-b723cf961d3e"), Latitude = 42.6399, Longitude = 18.1131,
            OpeningHours = "6:00 AM - 10:00 PM", AverageRating = 4.8, ReviewCount = 136, CreatedAt = DateTime.UtcNow
        };
        var svJakov = new Place
        {
            Name = "Sveti Jakov Beach", Subtitle = "Hidden gem south of Old Town", PlaceTypeId = ptBeach.Id, LocationId = oldTown.Id,
            Description = "A secluded pebble beach with crystal-clear waters, accessible via a steep staircase from the road above. Less crowded than Banje, offering a more peaceful atmosphere with excellent views.",
            ImageUrl = U("1519046904884-53103b34b206"), Latitude = 42.6369, Longitude = 18.1185,
            OpeningHours = "Open 24h", AverageRating = 4.7, ReviewCount = 89, CreatedAt = DateTime.UtcNow
        };
        var lapadBeach = new Place
        {
            Name = "Lapad Beach", Subtitle = "Family-friendly sandy beach", PlaceTypeId = ptBeach.Id, LocationId = lapad.Id,
            Description = "Lapad Beach is a popular sandy beach with shallow waters, making it ideal for families. The promenade is lined with cafes and restaurants.",
            ImageUrl = U("1473116763249-2faaef81ccda"), Latitude = 42.6567, Longitude = 18.0618,
            OpeningHours = "Open 24h", AverageRating = 4.5, ReviewCount = 72, CreatedAt = DateTime.UtcNow
        };

        // PLACES - Restaurants
        var nautika = new Place
        {
            Name = "Nautika Restaurant", Subtitle = "Fine dining by the sea", PlaceTypeId = ptRestaurant.Id, LocationId = oldTown.Id,
            Description = "One of Croatia's finest restaurants, Nautika offers exquisite Mediterranean cuisine with breathtaking views of the Adriatic Sea and Lovrijenac Fortress. Perfect for a special dining experience.",
            ImageUrl = U("1414235077428-338989a2e8c0"), Latitude = 42.6414, Longitude = 18.1064,
            OpeningHours = "12:00 PM - 11:00 PM", AverageRating = 4.9, ReviewCount = 205, CreatedAt = DateTime.UtcNow
        };
        var buzaBar = new Place
        {
            Name = "Buža Bar", Subtitle = "Cliff bar with stunning views", PlaceTypeId = ptBar.Id, LocationId = oldTown.Id,
            Description = "Perched on the cliffs outside the Old Town walls, Buža Bar is famous for its incredible sunset views and unique ambiance. A must-visit for drinks with a view.",
            ImageUrl = U("1528823872057-9c018a7a7553"), Latitude = 42.6389, Longitude = 18.1095,
            OpeningHours = "9:00 AM - 2:00 AM", AverageRating = 4.7, ReviewCount = 312, CreatedAt = DateTime.UtcNow
        };
        var cogito = new Place
        {
            Name = "Cogito Coffee", Subtitle = "Specialty coffee in Old Town", PlaceTypeId = ptCafe.Id, LocationId = oldTown.Id,
            Description = "Award-winning specialty coffee roaster in the heart of Old Town. Known for carefully sourced beans and expert preparation.",
            ImageUrl = U("1495474472287-4d71bcdd2085"), Latitude = 42.6413, Longitude = 18.1089,
            OpeningHours = "7:30 AM - 9:00 PM", AverageRating = 4.8, ReviewCount = 98, CreatedAt = DateTime.UtcNow
        };
        var kopun = new Place
        {
            Name = "Restaurant Kopun", Subtitle = "Traditional Dalmatian cuisine", PlaceTypeId = ptRestaurant.Id, LocationId = oldTown.Id,
            Description = "Authentic Dalmatian restaurant specializing in traditional recipes with a modern twist. Famous for their signature roasted capon dish and local wines.",
            ImageUrl = U("1555396273-367ea4eb4db5"), Latitude = 42.6413, Longitude = 18.1106,
            OpeningHours = "11:00 AM - 11:00 PM", AverageRating = 4.6, ReviewCount = 167, CreatedAt = DateTime.UtcNow
        };
        var revelin = new Place
        {
            Name = "Culture Club Revelin", Subtitle = "Nightclub in a 16th-century fortress", PlaceTypeId = ptBar.Id, LocationId = oldTown.Id,
            Description = "One of Europe's most unique nightclubs, set inside a 16th-century defensive fortress. Hosts world-renowned DJs and themed events throughout the summer.",
            ImageUrl = U("1516450360452-9258136e97a1"), Latitude = 42.6423, Longitude = 18.1121,
            OpeningHours = "11:00 PM - 5:00 AM", AverageRating = 4.4, ReviewCount = 143, CreatedAt = DateTime.UtcNow
        };
        var proto = new Place
        {
            Name = "Proto Restaurant", Subtitle = "Seafood fine dining", PlaceTypeId = ptRestaurant.Id, LocationId = oldTown.Id,
            Description = "Elegant seafood restaurant known for fresh catches and innovative Mediterranean dishes. Stunning rooftop terrace with views.",
            ImageUrl = U("1559339352-11d035aa65de"), Latitude = 42.6410, Longitude = 18.1100,
            OpeningHours = "11:00 AM - 11:30 PM", AverageRating = 4.7, ReviewCount = 189, CreatedAt = DateTime.UtcNow
        };

        // PLACES - Museums & Culture
        var oldTownWalls = new Place
        {
            Name = "Dubrovnik Old Town Walls", Subtitle = "Iconic medieval fortifications", PlaceTypeId = ptLandmark.Id, LocationId = oldTown.Id,
            Description = "Walk along the magnificent medieval walls that encircle the Old Town. These ancient fortifications offer breathtaking panoramic views of the city's terracotta rooftops and the sparkling Adriatic Sea.",
            ImageUrl = U("1560703650-1e76a3b39c9c"), Latitude = 42.6411, Longitude = 18.1083,
            OpeningHours = "8:00 AM - 6:30 PM", AverageRating = 4.9, ReviewCount = 423, CreatedAt = DateTime.UtcNow
        };
        var rectorsPlace = new Place
        {
            Name = "Rector's Palace", Subtitle = "Gothic-Renaissance masterpiece", PlaceTypeId = ptMuseum.Id, LocationId = oldTown.Id,
            Description = "A stunning Gothic-Renaissance palace that once served as the seat of the Republic of Ragusa. Now houses a museum with period furniture, paintings, and artifacts.",
            ImageUrl = U("1564399580075-3af6f48fa3e2"), Latitude = 42.6407, Longitude = 18.1107,
            OpeningHours = "9:00 AM - 6:00 PM", AverageRating = 4.6, ReviewCount = 187, CreatedAt = DateTime.UtcNow
        };
        var warPhotoMuseum = new Place
        {
            Name = "War Photo Limited", Subtitle = "Powerful photographic exhibition", PlaceTypeId = ptMuseum.Id, LocationId = oldTown.Id,
            Description = "A moving exhibition of war photography documenting conflicts around the world, with a focus on the Croatian War of Independence.",
            ImageUrl = U("1580060839134-75a5edca2e99"), Latitude = 42.6403, Longitude = 18.1089,
            OpeningHours = "10:00 AM - 4:00 PM", AverageRating = 4.5, ReviewCount = 94, CreatedAt = DateTime.UtcNow
        };
        var lovrijenac = new Place
        {
            Name = "Fort Lovrijenac", Subtitle = "Dubrovnik's Gibraltar", PlaceTypeId = ptLandmark.Id, LocationId = oldTown.Id,
            Description = "An imposing fortress perched on a 37-meter high cliff, offering spectacular views of the city and sea. Known as 'Dubrovnik's Gibraltar'.",
            ImageUrl = U("1580587771525-78b9dba3b914"), Latitude = 42.6414, Longitude = 18.1050,
            OpeningHours = "8:00 AM - 7:00 PM", AverageRating = 4.7, ReviewCount = 156, CreatedAt = DateTime.UtcNow
        };
        var stradun = new Place
        {
            Name = "Stradun", Subtitle = "Main street of the Old Town", PlaceTypeId = ptLandmark.Id, LocationId = oldTown.Id,
            Description = "The famous limestone-paved main street of Dubrovnik Old Town, running from the Pile Gate to the Old Port. Lined with shops, cafes, and historic buildings.",
            ImageUrl = U("1590502593747-42a996133562"), Latitude = 42.6414, Longitude = 18.1089,
            AverageRating = 4.8, ReviewCount = 267, CreatedAt = DateTime.UtcNow
        };

        // PLACES - Nature
        var lokrumIsland = new Place
        {
            Name = "Lokrum Island", Subtitle = "Nature paradise nearby", PlaceTypeId = ptNature.Id, LocationId = oldTown.Id,
            Description = "A lush island nature reserve just a 15-minute boat ride from Old Town. Experience the stunning cliffs, vibrant cistern towers, and abundant wildlife in this authentic custard in the beautiful Adriatic.",
            ImageUrl = U("1441974231531-c6227db76b6e"), Latitude = 42.6275, Longitude = 18.1208,
            OpeningHours = "9:00 AM - 6:00 PM", AverageRating = 4.9, ReviewCount = 320, CreatedAt = DateTime.UtcNow
        };
        var mljetPark = new Place
        {
            Name = "Mljet National Park", Subtitle = "Croatia's greenest island", PlaceTypeId = ptNature.Id, LocationId = gruz.Id,
            Description = "Discover majestic mountain peaks, abundant wildlife and stunning landscapes. A pristine destination for hiking and mountain exploration.",
            ImageUrl = U("1469474968028-56623f02e42e"), Latitude = 42.7667, Longitude = 17.5500,
            AverageRating = 4.8, ReviewCount = 210, CreatedAt = DateTime.UtcNow
        };
        var srdHill = new Place
        {
            Name = "Mount Srđ", Subtitle = "Panoramic city viewpoint", PlaceTypeId = ptNature.Id, LocationId = oldTown.Id,
            Description = "Take the cable car or hike to the top for the most spectacular panoramic views of Dubrovnik, the Adriatic Sea, and surrounding islands.",
            ImageUrl = U("1506905925346-21bda4d32df4"), Latitude = 42.6500, Longitude = 18.1100,
            AverageRating = 4.7, ReviewCount = 178, CreatedAt = DateTime.UtcNow
        };
        var elaphitiIslands = new Place
        {
            Name = "Elaphiti Islands", Subtitle = "Island hopping paradise", PlaceTypeId = ptNature.Id, LocationId = gruz.Id,
            Description = "A small archipelago northwest of Dubrovnik, perfect for day trips. Crystal-clear waters, peaceful villages, and beautiful Mediterranean vegetation.",
            ImageUrl = U("1504646268844-76fa29ae5192"), Latitude = 42.7100, Longitude = 17.9400,
            AverageRating = 4.6, ReviewCount = 145, CreatedAt = DateTime.UtcNow
        };
        var sunriseHike = new Place
        {
            Name = "Sunrise Hike Trail", Subtitle = "Dawn trail above Old Town", PlaceTypeId = ptActivity.Id, LocationId = oldTown.Id,
            Description = "A guided sunrise hiking trail through the hills above Dubrovnik, offering breathtaking dawn views over the Old Town and sea.",
            ImageUrl = U("1551632811-561732d1e306"), Latitude = 42.6500, Longitude = 18.1050,
            OpeningHours = "5:30 AM - 9:00 AM", AverageRating = 4.8, ReviewCount = 67, CreatedAt = DateTime.UtcNow
        };

        // ═══════════════════════════════════
        // PLACES - Hotels / Stays
        // ═══════════════════════════════════
        var grandVilla = new Place
        {
            Name = "Villa Adriatica Luxury Resort", Subtitle = "5-star luxury resort", PlaceTypeId = ptHotel.Id, LocationId = lapad.Id,
            Description = "Perched on a cliff overlooking the Adriatic Sea, Villa Adriatica offers an unparalleled luxury experience. Enjoy breathtaking sunset views, exquisite on-site dining, and world-class spa facilities, all just minutes from the historic Old Town.",
            ImageUrl = U("1566073771259-6a6300d8226c"), Latitude = 42.6560, Longitude = 18.0630,
            PricePerNight = 189, AverageRating = 4.8, ReviewCount = 342, CreatedAt = DateTime.UtcNow
        };
        var hotelExcelsior = new Place
        {
            Name = "Hotel Excelsior", Subtitle = "Historic luxury hotel", PlaceTypeId = ptHotel.Id, LocationId = oldTown.Id,
            Description = "A grand historic hotel offering luxurious rooms with stunning sea views. Located near the Old Town, it combines classic elegance with modern amenities.",
            ImageUrl = U("1542314831-de8b4b14c4fc"), Latitude = 42.6395, Longitude = 18.1138,
            PricePerNight = 165, AverageRating = 4.7, ReviewCount = 287, CreatedAt = DateTime.UtcNow
        };
        var grandHotelPark = new Place
        {
            Name = "Grand Hotel Park", Subtitle = "Elegant park-side hotel", PlaceTypeId = ptHotel.Id, LocationId = lapad.Id,
            Description = "An elegant hotel surrounded by lush Mediterranean gardens, offering panoramic views and easy access to Lapad Beach.",
            ImageUrl = U("1551882547-ff40c63fe5fa"), Latitude = 42.6570, Longitude = 18.0650,
            PricePerNight = 120, AverageRating = 4.6, ReviewCount = 198, CreatedAt = DateTime.UtcNow
        };
        var rixos = new Place
        {
            Name = "Rixos Premium Dubrovnik", Subtitle = "Premium all-inclusive resort", PlaceTypeId = ptHotel.Id, LocationId = lapad.Id,
            Description = "A premium all-inclusive resort offering world-class amenities, private beach access, and exceptional dining experiences with Adriatic views.",
            ImageUrl = U("1584132967334-10e028bd69f7"), Latitude = 42.6575, Longitude = 18.0640,
            PricePerNight = 155, AverageRating = 4.5, ReviewCount = 156, CreatedAt = DateTime.UtcNow
        };
        var boutiqueStariGrad = new Place
        {
            Name = "Boutique Hotel Stari Grad", Subtitle = "Charming Old Town boutique", PlaceTypeId = ptHotel.Id, LocationId = oldTown.Id,
            Description = "A charming boutique hotel housed in a renovated medieval building in the heart of Old Town. Intimate atmosphere with uniquely decorated rooms.",
            ImageUrl = U("1520250497591-112f2f40a3f4"), Latitude = 42.6412, Longitude = 18.1095,
            PricePerNight = 95, AverageRating = 4.9, ReviewCount = 89, CreatedAt = DateTime.UtcNow
        };
        var hotelLero = new Place
        {
            Name = "Hotel Lero", Subtitle = "Modern comfort near Gruž", PlaceTypeId = ptHotel.Id, LocationId = gruz.Id,
            Description = "A modern hotel with comfortable rooms and excellent facilities, close to Gruž port and with easy access to Old Town by bus.",
            ImageUrl = U("1564501049412-61c2a3083791"), Latitude = 42.6590, Longitude = 18.0790,
            PricePerNight = 65, AverageRating = 4.4, ReviewCount = 112, CreatedAt = DateTime.UtcNow
        };
        var hiltonImperial = new Place
        {
            Name = "Hilton Imperial Dubrovnik", Subtitle = "Hilton quality at the Pile Gate", PlaceTypeId = ptHotel.Id, LocationId = oldTown.Id,
            Description = "Located right at the Pile Gate entrance to Old Town, offering the perfect blend of historic charm and Hilton luxury standards.",
            ImageUrl = U("1571896349842-33c89424de2d"), Latitude = 42.6418, Longitude = 18.1068,
            PricePerNight = 145, AverageRating = 4.6, ReviewCount = 234, CreatedAt = DateTime.UtcNow
        };

        var allPlaces = new[] { banjeBeach, svJakov, lapadBeach, nautika, buzaBar, cogito, kopun, revelin, proto,
            oldTownWalls, rectorsPlace, warPhotoMuseum, lovrijenac, stradun,
            lokrumIsland, mljetPark, srdHill, elaphitiIslands, sunriseHike,
            grandVilla, hotelExcelsior, grandHotelPark, rixos, boutiqueStariGrad, hotelLero, hiltonImperial };
        db.Places.AddRange(allPlaces);
        await db.SaveChangesAsync();

        // ── Place Tags ──
        void AddPT(Place p, params Tag[] tgs) { foreach (var t in tgs) db.PlaceTags.Add(new PlaceTag { PlaceId = p.Id, TagId = t.Id }); }

        AddPT(banjeBeach, T("beaches"), T("outdoor"), T("scenic-views"));
        AddPT(svJakov, T("beaches"), T("scenic-views"));
        AddPT(lapadBeach, T("beaches"), T("family-friendly"));
        AddPT(nautika, T("food-drink"), T("fine-dining"), T("sea-view"));
        AddPT(buzaBar, T("food-drink"), T("nightlife"), T("scenic-views"), T("beach-bars"));
        AddPT(cogito, T("food-drink"));
        AddPT(kopun, T("food-drink"), T("culture"));
        AddPT(revelin, T("nightlife"), T("culture"), T("live-music"));
        AddPT(proto, T("food-drink"), T("fine-dining"), T("sea-view"));
        AddPT(oldTownWalls, T("history"), T("landmarks"), T("scenic-views"), T("culture"), T("historic-sites"));
        AddPT(rectorsPlace, T("history"), T("museums"), T("culture"));
        AddPT(warPhotoMuseum, T("history"), T("museums"), T("culture"), T("exhibitions"));
        AddPT(lovrijenac, T("history"), T("landmarks"), T("scenic-views"), T("culture"));
        AddPT(stradun, T("history"), T("landmarks"), T("culture"), T("districts"));
        AddPT(lokrumIsland, T("nature"), T("islands"), T("scenic-views"), T("outdoor"));
        AddPT(mljetPark, T("nature"), T("parks"), T("islands"), T("hiking"));
        AddPT(srdHill, T("nature"), T("scenic-views"), T("hiking"), T("mountains"));
        AddPT(elaphitiIslands, T("nature"), T("islands"), T("beaches"));
        AddPT(sunriseHike, T("nature"), T("adventure"), T("hiking"), T("scenic-views"));
        // Hotels
        AddPT(grandVilla, T("luxury"), T("spa"), T("pool"), T("sea-view"), T("breakfast"));
        AddPT(hotelExcelsior, T("luxury"), T("sea-view"), T("spa"), T("pool"));
        AddPT(grandHotelPark, T("pool"), T("breakfast"), T("family-friendly"));
        AddPT(rixos, T("luxury"), T("pool"), T("spa"), T("breakfast"), T("sea-view"));
        AddPT(boutiqueStariGrad, T("boutique"), T("breakfast"));
        AddPT(hotelLero, T("budget"), T("breakfast"), T("family-friendly"));
        AddPT(hiltonImperial, T("luxury"), T("pool"), T("spa"), T("breakfast"));
        await db.SaveChangesAsync();

        // ── Hotel Amenities & Booking Partners ──
        void AddAmenities(Place h, params Amenity[] ams)
        { foreach (var a in ams) db.PlaceAmenities.Add(new PlaceAmenity { PlaceId = h.Id, AmenityId = a.Id }); }
        void AddBP(Place h, params BookingPartner[] bps)
        { foreach (var bp in bps) db.PlaceBookingPartners.Add(new PlaceBookingPartner { PlaceId = h.Id, BookingPartnerId = bp.Id }); }

        AddAmenities(grandVilla, A("pool"), A("wifi"), A("breakfast"), A("spa"), A("gym"), A("restaurant"), A("ac"), A("parking"));
        AddBP(grandVilla, bpBooking, bpAirbnb, bpExpedia);
        AddAmenities(hotelExcelsior, A("pool"), A("wifi"), A("breakfast"), A("spa"), A("restaurant"), A("ac"));
        AddBP(hotelExcelsior, bpBooking, bpExpedia);
        AddAmenities(grandHotelPark, A("pool"), A("wifi"), A("breakfast"), A("parking"), A("restaurant"), A("ac"));
        AddBP(grandHotelPark, bpBooking, bpAirbnb);
        AddAmenities(rixos, A("pool"), A("wifi"), A("breakfast"), A("spa"), A("gym"), A("restaurant"), A("ac"), A("parking"));
        AddBP(rixos, bpBooking, bpExpedia);
        AddAmenities(boutiqueStariGrad, A("wifi"), A("breakfast"), A("ac"));
        AddBP(boutiqueStariGrad, bpBooking, bpAirbnb);
        AddAmenities(hotelLero, A("wifi"), A("breakfast"), A("parking"), A("ac"));
        AddBP(hotelLero, bpBooking);
        AddAmenities(hiltonImperial, A("pool"), A("wifi"), A("breakfast"), A("spa"), A("gym"), A("ac"), A("parking"));
        AddBP(hiltonImperial, bpBooking, bpExpedia);
        await db.SaveChangesAsync();

        // ═══════════════════════════════════
        // EVENTS
        // ═══════════════════════════════════
        var summerFest = new Event
        {
            Name = "Dubrovnik Summer Festival", EventTypeId = etFestival.Id, LocationId = oldTown.Id,
            Description = "Experience an unforgettable evening under the stars. The Summer Festival brings together world-class talent, delicious street food, and an exclusive beachfront atmosphere. Whether you're here to dance barefoot in the sand or simply be captivated by the stage, this is the ultimate summer experience.",
            ImageUrl = U("1533174072545-7a4b6ad7a6c8"),
            StartDate = DateTime.UtcNow.AddDays(5), EndDate = DateTime.UtcNow.AddDays(47),
            TicketPrice = "€25 - €100", Duration = "42 days", HostedBy = "Dubrovnik Summer Festival Foundation",
            VenueDescription = "Multiple venues across Old Town including Lovrijenac Fortress, Rector's Palace courtyard, and the Gradac Park amphitheatre.",
            ArrivalTips = "Arrive 30 minutes before show starts. Parking is limited near Old Town — use public transport or walk from Pile Gate.",
            Accessibility = "Most venues offer wheelchair accessibility. Contact the festival office 48 hours in advance for special seating arrangements.",
            CreatedAt = DateTime.UtcNow
        };
        var jazzNight = new Event
        {
            Name = "Jazz Night at Lazareti", EventTypeId = etConcert.Id, LocationId = oldTown.Id,
            Description = "An intimate evening of jazz performances in the historic Lazareti complex. Featuring local and international jazz artists in a unique courtyard setting.",
            ImageUrl = U("1415201364774-f6f0bb35f28f"),
            StartDate = DateTime.UtcNow.AddDays(3), EndDate = DateTime.UtcNow.AddDays(3).AddHours(4),
            TicketPrice = "€15", Duration = "4 hours", HostedBy = "Dubrovnik Jazz Club",
            VenueDescription = "Lazareti complex, a renovated quarantine building from the 14th century.",
            ArrivalTips = "The venue is a short walk from Ploče Gate. Arrive early for the best seats.",
            Accessibility = "Ground floor venue, wheelchair accessible.",
            CreatedAt = DateTime.UtcNow
        };
        var foodMarket = new Event
        {
            Name = "Old Town Food Market", EventTypeId = etMarket.Id, LocationId = oldTown.Id,
            Description = "A weekly open-air food market featuring local producers, artisan cheeses, olive oils, wines, and traditional Dalmatian delicacies. Taste the authentic flavors of the region.",
            ImageUrl = U("1555939594-58d7cb561ad1"),
            StartDate = DateTime.UtcNow.AddDays(2), EndDate = DateTime.UtcNow.AddDays(2).AddHours(8),
            TicketPrice = "Free entry", Duration = "8 hours", HostedBy = "Dubrovnik City Market Association",
            VenueDescription = "Gundulićeva Poljana, the charming open square in Old Town.",
            ArrivalTips = "Mornings offer the freshest produce. Bring cash for small vendors.",
            Accessibility = "Open-air market, accessible via Old Town gates.",
            CreatedAt = DateTime.UtcNow
        };
        var fullMoonParty = new Event
        {
            Name = "Full Moon Party", EventTypeId = etConcert.Id, LocationId = lapad.Id,
            Description = "Dance under the stars at Lapad's famous beach party. Live DJs, fire performers, and cocktails on the sand.",
            ImageUrl = U("1504680177321-2acddfafcbed"),
            StartDate = DateTime.UtcNow.AddDays(10), EndDate = DateTime.UtcNow.AddDays(10).AddHours(6),
            TicketPrice = "€20", Duration = "6 hours", HostedBy = "Lapad Beach Events",
            CreatedAt = DateTime.UtcNow
        };
        var acousticSunset = new Event
        {
            Name = "Acoustic Sunset Sessions", EventTypeId = etConcert.Id, LocationId = oldTown.Id,
            Description = "Intimate acoustic performances on the Old Town walls as the sun sets over the Adriatic. Local musicians perform traditional and contemporary pieces.",
            ImageUrl = U("1493225457124-a3eb161ffa5f"),
            StartDate = DateTime.UtcNow.AddDays(7), EndDate = DateTime.UtcNow.AddDays(7).AddHours(2),
            TicketPrice = "€10", Duration = "2 hours", HostedBy = "Walls Music Initiative",
            CreatedAt = DateTime.UtcNow
        };
        var culturalWalk = new Event
        {
            Name = "Old Town Cultural Walking Tour", EventTypeId = etCultural.Id, LocationId = oldTown.Id,
            Description = "A guided walking tour through the historic streets of Dubrovnik Old Town, exploring hidden courtyards, medieval churches, and stories of the Republic of Ragusa.",
            ImageUrl = U("1590502593747-42a996133562"),
            StartDate = DateTime.UtcNow.AddDays(1), EndDate = DateTime.UtcNow.AddDays(1).AddHours(3),
            TicketPrice = "€12", Duration = "3 hours", HostedBy = "Dubrovnik Walking Tours",
            CreatedAt = DateTime.UtcNow
        };
        var kayakTour = new Event
        {
            Name = "Sea Kayaking Adventure", EventTypeId = etOutdoor.Id, LocationId = oldTown.Id,
            Description = "Paddle along the stunning Dubrovnik coastline, exploring sea caves, hidden beaches, and the walls from the sea. Includes snorkeling stop at Lokrum Island.",
            ImageUrl = U("1544551763-46a013bb70d5"),
            StartDate = DateTime.UtcNow.AddDays(4), EndDate = DateTime.UtcNow.AddDays(4).AddHours(4),
            TicketPrice = "€35", Duration = "4 hours", HostedBy = "Adriatic Kayak Tours",
            CreatedAt = DateTime.UtcNow
        };

        db.Events.AddRange(summerFest, jazzNight, foodMarket, fullMoonParty, acousticSunset, culturalWalk, kayakTour);
        await db.SaveChangesAsync();

        // ── Event Tags ──
        void AddET(Event e, params Tag[] tgs) { foreach (var t in tgs) db.EventTags.Add(new EventTag { EventId = e.Id, TagId = t.Id }); }
        AddET(summerFest, T("festivals"), T("cultural"), T("live-music"), T("outdoor"));
        AddET(jazzNight, T("live-music"), T("cultural"));
        AddET(foodMarket, T("food-market"), T("food-drink"), T("outdoor"));
        AddET(fullMoonParty, T("nightlife"), T("live-music"), T("outdoor"));
        AddET(acousticSunset, T("live-music"), T("cultural"), T("outdoor"));
        AddET(culturalWalk, T("cultural"), T("history"), T("outdoor"));
        AddET(kayakTour, T("outdoor"), T("adventure"));
        await db.SaveChangesAsync();

        // ── Reviews ──
        var now = DateTime.UtcNow;
        db.PlaceReviews.AddRange(
            new PlaceReview { PlaceId = banjeBeach.Id, UserId = demoUser.Id, Rating = 5, Comment = "Absolutely breathtaking views! The water is pristine and the service at the beachfront restaurant is excellent. It gets busy, so arriving early is highly recommended.", CreatedAt = now.AddDays(-10) },
            new PlaceReview { PlaceId = banjeBeach.Id, UserId = reviewerElena.Id, Rating = 4, Comment = "Beautiful beach with clear water. Can get crowded in summer but worth visiting. The sunset views are spectacular.", CreatedAt = now.AddDays(-25) },
            new PlaceReview { PlaceId = oldTownWalls.Id, UserId = demoUser.Id, Rating = 5, Comment = "A must-do when visiting Dubrovnik! The views are absolutely spectacular from every angle. Go early morning to avoid crowds.", CreatedAt = now.AddDays(-15) },
            new PlaceReview { PlaceId = oldTownWalls.Id, UserId = reviewerElena.Id, Rating = 5, Comment = "Walking along these walls was the highlight of our trip. Simply magical, especially at sunset.", CreatedAt = now.AddDays(-30) },
            new PlaceReview { PlaceId = nautika.Id, UserId = demoUser.Id, Rating = 5, Comment = "The finest dining experience in Dubrovnik. Impeccable service and breathtaking views. The seafood risotto was divine.", CreatedAt = now.AddDays(-8) },
            new PlaceReview { PlaceId = lokrumIsland.Id, UserId = reviewerElena.Id, Rating = 5, Comment = "A paradise just minutes from the city. The botanical gardens are beautiful and the Dead Sea salt lake is a unique experience.", CreatedAt = now.AddDays(-20) },
            new PlaceReview { PlaceId = grandVilla.Id, UserId = demoUser.Id, Rating = 5, Comment = "Exceptional luxury resort. The infinity pool overlooking the sea is a highlight. Staff is incredibly attentive.", CreatedAt = now.AddDays(-12) },
            new PlaceReview { PlaceId = grandVilla.Id, UserId = reviewerElena.Id, Rating = 4, Comment = "Beautiful property with amazing views. The spa is world-class. Only minor note is the restaurant can get busy.", CreatedAt = now.AddDays(-22) },
            new PlaceReview { PlaceId = boutiqueStariGrad.Id, UserId = demoUser.Id, Rating = 5, Comment = "The most charming hotel in Old Town! Each room has its own character. The location cannot be beaten.", CreatedAt = now.AddDays(-5) }
        );
        await db.SaveChangesAsync();

        // ── Destination Tags ──
        db.DestinationTags.AddRange(
            new DestinationTag { DestinationId = dubrovnik.Id, TagId = T("history").Id },
            new DestinationTag { DestinationId = dubrovnik.Id, TagId = T("culture").Id },
            new DestinationTag { DestinationId = dubrovnik.Id, TagId = T("nature").Id },
            new DestinationTag { DestinationId = dubrovnik.Id, TagId = T("beaches").Id }
        );
        await db.SaveChangesAsync();
    }
}

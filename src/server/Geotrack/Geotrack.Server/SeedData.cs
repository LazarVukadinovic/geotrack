using System.Globalization;
using System.Text;
using Geotrack.Application.Services;
using Geotrack.Domain.Entities;
using Geotrack.Domain.Entities.Analytics;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Interests;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Tags;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Geometry = NetTopologySuite.Geometries.Geometry;
using GeometryFactory = NetTopologySuite.Geometries.GeometryFactory;
using Point = NetTopologySuite.Geometries.Point;
using PrecisionModel = NetTopologySuite.Geometries.PrecisionModel;

namespace Geotrack.Server;

public static class SeedData
{
    private const string BoundaryGeometryPropertyName = "BoundaryGeometry";
    private const string BoundaryDisplayGeometryPropertyName = "BoundaryDisplayGeometry";
    private const string GeoBoundariesBoundarySource = "geoBoundaries";
    private const string SeedExplicitBoundarySource = "seed-explicit";
    private const string GeoBoundariesReleaseType = "gbOpen";
    private const string GeoBoundariesLicense = "CC BY 4.0";
    private const string Adm1BoundaryLevel = "ADM1";
    private const string CroatiaIso3 = "HRV";
    private const string SerbiaIso2 = "RS";
    private const string SerbiaIso3 = "SRB";
    private const string CroatiaSeedRegionName = "Dubrovnik-Neretva";
    private const string LegacyCroatiaSeedRegionName = "Dalmatia";
    private const string DemoRegionalManagerEmail = "pera@gmail.com";
    private const string DemoRegionalManagerPassword = "Proba123!";
    private const string DemoRegionalManagerUsername = "pera";
    private const string RegionalManagerRoleName = "RegionalManager";
    private const string TerritoryManagerRoleName = "TerritoryManager";
    private const string DemoRegionalManagerRegionName = CroatiaSeedRegionName;
    private const string SerbiaTerritoryManagerEmail = "nikola@gmail.com";
    private const string SerbiaTerritoryManagerPassword = "Proba123!";
    private const string SerbiaTerritoryManagerUsername = "nikola";
    private const string SerbiaRegionalManagerEmail = "serbia.regional@gmail.com";
    private const string SerbiaRegionalManagerPassword = "Proba123!";
    private const string SerbiaRegionalManagerUsername = "serbia-regional";
    private const string SerbiaRegionalManagerAnchorDestinationName = "Kragujevac";

    private static readonly SemaphoreSlim InitializationLock = new(1, 1);
    private static readonly GeometryFactory Wgs84GeometryFactory = new(new PrecisionModel(), 4326);

    private static readonly string[] LegacySerbiaSeedRegionNames =
    {
        "Central Serbia",
        "Vojvodina",
        "Western Serbia"
    };

    private sealed record SerbiaSeedRegionFallbackDefinition(
        string Key,
        string RegionName,
        string[] Aliases,
        string? Description = null);

    private static readonly SerbiaSeedRegionFallbackDefinition[] SerbiaSeedRegionFallbacks =
    [
        new("belgrade", "Belgrade", ["Beograd"],
            "Belgrade is Serbia's capital and largest city, situated at the confluence of the Sava and Danube rivers. " +
            "It is known for its fortress of Kalemegdan, its bohemian Skadarlija quarter, a vibrant cafe and nightlife culture, " +
            "and a compact historic core surrounded by expanding modern neighbourhoods. " +
            "The city is the political, cultural, and economic centre of the country, and a major hub for regional transport and tourism."),
        new("south-backa", "South Backa District", ["South Bačka District", "Juzna Backa District", "Južna Bačka District"],
            "South Bačka District is the most populous district of Vojvodina, home to Novi Sad — Serbia's second-largest city and a 2022 European Capital of Culture. " +
            "The district is characterised by the flat Pannonian plains, the Danube riverfront, the Fruška Gora national park, and a rich multicultural heritage. " +
            "Novi Sad's Petrovaradin Fortress, historic old town, and Exit music festival attract visitors throughout the year."),
        new("zlatibor", "Zlatibor District", ["Zlatibor"],
            "Zlatibor District is a mountainous region in southwestern Serbia, centred on the Zlatibor plateau at around 1,000 metres elevation. " +
            "It is one of Serbia's most popular domestic tourism destinations, offering ski slopes in winter and hiking, cycling, and fresh-air retreats in summer. " +
            "The area is also known for the Šargan Eight heritage railway, the Drvengrad (Küstendorf) village built by filmmaker Emir Kusturica, " +
            "and traditional wooden architecture across rural villages."),
        new("raska", "Raska District", ["Raška District", "Raska"],
            "Raška District, in south-central Serbia, is the historic heartland of the medieval Serbian state. " +
            "It encompasses the UNESCO-listed monasteries of Sopoćani and Studenica, the spa town of Novi Pazar, " +
            "and the Kopaonik massif — Serbia's premier ski resort and a national park offering mountain activities year-round. " +
            "The region reflects a layered heritage of Byzantine, Ottoman, and Serbian Orthodox traditions."),
        new("sumadija", "Sumadija District", ["Šumadija District", "Sumadija", "Šumadija"],
            "Šumadija District is the geographic and historical heart of Serbia, taking its name from the forests (šuma) that once covered the region. " +
            "Its capital, Kragujevac, was the first capital of modern Serbia and retains significant industrial and cultural history. " +
            "The district is known for its rolling hills, vineyards, plum orchards, and traditional village life, " +
            "as well as the Sumarice Memorial Park commemorating World War II victims.")
    ];

    private static readonly Dictionary<string, Dictionary<string, string>> SeedRegionBoundaryAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["HRV"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Keep the legacy seed alias for repairing existing development/test databases.
                ["Dalmatia"] = "Dubrovnik-Neretva",
                ["Dubrovnik"] = "Dubrovnik-Neretva"
            }
        };

    private static readonly HashSet<string> ManagedDestinationDiscoveryTagTypeSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        "destination-theme", // legacy destination filter group; kept for cleanup/backward compatibility
        "destination-activity",
        "destination-food",
        "destination-stays",
        "destination-vibe",
        "destination-landscape"
    };

    private sealed record SeedTagTypeDefinition(
        string Name,
        string Slug,
        bool ForDestination = false,
        bool ForLocation = false,
        bool ForPlace = false,
        bool ForEvent = false,
        string Description = "",
        string[]? AllowedPlaceTypes = null,
        bool IsFilterable = false,
        string? FilterLabel = null,
        int FilterOrder = 0);

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


    private sealed record SeedInterestTopicDefinition(
        string Name,
        string Slug,
        string Group,
        string IconKey,
        int SortOrder,
        string Description,
        IReadOnlyList<SeedInterestTopicTagDefinition> Tags,
        bool IsActive = true,
        bool ShowOnOnboarding = true);

    private sealed record SeedInterestTopicTagDefinition(string TagSlug, double Weight = 1.0);

    private static string Unsplash(string photoId, int width = 900) =>
        $"https://images.unsplash.com/photo-{photoId}?auto=format&fit=crop&w={width}&q=80";

    private static string SeedMedia(string fileName) => $"/media/seed/{fileName}.svg";

    // Koristi se za slike konvertovane Sharp skriptom (scripts/convert-images.mjs)
    // Primer: SeedWebp("kopaonik-vrh")  →  /media/seed/kopaonik-vrh.webp
    private static string SeedWebp(string fileName) => $"/media/seed/{fileName}.webp";

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

    public static async Task EnsureRequiredPublicContentAsync(AppDbContext db)
    {
        await InitializationLock.WaitAsync();

        try
        {
            await EnsureRequiredPublicContentCoreAsync(db);
        }
        finally
        {
            InitializationLock.Release();
        }
    }

    private static async Task EnsureRequiredPublicContentCoreAsync(AppDbContext db)
    {
        await db.Database.MigrateAsync();

        var tagLookup = await EnsureSmartTagTaxonomyAsync(db);
        await EnsureInterestTopicsAsync(db, tagLookup);
        await EnsureExploreDemoPlaceTagsAsync(db, tagLookup);
        await EnsureDubrovnikRequiredPublicContentAsync(db);
        await EnsureAdditionalExploreDemoDataAsync(db, tagLookup);
        await EnsureSerbiaDemoDataAsync(db, tagLookup);
        await EnsureDestinationDiscoveryTagAssignmentsAsync(db, tagLookup);
        await EnsureCoreEntityImagesAsync(db);
        await PruneDubrovnikDemoCatalogAsync(db);
        await EnsureSeedRegionsLinkedToBoundaryCandidatesAsync(db);
        await EnsureDefaultModerationTermsAsync(db);
    }

    private static async Task InitializeCoreAsync(AppDbContext db)
    {
        await db.Database.MigrateAsync();

        if (await db.Destinations.AnyAsync())
        {
            await EnsureRolesAsync(db);
            await EnsureDemoUsersAsync(db);
            await EnsureDemoRegionalManagerAsync(db);
            var tagLookup1 = await EnsureSmartTagTaxonomyAsync(db);
            await EnsureInterestTopicsAsync(db, tagLookup1);
            await EnsureExploreDemoPlaceTagsAsync(db, tagLookup1);
            await EnsureDubrovnikRequiredPublicContentAsync(db);
            await EnsureAdditionalExploreDemoDataAsync(db, tagLookup1);
            await EnsureSerbiaDemoDataAsync(db, tagLookup1);
            await EnsureDestinationDiscoveryTagAssignmentsAsync(db, tagLookup1);
            await EnsureCoreEntityImagesAsync(db);
            await PruneDubrovnikDemoCatalogAsync(db);
            await EnsureSeedRegionsLinkedToBoundaryCandidatesAsync(db);
            await EnsureDefaultModerationTermsAsync(db);
            return;
        }

        var now = DateTime.UtcNow;

        async Task<DestinationType> EnsureDestinationTypeAsync(string name, string description)
        {
            var destinationType = await db.DestinationTypes.FirstOrDefaultAsync(x => x.Name == name);
            if (destinationType is not null)
            {
                return destinationType;
            }

            destinationType = new DestinationType
            {
                Name = name,
                Description = description,
                CreatedAt = now
            };

            db.DestinationTypes.Add(destinationType);
            await db.SaveChangesAsync();
            return destinationType;
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

        async Task<PlaceType> EnsurePlaceTypeAsync(string name, string description, bool isOwnable = false)
        {
            var placeType = await db.PlaceTypes.FirstOrDefaultAsync(x => x.Name == name);
            if (placeType is not null)
            {
                if (placeType.IsOwnable != isOwnable)
                {
                    placeType.IsOwnable = isOwnable;
                    placeType.UpdatedAt = now;
                    await db.SaveChangesAsync();
                }

                return placeType;
            }

            placeType = new PlaceType
            {
                Name = name,
                Description = description,
                IsOwnable = isOwnable,
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

        var croatia = await EnsureCroatiaTerritoryAsync(db, now);
        await db.SaveChangesAsync();

        var croatiaSeedRegion = await EnsureCroatiaSeedRegionAsync(db, croatia, now);

        var cityType = await EnsureDestinationTypeAsync("City", "Urban destination");

        var dubrovnik = new Destination
        {
            Name = "Dubrovnik",
            DestinationTypeId = cityType.Id,
            RegionId = croatiaSeedRegion.Id,
            Summary = "A compact Adriatic heritage city known for stone walls, coastal views and island escapes.",
            Description = "Dubrovnik is one of the most visited cities on the Adriatic, widely recognised for its remarkably intact medieval city walls — " +
                "a UNESCO World Heritage Site since 1979. The Old Town is largely car-free and can be explored entirely on foot, " +
                "with the main limestone-paved street (Stradun) connecting the Pile Gate to the Ploče Gate. " +
                "The city sits on a peninsula overlooking the clear blue waters of the Adriatic, with the island of Lokrum visible just offshore. " +
                "Beyond the walls, the surrounding coastline and nearby islands offer day-trip options including sea-kayaking, island ferry services, " +
                "and the cable car ascent of Mount Srđ. Dubrovnik has a Mediterranean climate with hot, dry summers and mild winters, " +
                "making it a year-round destination, though peak season (July–August) is intensely busy.",
            Latitude = 42.6507,
            Longitude = 18.0944,
            CreatedAt = now
        };

        db.Destinations.Add(dubrovnik);
        await db.SaveChangesAsync();

        var neighborhoodType = await EnsureLocationTypeAsync("Neighborhood", "City neighborhood");
        var districtType = await EnsureLocationTypeAsync("District", "City district");
        var harborType = await EnsureLocationTypeAsync("Harbor Area", "Harbor or port-side location");

        var oldTown = new Location
        {
            Name = "Old Town",
            Summary = "The historic walled core of Dubrovnik, encircled by medieval fortifications and open to the Adriatic.",
            Description = "Dubrovnik's Old Town is a UNESCO World Heritage Site enclosed by some of the best-preserved medieval city walls in Europe. " +
                "The main pedestrian artery, Stradun, connects the Pile Gate to the Ploče Gate and is lined with limestone-paved streets, " +
                "Renaissance churches, baroque fountains, and open-air cafes. " +
                "Key landmarks include the Rector's Palace, the Franciscan Monastery with its 14th-century pharmacy, " +
                "and a network of steep lanes leading up to the walls walkway with panoramic sea views. " +
                "The area is compact and walkable, best explored on foot in the early morning or evening to avoid peak-hour crowds.",
            DestinationId = dubrovnik.Id,
            LocationTypeId = neighborhoodType.Id,
            Latitude = 42.6413,
            Longitude = 18.1090,
            Status = "Published",
            CreatedAt = now,
            PublishedAt = now
        };

        var ploce = new Location
        {
            Name = "Ploce",
            Summary = "The upscale residential and beach district east of the Old Town walls, home to pebble coves and sea-view terraces.",
            Description = "Ploče is the district immediately east of the Ploče Gate, the eastern entrance to Dubrovnik's Old Town. " +
                "It offers a quieter, more residential feel than the walled city itself, with tree-lined lanes, boutique hotels, " +
                "and direct access to Banje Beach — the closest sandy and pebble beach to the city centre. " +
                "The area is popular with visitors who want proximity to the Old Town combined with sea-swimming access. " +
                "Several luxury hotels are located here, taking advantage of the elevated views over the Adriatic and toward Lokrum Island.",
            DestinationId = dubrovnik.Id,
            LocationTypeId = districtType.Id,
            Latitude = 42.6422,
            Longitude = 18.1180,
            Status = "Published",
            CreatedAt = now,
            PublishedAt = now
        };

        var lapad = new Location
        {
            Name = "Lapad",
            Summary = "A peninsula district west of the city centre with family beaches, a resort strip, and a relaxed bay promenade.",
            Description = "Lapad is a green peninsula to the west of Dubrovnik's centre, separated from the city by the Gruž harbour. " +
                "It is the main resort area, characterised by a long promenade (šetalište) running along Lapad Bay, " +
                "a concentration of hotels and apartment complexes, several pebble and sand beaches, " +
                "and a calmer pace than the Old Town. " +
                "The area is well-suited to families and those who prefer a quieter base with reliable beach access. " +
                "Sunsets from the western edge of the peninsula offer sweeping views over the open Adriatic.",
            DestinationId = dubrovnik.Id,
            LocationTypeId = districtType.Id,
            Latitude = 42.6559,
            Longitude = 18.0712,
            Status = "Published",
            CreatedAt = now,
            PublishedAt = now
        };

        var gruz = new Location
        {
            Name = "Gruz",
            Summary = "The working harbour and ferry port of Dubrovnik, with a daily market, local restaurants, and island-boat connections.",
            Description = "Gruž is Dubrovnik's main port and harbour area, situated to the northwest of the Old Town. " +
                "It serves as the departure point for ferries to the Elaphiti Islands, Korčula, Split, and international lines. " +
                "Unlike the tourist-heavy Old Town, Gruž has a more everyday local character — " +
                "the Gruž morning market (open daily) is one of the best places to buy fresh seafood, produce, and local cheese. " +
                "Several well-regarded konobas and seafood restaurants are found near the waterfront, " +
                "making it a good choice for a meal away from Old Town prices.",
            DestinationId = dubrovnik.Id,
            LocationTypeId = harborType.Id,
            Latitude = 42.6592,
            Longitude = 18.0863,
            Status = "Published",
            CreatedAt = now,
            PublishedAt = now
        };

        db.Locations.AddRange(oldTown, ploce, lapad, gruz);
        await db.SaveChangesAsync();

        var ptBeach = await EnsurePlaceTypeAsync("Beach", "Beaches and coastal swimming areas");
        var ptRestaurant = await EnsurePlaceTypeAsync("Restaurant", "Dining establishments", isOwnable: true);
        var ptCafe = await EnsurePlaceTypeAsync("Cafe", "Cafes and coffee shops", isOwnable: true);
        var ptLandmark = await EnsurePlaceTypeAsync("Landmark", "Landmarks, monuments and historical places");
        var ptNature = await EnsurePlaceTypeAsync("Nature", "Nature spots and island escapes");
        var ptStay = await EnsurePlaceTypeAsync("Stay", "Hotels, apartments, villas and other accommodation", isOwnable: true);
        var ptBar = await EnsurePlaceTypeAsync("Bar", "Bars, wine bars and nightlife venues", isOwnable: true);
        var ptMuseum = await EnsurePlaceTypeAsync("Museum", "Museums and cultural institutions");
        var ptGallery = await EnsurePlaceTypeAsync("Gallery", "Art galleries and exhibition spaces");

        var tagLookup = await EnsureSmartTagTaxonomyAsync(db);
        await EnsureInterestTopicsAsync(db, tagLookup);

        var hotelStayType = await EnsureStayTypeAsync("Hotel", "hotel", "Hotel accommodation");
        var apartmentStayType = await EnsureStayTypeAsync("Apartment", "apartment", "Private apartment accommodation");
        var villaStayType = await EnsureStayTypeAsync("Villa", "villa", "Private villa accommodation");

        var stayAmenities = new List<StayAmenity>
        {
            await EnsureStayAmenityAsync("Free WiFi", "free-wifi", "wifi", "Wireless internet access"),
            await EnsureStayAmenityAsync("Swimming Pool", "swimming-pool", "pool", "Pool available for guests"),
            await EnsureStayAmenityAsync("Breakfast Included", "breakfast-included", "breakfast", "Breakfast included in the stay"),
            await EnsureStayAmenityAsync("Parking", "parking", "parking", "Parking available"),
            await EnsureStayAmenityAsync("Spa", "spa", "spa", "Spa and wellness facilities"),
            await EnsureStayAmenityAsync("Fitness Center", "fitness-center", "gym", "Fitness facilities available"),
            await EnsureStayAmenityAsync("Restaurant", "restaurant", "restaurant", "On-site dining available"),
            await EnsureStayAmenityAsync("Air Conditioning", "air-conditioning", "ac", "Air-conditioned rooms")
        };

        StayAmenity SA(string slug) => stayAmenities.First(x => x.Slug == slug);

        var amenities = new List<Amenity>
        {
            await EnsureAmenityAsync("Free WiFi", "wifi"),
            await EnsureAmenityAsync("Outdoor Seating", "outdoor"),
            await EnsureAmenityAsync("Sea View", "sea"),
            await EnsureAmenityAsync("Family Friendly", "family"),
            await EnsureAmenityAsync("Swimming Pool", "pool"),
            await EnsureAmenityAsync("Parking", "parking"),
            await EnsureAmenityAsync("Spa", "spa"),
            await EnsureAmenityAsync("Gym", "gym"),
            await EnsureAmenityAsync("Breakfast", "breakfast"),
            await EnsureAmenityAsync("Air Conditioning", "ac"),
            await EnsureAmenityAsync("Restaurant", "restaurant"),
        };

        Amenity A(string icon) => amenities.First(x => x.Icon == icon);

        var bpBooking = await EnsureBookingPartnerAsync("Booking.com", "dark");
        var bpAirbnb = await EnsureBookingPartnerAsync("Airbnb", "brand");
        var bpExpedia = await EnsureBookingPartnerAsync("Expedia", "muted");

        var etFestival = await EnsureEventTypeAsync("Festival", "Music and cultural festivals");
        var etMarket = await EnsureEventTypeAsync("Market", "Food and craft markets");
        var etOutdoor = await EnsureEventTypeAsync("Outdoor", "Outdoor activities");
        var etMusic = await EnsureEventTypeAsync("Music", "Music and live performance events");

        await EnsureRolesAsync(db);
        await EnsureDemoRegionalManagerAsync(db);

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
            PlaceTypeId = ptNature.Id,
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
            T("dest-coastal"),
            T("dest-old-town"),
            T("dest-island-nearby"),
            T("dest-historic"),
            T("dest-romantic"),
            T("dest-culture"),
            T("dest-sightseeing"),
            T("dest-swimming"),
            T("dest-boat-trips"),
            T("dest-festivals"),
            T("dest-seafood"),
            T("dest-fine-dining"),
            T("dest-luxury-hotels"),
            T("dest-boutique-hotels"));

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
        await BackfillUserInterestTopicsFromLegacyTagsAsync(db);
        await BackfillUserInterestTopicSignalsFromLegacyTagSignalsAsync(db);

        var summerFest = new Event
        {
            Name = "Dubrovnik Summer Festival",
            EventTypeId = etFestival.Id,
            DestinationId = oldTown.DestinationId,
            LocationId = oldTown.Id,
            Summary = "A cultural summer festival with performances across historic Old Town venues.",
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
            DestinationId = oldTown.DestinationId,
            LocationId = oldTown.Id,
            Summary = "Open-air food market with local produce, olive oils, cheeses and wines.",
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
            DestinationId = ploce.DestinationId,
            LocationId = ploce.Id,
            PlaceId = banjeBeach.Id,
            Summary = "Guided sea kayaking along the Dubrovnik coastline.",
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
            DestinationId = lapad.DestinationId,
            LocationId = lapad.Id,
            Summary = "Relaxed live jazz evening near the promenade.",
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
        await EnsureSerbiaDemoDataAsync(db, tagLookup);
        await EnsureDestinationDiscoveryTagAssignmentsAsync(db, tagLookup);
        await PruneDubrovnikDemoCatalogAsync(db);
        await EnsureSeedRegionsLinkedToBoundaryCandidatesAsync(db);
        await EnsureDefaultModerationTermsAsync(db);
    }

    private static async Task PruneDubrovnikDemoCatalogAsync(AppDbContext db)
    {
        var dubrovnik = await db.Destinations
            .AsNoTracking()
            .FirstOrDefaultAsync(destination => destination.Name == "Dubrovnik");

        if (dubrovnik is null)
        {
            return;
        }

        var keptPlaceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Beaches
            "Banje Beach",
            "Copacabana Beach Dubrovnik",
            "Bellevue Beach",

            // Restaurants, cafes and bars
            "Nautika Restaurant",
            "Proto Fish Restaurant",
            "Pantarul",
            "Cogito Coffee",
            "Gradska Kavana Arsenal",
            "Peppino's Gelato",
            "D'vino Wine Bar",

            // Culture, landmarks and nature
            "Dubrovnik City Walls",
            "Lovrijenac Fortress",
            "Sponza Palace",
            "Dubrovnik Cable Car",
            "Rector's Palace",
            "Franciscan Monastery Museum",
            "War Photo Limited",
            "Lokrum Island",
            "Mount Srd Viewpoint",
            "Trsteno Arboretum",
            "Lapad Bay Promenade",

            // Stays
            "Hotel Excelsior",
            "Boutique Hotel Stari Grad"
        };

        var keptEventNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Dubrovnik Summer Festival",
            "Gundulic Square Farmers Market",
            "City Walls Sunset Tour"
        };

        var placeImages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Banje Beach"] = SeedWebp("banje-beach"),
            ["Copacabana Beach Dubrovnik"] = SeedWebp("copacabana"),
            ["Bellevue Beach"] = SeedWebp("bellevue"),
            ["Nautika Restaurant"] = SeedWebp("nautika-restaurant"),
            ["Proto Fish Restaurant"] = SeedWebp("fish-restaurant-proto"),
            ["Pantarul"] = SeedWebp("pantarul"),
            ["Cogito Coffee"] = SeedWebp("cogito-coffee"),
            ["Gradska Kavana Arsenal"] = SeedWebp("kavana-arsenal"),
            ["Peppino's Gelato"] = SeedWebp("peppinos-gelato"),
            ["D'vino Wine Bar"] = SeedWebp("dvino-wine-bar"),
            ["Dubrovnik City Walls"] = SeedWebp("city-walls-dubrovnik"),
            ["Lovrijenac Fortress"] = SeedWebp("fort-lovrijenac"),
            ["Sponza Palace"] = SeedWebp("spozna"),
            ["Dubrovnik Cable Car"] = SeedWebp("dubrovnik-cable-car"),
            ["Rector's Palace"] = SeedWebp("rectors-palace-dubrovnik"),
            ["Franciscan Monastery Museum"] = SeedWebp("the-franciscan-monastery"),
            ["War Photo Limited"] = SeedWebp("warphoto"),
            ["Lokrum Island"] = SeedWebp("Lokrum-Island"),
            ["Mount Srd Viewpoint"] = SeedWebp("mount-srd"),
            ["Trsteno Arboretum"] = SeedWebp("trsteno-arboretum"),
            ["Lapad Bay Promenade"] = SeedWebp("lapad"),
            ["Hotel Excelsior"] = SeedWebp("EXCELSIOR"),
            ["Boutique Hotel Stari Grad"] = SeedWebp("boutique-stari-grad")
        };

        var eventImages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Dubrovnik Summer Festival"] = SeedWebp("dubrovnik-summer-festival"),
            ["Gundulic Square Farmers Market"] = SeedWebp("gundulic-square-farmers-market"),
            ["City Walls Sunset Tour"] = SeedWebp("sunset-tour")
        };

        var placeWebsites = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Banje Beach"] = "https://banjebeach.com/",
            ["Nautika Restaurant"] = "https://www.nautikarestaurants.com/restaurant-nautika/",
            ["Proto Fish Restaurant"] = "https://www.esculaprestaurants.com/",
            ["Pantarul"] = "https://www.pantarul.com/",
            ["Cogito Coffee"] = "https://www.cogitocoffee.com/locations/",
            ["Gradska Kavana Arsenal"] = "https://www.nautikarestaurants.com/",
            ["Peppino's Gelato"] = "https://peppinosgelato.com/",
            ["D'vino Wine Bar"] = "https://dvino.net/",
            ["Dubrovnik Cable Car"] = "https://www.dubrovnikcablecar.com/",
            ["Lokrum Island"] = "https://www.lokrum.hr/en",
            ["War Photo Limited"] = "https://warphotoltd.com/",
            ["Hotel Excelsior"] = "https://www.adriaticluxuryhotels.com/hotel-excelsior-dubrovnik/",
            ["Boutique Hotel Stari Grad"] = "https://www.hotelstarigrad.com/"
        };

        var placeContacts = new Dictionary<string, (string? Email, string? Number)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Banje Beach"] = ("info@banjebeach.eu", null),
            ["Nautika Restaurant"] = (null, "+385 20 442 526"),
            ["Proto Fish Restaurant"] = ("sales@esculap-teo.hr", "+385 20 323 234"),
            ["Pantarul"] = (null, "+385 20 333 486"),
            ["Peppino's Gelato"] = ("hello@peppinosgelato.com", "+385 91 459 0002"),
            ["Gradska Kavana Arsenal"] = (null, "+385 20 321 202"),
            ["Dubrovnik Cable Car"] = ("info@dubrovnikcablecar.com", "+385 20 414 355"),
            ["Lokrum Island"] = ("prodaja@lokrum.hr", "+385 99 161 1144"),
            ["War Photo Limited"] = ("info@warphotoltd.com", "+385 20 322 166"),
            ["Hotel Excelsior"] = (null, "+385 20 353 353"),
            ["Boutique Hotel Stari Grad"] = (null, "+385 20 322 244")
        };

        var hotelBookingLinks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Hotel Excelsior"] = "https://www.booking.com/hotel/hr/hotelexcelsiordubrovnik.html",
            ["Boutique Hotel Stari Grad"] = "https://www.booking.com/hotel/hr/stari-grad.html"
        };

        var dubrovnikLocationIds = await db.Locations
            .Where(location => location.DestinationId == dubrovnik.Id)
            .Select(location => location.Id)
            .ToListAsync();

        var eventsToRemove = await db.Events
            .Where(eventEntity =>
                eventEntity.DestinationId == dubrovnik.Id &&
                !keptEventNames.Contains(eventEntity.Name))
            .ToListAsync();

        if (eventsToRemove.Count > 0)
        {
            db.Events.RemoveRange(eventsToRemove);
            await db.SaveChangesAsync();
        }

        var placesToRemove = await db.Places
            .Where(place =>
                dubrovnikLocationIds.Contains(place.LocationId) &&
                !keptPlaceNames.Contains(place.Name))
            .ToListAsync();

        if (placesToRemove.Count > 0)
        {
            db.Places.RemoveRange(placesToRemove);
            await db.SaveChangesAsync();
        }

        await ForceSetDestinationImageAsync(dubrovnik.Id, dubrovnik.Name, SeedWebp("dubrovnik"));

        var keptPlaces = await db.Places
            .Where(place => dubrovnikLocationIds.Contains(place.LocationId) && keptPlaceNames.Contains(place.Name))
            .ToListAsync();

        foreach (var place in keptPlaces)
        {
            if (placeImages.TryGetValue(place.Name, out var imageUrl))
            {
                await ForceSetPlaceImageAsync(place, imageUrl);
            }

            var changed = false;
            if (placeWebsites.TryGetValue(place.Name, out var website) && place.Website != website)
            {
                place.Website = website;
                changed = true;
            }

            if (placeContacts.TryGetValue(place.Name, out var contact))
            {
                if (contact.Email is not null && place.ContactEmail != contact.Email)
                {
                    place.ContactEmail = contact.Email;
                    changed = true;
                }

                if (contact.Number is not null && place.Number != contact.Number)
                {
                    place.Number = contact.Number;
                    changed = true;
                }
            }

            if (hotelBookingLinks.TryGetValue(place.Name, out var bookingUrl) && place.BookingExternalLink != bookingUrl)
            {
                place.BookingExternalLink = bookingUrl;
                changed = true;
            }

            if (changed)
            {
                place.UpdatedAt = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync();

        var bookingPartner = await EnsureBookingPartnerAsync("Booking.com", "dark");
        var officialPartner = await EnsureBookingPartnerAsync("Official Website", "muted");

        foreach (var place in keptPlaces)
        {
            await RemovePlaceholderBookingPartnersAsync(place);

            if (hotelBookingLinks.TryGetValue(place.Name, out var bookingUrl))
            {
                await UpsertBookingPartnerAsync(place, bookingPartner, bookingUrl);
            }

            if (placeWebsites.TryGetValue(place.Name, out var website) &&
                place.Name is "Hotel Excelsior" or "Boutique Hotel Stari Grad")
            {
                await UpsertBookingPartnerAsync(place, officialPartner, website);
            }
        }

        var keptEvents = await db.Events
            .Where(eventEntity => eventEntity.DestinationId == dubrovnik.Id && keptEventNames.Contains(eventEntity.Name))
            .ToListAsync();

        foreach (var eventEntity in keptEvents)
        {
            if (eventImages.TryGetValue(eventEntity.Name, out var imageUrl))
            {
                await ForceSetEventImageAsync(eventEntity, imageUrl);
            }

            if (eventEntity.Name == "Dubrovnik Summer Festival")
            {
                eventEntity.ExternalTicketLink = "https://www.dubrovnik-festival.hr/en";
                eventEntity.UpdatedAt = DateTime.UtcNow;
            }
            else if (eventEntity.Name == "City Walls Sunset Tour")
            {
                eventEntity.ExternalTicketLink = "https://www.getyourguide.com/dubrovnik-l513/dubrovnik-city-walls-sunset-walking-tour-t146235/";
                eventEntity.UpdatedAt = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync();

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

        async Task RemovePlaceholderBookingPartnersAsync(Place place)
        {
            var placeholders = await db.PlaceBookingPartners
                .Where(x => x.PlaceId == place.Id && x.Url != null && x.Url.Contains("example.com"))
                .ToListAsync();

            if (placeholders.Count == 0)
            {
                return;
            }

            db.PlaceBookingPartners.RemoveRange(placeholders);
            await db.SaveChangesAsync();
        }

        async Task UpsertBookingPartnerAsync(Place place, BookingPartner partner, string url)
        {
            var existing = await db.PlaceBookingPartners.FirstOrDefaultAsync(x =>
                x.PlaceId == place.Id &&
                x.BookingPartnerId == partner.Id);

            if (existing is null)
            {
                db.PlaceBookingPartners.Add(new PlaceBookingPartner
                {
                    PlaceId = place.Id,
                    BookingPartnerId = partner.Id,
                    Url = url
                });
            }
            else
            {
                existing.Url = url;
            }

            await db.SaveChangesAsync();
        }

        async Task ForceSetDestinationImageAsync(int destinationId, string destinationName, string url)
        {
            var existing = await db.EntityImages
                .Where(x => x.TargetType == EntityImageTargetType.Destination && x.DestinationId == destinationId)
                .ToListAsync();

            if (existing.Count == 1 && existing[0].Url == url)
            {
                return;
            }

            db.EntityImages.RemoveRange(existing);
            db.EntityImages.Add(new EntityImage
            {
                TargetType = EntityImageTargetType.Destination,
                DestinationId = destinationId,
                Url = url,
                Caption = destinationName,
                AltText = destinationName,
                IsMain = true,
                ImageRole = EntityImageRole.Cover,
                SortOrder = 0,
                CreatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        async Task ForceSetPlaceImageAsync(Place place, string url)
        {
            var existing = await db.EntityImages
                .Where(x => x.TargetType == EntityImageTargetType.Place && x.PlaceId == place.Id)
                .ToListAsync();

            if (existing.Count == 1 && existing[0].Url == url)
            {
                return;
            }

            db.EntityImages.RemoveRange(existing);
            db.EntityImages.Add(new EntityImage
            {
                TargetType = EntityImageTargetType.Place,
                PlaceId = place.Id,
                Url = url,
                Caption = place.Name,
                AltText = place.Name,
                IsMain = true,
                ImageRole = EntityImageRole.Cover,
                SortOrder = 0,
                CreatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        async Task ForceSetEventImageAsync(Event eventEntity, string url)
        {
            var existing = await db.EntityImages
                .Where(x => x.TargetType == EntityImageTargetType.Event && x.EventId == eventEntity.Id)
                .ToListAsync();

            if (existing.Count == 1 && existing[0].Url == url)
            {
                return;
            }

            db.EntityImages.RemoveRange(existing);
            db.EntityImages.Add(new EntityImage
            {
                TargetType = EntityImageTargetType.Event,
                EventId = eventEntity.Id,
                Url = url,
                Caption = eventEntity.Name,
                AltText = eventEntity.Name,
                IsMain = true,
                ImageRole = EntityImageRole.Cover,
                SortOrder = 0,
                CreatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }
    }

    private static async Task EnsureSeedRegionsLinkedToBoundaryCandidatesAsync(AppDbContext db)
    {
        if (!await db.AdministrativeBoundaryCandidates.AnyAsync())
        {
            return;
        }

        var regions = await db.Regions
            .Where(region =>
                region.BoundaryCandidateId == null ||
                region.BoundaryExternalKey == null ||
                region.BoundaryShapeGroup == null ||
                region.BoundaryShapeId == null ||
                region.BoundaryAdmLevel != Adm1BoundaryLevel ||
                EF.Property<Geometry?>(region, BoundaryGeometryPropertyName) == null)
            .Select(region => new SeedRegionBoundaryRow
            {
                RegionId = region.Id,
                RegionName = region.Name,
                CountryIso3 = region.Territory.CountryIso3
            })
            .ToListAsync();

        foreach (var region in regions)
        {
            if (string.IsNullOrWhiteSpace(region.CountryIso3))
            {
                continue;
            }

            var candidates = await db.AdministrativeBoundaryCandidates
                .Where(candidate =>
                    candidate.Source == GeoBoundariesBoundarySource &&
                    candidate.AdmLevel == Adm1BoundaryLevel &&
                    candidate.CountryIso3 == region.CountryIso3)
                .Select(candidate => new SeedCandidateBoundaryRow
                {
                    CandidateId = candidate.Id,
                    ReleaseType = candidate.ReleaseType,
                    ShapeGroup = candidate.ShapeGroup,
                    ShapeId = candidate.ShapeId,
                    ShapeName = candidate.ShapeName,
                    ShapeType = candidate.ShapeType,
                    ExternalKey = candidate.ExternalKey,
                    BoundaryGeometry = EF.Property<Geometry?>(candidate, BoundaryGeometryPropertyName),
                    BoundaryDisplayGeometry = EF.Property<Geometry?>(candidate, BoundaryDisplayGeometryPropertyName)
                })
                .ToListAsync();

            var matchedCandidate = ResolveSeedBoundaryCandidate(region, candidates);
            if (matchedCandidate is null || matchedCandidate.BoundaryGeometry is null)
            {
                continue;
            }

            var matchedGeometry = matchedCandidate.BoundaryGeometry!;
            var trackedRegion = await db.Regions.SingleAsync(existing => existing.Id == region.RegionId);
            var now = DateTime.UtcNow;
            trackedRegion.BoundaryCandidateId = matchedCandidate.CandidateId;
            trackedRegion.BoundaryExternalKey = matchedCandidate.ExternalKey;
            trackedRegion.BoundaryShapeGroup = matchedCandidate.ShapeGroup;
            trackedRegion.BoundaryShapeId = matchedCandidate.ShapeId;
            trackedRegion.BoundaryShapeType = matchedGeometry.GeometryType;
            trackedRegion.BoundaryAdmLevel = Adm1BoundaryLevel;
            trackedRegion.BoundarySource = GeoBoundariesBoundarySource;
            trackedRegion.BoundaryReleaseType = string.IsNullOrWhiteSpace(matchedCandidate.ReleaseType)
                ? GeoBoundariesReleaseType
                : matchedCandidate.ReleaseType;
            trackedRegion.BoundaryLicense = GeoBoundariesLicense;
            trackedRegion.BoundaryImportedAt = now;
            trackedRegion.UpdatedAt = now;

            db.Entry(trackedRegion)
                .Property<Geometry?>(BoundaryGeometryPropertyName)
                .CurrentValue = matchedGeometry.Copy();

            if (RegionHasDisplayGeometryProperty(db) && matchedCandidate.BoundaryDisplayGeometry is not null)
            {
                db.Entry(trackedRegion)
                    .Property<Geometry?>(BoundaryDisplayGeometryPropertyName)
                    .CurrentValue = matchedCandidate.BoundaryDisplayGeometry.Copy();
            }

            await db.SaveChangesAsync();
        }
    }

    private static async Task<Territory> EnsureCroatiaTerritoryAsync(AppDbContext db, DateTime now)
    {
        var territory = await db.Territories
            .FirstOrDefaultAsync(item => item.CountryIso3 == "HRV")
            ?? await db.Territories.FirstOrDefaultAsync(item => item.CountryCode == "HR")
            ?? await db.Territories.FirstOrDefaultAsync(item => item.Name == "Croatia");

        const string croatiaDescription =
            "Croatia is a Central European and Mediterranean country situated along the eastern Adriatic coast. " +
            "Known for its stunning coastline of over 1,700 islands, its UNESCO-listed historic cities, and its national parks, " +
            "Croatia offers an exceptional blend of natural beauty, cultural heritage, and year-round tourism appeal. " +
            "From the ancient walled city of Dubrovnik to the lakes of Plitvice and the seafront promenades of Split and Zadar, " +
            "the country attracts millions of visitors seeking history, outdoor adventure, and Mediterranean cuisine.";

        if (territory is null)
        {
            territory = new Territory
            {
                Name = "Croatia",
                Description = croatiaDescription,
                CountryCode = "HR",
                CountryIso3 = "HRV",
                TerritoryKind = TerritoryKinds.Country,
                CreatedAt = now
            };

            db.Territories.Add(territory);
            return territory;
        }

        territory.Name = "Croatia";
        territory.Description ??= croatiaDescription;
        territory.CountryCode = "HR";
        territory.CountryIso3 = "HRV";
        territory.TerritoryKind = TerritoryKinds.Country;
        territory.UpdatedAt ??= now;
        return territory;
    }

    private static async Task EnsureDubrovnikRequiredPublicContentAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        var croatia = await EnsureCroatiaTerritoryAsync(db, now);
        await db.SaveChangesAsync();

        var region = await EnsureCroatiaSeedRegionAsync(db, croatia, now);
        var cityType = await EnsureDestinationTypeAsync(db, "City", "Urban destination", now);

        var destination = await db.Destinations
            .FirstOrDefaultAsync(item => item.Name == "Dubrovnik");

        if (destination is null)
        {
            destination = new Destination
            {
                Name = "Dubrovnik",
                DestinationTypeId = cityType.Id,
                RegionId = region.Id,
                Summary = "A compact Adriatic heritage city known for stone walls, coastal views and island escapes.",
                Description = "Dubrovnik is one of the most visited cities on the Adriatic, widely recognised for its remarkably intact medieval city walls — " +
                    "a UNESCO World Heritage Site since 1979. The Old Town is largely car-free and can be explored entirely on foot, " +
                    "with the main limestone-paved street (Stradun) connecting the Pile Gate to the Ploče Gate. " +
                    "The city sits on a peninsula overlooking the clear blue waters of the Adriatic, with the island of Lokrum visible just offshore. " +
                    "Beyond the walls, the surrounding coastline and nearby islands offer day-trip options including sea-kayaking, island ferry services, " +
                    "and the cable car ascent of Mount Srđ. Dubrovnik has a Mediterranean climate with hot, dry summers and mild winters, " +
                    "making it a year-round destination, though peak season (July–August) is intensely busy.",
                Latitude = 42.6507,
                Longitude = 18.0944,
                Status = "Published",
                CreatedAt = now,
                PublishedAt = now
            };

            db.Destinations.Add(destination);
            await db.SaveChangesAsync();
            return;
        }

        var changed = false;
        if (destination.RegionId != region.Id)
        {
            destination.RegionId = region.Id;
            changed = true;
        }

        if (destination.DestinationTypeId != cityType.Id)
        {
            destination.DestinationTypeId = cityType.Id;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(destination.Summary))
        {
            destination.Summary = "A compact Adriatic heritage city known for stone walls, coastal views and island escapes.";
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(destination.Description))
        {
            destination.Description =
                "Dubrovnik is one of the most visited cities on the Adriatic, widely recognised for its remarkably intact medieval city walls — " +
                "a UNESCO World Heritage Site since 1979. The Old Town is largely car-free and can be explored entirely on foot, " +
                "with the main limestone-paved street (Stradun) connecting the Pile Gate to the Ploče Gate. " +
                "The city sits on a peninsula overlooking the clear blue waters of the Adriatic, with the island of Lokrum visible just offshore. " +
                "Beyond the walls, the surrounding coastline and nearby islands offer day-trip options including sea-kayaking, island ferry services, " +
                "and the cable car ascent of Mount Srđ. Dubrovnik has a Mediterranean climate with hot, dry summers and mild winters, " +
                "making it a year-round destination, though peak season (July–August) is intensely busy.";
            changed = true;
        }

        if (Math.Abs(destination.Latitude - 42.6507) > 1e-9 ||
            Math.Abs(destination.Longitude - 18.0944) > 1e-9)
        {
            destination.Latitude = 42.6507;
            destination.Longitude = 18.0944;
            changed = true;
        }

        if (!string.Equals(destination.Status, "Published", StringComparison.Ordinal))
        {
            destination.Status = "Published";
            changed = true;
        }

        if (destination.PublishedAt is null)
        {
            destination.PublishedAt = now;
            changed = true;
        }

        if (changed)
        {
            destination.UpdatedAt = now;
            await db.SaveChangesAsync();
        }
    }

    private static async Task<DestinationType> EnsureDestinationTypeAsync(
        AppDbContext db,
        string name,
        string description,
        DateTime now)
    {
        var destinationType = await db.DestinationTypes.FirstOrDefaultAsync(x => x.Name == name);
        if (destinationType is not null)
        {
            return destinationType;
        }

        destinationType = new DestinationType
        {
            Name = name,
            Description = description,
            CreatedAt = now
        };

        db.DestinationTypes.Add(destinationType);
        await db.SaveChangesAsync();
        return destinationType;
    }

    private static async Task<Region> EnsureCroatiaSeedRegionAsync(
        AppDbContext db,
        Territory croatia,
        DateTime now)
    {
        var acceptedNames = new[]
            {
                CroatiaSeedRegionName,
                LegacyCroatiaSeedRegionName,
                "Dubrovnik"
            }
            .Select(NormalizeSeedBoundaryName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var matches = await db.Regions
            .Where(item => item.TerritoryId == croatia.Id)
            .OrderBy(item => item.Id)
            .ToListAsync();

        matches = matches
            .Where(item => acceptedNames.Contains(NormalizeSeedBoundaryName(item.Name)))
            .ToList();

        var exactMatches = matches
            .Where(item => NormalizeSeedBoundaryName(item.Name) == NormalizeSeedBoundaryName(CroatiaSeedRegionName))
            .Take(2)
            .ToList();

        Region? region = null;
        if (exactMatches.Count == 1)
        {
            region = exactMatches[0];
        }
        else if (matches.Count == 1)
        {
            region = matches[0];

            if (!string.Equals(region.Name, CroatiaSeedRegionName, StringComparison.Ordinal))
            {
                region.Name = CroatiaSeedRegionName;
                region.UpdatedAt = now;
            }
        }
        else if (matches.Count > 1)
        {
            throw new InvalidOperationException(
                $"Croatia seed cannot choose between multiple fallback ADM1 regions for {CroatiaSeedRegionName}.");
        }

        const string dubrovnikNeretvaDescription =
            "Dubrovnik-Neretva County is the southernmost county of Croatia, stretching along the lower Dalmatian coast " +
            "and encompassing the Pelješac Peninsula, the Neretva delta, and a scattering of islands including Korčula, Mljet, and the Elaphiti archipelago. " +
            "The region is anchored by Dubrovnik, a UNESCO World Heritage Site and one of the most visited cities on the Adriatic. " +
            "Beyond the city walls, the county offers wine routes on Pelješac, kayaking around Lokrum, and pristine national park landscapes on Mljet.";

        if (region is null)
        {
            region = new Region
            {
                Name = CroatiaSeedRegionName,
                Description = dubrovnikNeretvaDescription,
                TerritoryId = croatia.Id,
                BoundaryShapeGroup = CroatiaIso3,
                BoundaryAdmLevel = Adm1BoundaryLevel,
                BoundarySource = SeedExplicitBoundarySource,
                CreatedAt = now
            };

            db.Regions.Add(region);
        }
        else
        {
            region.Description ??= dubrovnikNeretvaDescription;
        }

        var candidate = await FindCroatiaSeedAdm1CandidateAsync(db);
        if (candidate is not null)
        {
            ApplySeedBoundaryCandidate(db, region, candidate, now);
        }
        else if (region.BoundaryCandidateId is null)
        {
            if (string.IsNullOrWhiteSpace(region.BoundaryShapeGroup))
            {
                region.BoundaryShapeGroup = CroatiaIso3;
            }

            if (string.IsNullOrWhiteSpace(region.BoundaryAdmLevel))
            {
                region.BoundaryAdmLevel = Adm1BoundaryLevel;
            }

            if (string.IsNullOrWhiteSpace(region.BoundarySource))
            {
                region.BoundarySource = SeedExplicitBoundarySource;
            }
        }

        await db.SaveChangesAsync();
        return region;
    }

    private static async Task<SeedCandidateBoundaryRow?> FindCroatiaSeedAdm1CandidateAsync(AppDbContext db)
    {
        var candidates = await db.AdministrativeBoundaryCandidates
            .Where(candidate =>
                candidate.Source == GeoBoundariesBoundarySource &&
                candidate.AdmLevel == Adm1BoundaryLevel &&
                candidate.CountryIso3 == CroatiaIso3)
            .Select(candidate => new SeedCandidateBoundaryRow
            {
                CandidateId = candidate.Id,
                ReleaseType = candidate.ReleaseType,
                ShapeGroup = candidate.ShapeGroup,
                ShapeId = candidate.ShapeId,
                ShapeName = candidate.ShapeName,
                ShapeType = candidate.ShapeType,
                ExternalKey = candidate.ExternalKey,
                BoundaryGeometry = EF.Property<Geometry?>(candidate, BoundaryGeometryPropertyName),
                BoundaryDisplayGeometry = EF.Property<Geometry?>(candidate, BoundaryDisplayGeometryPropertyName)
            })
            .ToListAsync();

        return ResolveSeedBoundaryCandidate(
            new SeedRegionBoundaryRow
            {
                RegionName = CroatiaSeedRegionName,
                CountryIso3 = CroatiaIso3
            },
            candidates);
    }

    private static void ApplySeedBoundaryCandidate(
        AppDbContext db,
        Region region,
        SeedCandidateBoundaryRow candidate,
        DateTime now)
    {
        if (candidate.BoundaryGeometry is null)
        {
            return;
        }

        var boundaryGeometry = candidate.BoundaryGeometry.Copy();
        region.BoundaryCandidateId = candidate.CandidateId;
        region.BoundaryExternalKey = candidate.ExternalKey;
        region.BoundaryShapeGroup = candidate.ShapeGroup;
        region.BoundaryShapeId = candidate.ShapeId;
        region.BoundaryShapeType = boundaryGeometry.GeometryType;
        region.BoundaryAdmLevel = Adm1BoundaryLevel;
        region.BoundarySource = GeoBoundariesBoundarySource;
        region.BoundaryReleaseType = string.IsNullOrWhiteSpace(candidate.ReleaseType)
            ? GeoBoundariesReleaseType
            : candidate.ReleaseType;
        region.BoundaryLicense = GeoBoundariesLicense;
        region.BoundaryImportedAt = now;
        region.UpdatedAt = region.CreatedAt == now ? null : now;

        db.Entry(region)
            .Property<Geometry?>(BoundaryGeometryPropertyName)
            .CurrentValue = boundaryGeometry;

        if (RegionHasDisplayGeometryProperty(db) && candidate.BoundaryDisplayGeometry is not null)
        {
            db.Entry(region)
                .Property<Geometry?>(BoundaryDisplayGeometryPropertyName)
                .CurrentValue = candidate.BoundaryDisplayGeometry.Copy();
        }
    }

    private static bool RegionHasDisplayGeometryProperty(AppDbContext db) =>
        db.Model
            .FindEntityType(typeof(Region))
            ?.FindProperty(BoundaryDisplayGeometryPropertyName) is not null;

    private static SeedCandidateBoundaryRow? ResolveSeedBoundaryCandidate(
        SeedRegionBoundaryRow region,
        List<SeedCandidateBoundaryRow> candidates)
    {
        var normalizedRegionName = NormalizeSeedBoundaryName(region.RegionName);
        var exactMatch = ResolveUniqueSeedCandidate(candidates
            .Where(candidate => NormalizeSeedBoundaryName(candidate.ShapeName) == normalizedRegionName));
        if (exactMatch is not null)
        {
            return exactMatch;
        }

        var aliasTarget = ResolveSeedBoundaryAlias(region.CountryIso3, region.RegionName);
        if (string.IsNullOrWhiteSpace(aliasTarget))
        {
            return null;
        }

        var normalizedAliasTarget = NormalizeSeedBoundaryName(aliasTarget);
        return ResolveUniqueSeedCandidate(candidates
            .Where(candidate =>
                string.Equals(candidate.ExternalKey, aliasTarget, StringComparison.OrdinalIgnoreCase) ||
                NormalizeSeedBoundaryName(candidate.ShapeName) == normalizedAliasTarget));
    }

    private static SeedCandidateBoundaryRow? ResolveUniqueSeedCandidate(
        IEnumerable<SeedCandidateBoundaryRow> candidates)
    {
        var matches = candidates
            .Take(2)
            .ToList();

        return matches.Count == 1 ? matches[0] : null;
    }

    private static string? ResolveSeedBoundaryAlias(string? countryIso3, string regionName)
    {
        if (string.IsNullOrWhiteSpace(countryIso3) ||
            string.IsNullOrWhiteSpace(regionName) ||
            !SeedRegionBoundaryAliases.TryGetValue(countryIso3, out var countryAliases))
        {
            return null;
        }

        var normalizedRegionName = NormalizeSeedBoundaryName(regionName);
        return countryAliases
            .FirstOrDefault(pair => NormalizeSeedBoundaryName(pair.Key) == normalizedRegionName)
            .Value;
    }

    private static string NormalizeSeedBoundaryName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var previousWasSpace = false;

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(ch) || ch is '-' or '_' || IsUnicodeDash(ch))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                    previousWasSpace = true;
                }

                continue;
            }

            builder.Append(char.ToLowerInvariant(ch));
            previousWasSpace = false;
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).Trim();
    }

    private static bool IsUnicodeDash(char ch) =>
        ch is '\u2010' or '\u2011' or '\u2012' or '\u2013' or '\u2014';

    private static async Task<Territory> EnsureSerbiaTerritoryAsync(AppDbContext db, DateTime now)
    {
        var territory = await db.Territories
            .FirstOrDefaultAsync(item => item.CountryIso3 == SerbiaIso3)
            ?? await db.Territories.FirstOrDefaultAsync(item => item.CountryCode == SerbiaIso2)
            ?? await db.Territories.FirstOrDefaultAsync(item => item.Name == "Serbia");

        const string serbiaDescription =
            "Serbia is a landlocked country in the heart of the Balkans, bordered by eight countries and traversed by the Danube, Sava, and Morava rivers. " +
            "It offers a compelling mix of medieval monasteries, fortress cities, dynamic urban culture, and mountain resorts. " +
            "Belgrade, the capital, is known for its vibrant nightlife, riverside kafanas, and rich history at the confluence of two great rivers. " +
            "The countryside features Orthodox monasteries in the rolling hills of Šumadija, the dramatic gorges of the Danube, " +
            "the ski slopes of Kopaonik and Zlatibor, and fertile plains stretching through Vojvodina. " +
            "Serbia's cuisine — grilled meats, freshwater fish, and local rakija — reflects a proud culinary tradition.";

        if (territory is null)
        {
            territory = new Territory
            {
                Name = "Serbia",
                Description = serbiaDescription,
                CountryCode = SerbiaIso2,
                CountryIso3 = SerbiaIso3,
                TerritoryKind = TerritoryKinds.Country,
                CreatedAt = now
            };

            db.Territories.Add(territory);
            await db.SaveChangesAsync();
            return territory;
        }

        territory.Description ??= serbiaDescription;

        var changed = false;
        if (territory.Name != "Serbia")
        {
            territory.Name = "Serbia";
            changed = true;
        }

        if (territory.CountryCode != SerbiaIso2)
        {
            territory.CountryCode = SerbiaIso2;
            changed = true;
        }

        if (territory.CountryIso3 != SerbiaIso3)
        {
            territory.CountryIso3 = SerbiaIso3;
            changed = true;
        }

        if (territory.TerritoryKind != TerritoryKinds.Country)
        {
            territory.TerritoryKind = TerritoryKinds.Country;
            changed = true;
        }

        if (changed)
        {
            territory.UpdatedAt = now;
            await db.SaveChangesAsync();
        }

        return territory;
    }

    private static async Task<List<SeedRegionSpatialRow>> EnsureSerbiaAdm1RegionsFromCandidatesAsync(
        AppDbContext db,
        Territory serbia,
        DateTime now)
    {
        var candidates = await db.AdministrativeBoundaryCandidates
            .Where(candidate =>
                candidate.Source == GeoBoundariesBoundarySource &&
                candidate.AdmLevel == Adm1BoundaryLevel &&
                candidate.CountryIso3 == SerbiaIso3)
            .OrderBy(candidate => candidate.ShapeName)
            .ThenBy(candidate => candidate.ShapeId)
            .Select(candidate => new SeedCandidateBoundaryRow
            {
                CandidateId = candidate.Id,
                ReleaseType = candidate.ReleaseType,
                ShapeGroup = candidate.ShapeGroup,
                ShapeId = candidate.ShapeId,
                ShapeName = candidate.ShapeName,
                ShapeType = candidate.ShapeType,
                ExternalKey = candidate.ExternalKey,
                BoundaryGeometry = EF.Property<Geometry?>(candidate, BoundaryGeometryPropertyName),
                BoundaryDisplayGeometry = EF.Property<Geometry?>(candidate, BoundaryDisplayGeometryPropertyName)
            })
            .ToListAsync();

        if (candidates.Count == 0)
        {
            return await LoadSeedRegionSpatialRowsAsync(db, serbia.Id);
        }

        var regions = await db.Regions
            .Where(region => region.TerritoryId == serbia.Id)
            .OrderBy(region => region.Id)
            .ToListAsync();

        var changed = false;
        foreach (var candidate in candidates)
        {
            if (candidate.BoundaryGeometry is null)
            {
                continue;
            }

            var region = ResolveSeedRegionForCandidate(candidate, regions);
            if (region is null)
            {
                region = new Region
                {
                    Name = candidate.ShapeName.Trim(),
                    TerritoryId = serbia.Id,
                    CreatedAt = now
                };

                db.Regions.Add(region);
                regions.Add(region);
                changed = true;
            }
            else if (!string.Equals(region.Name, candidate.ShapeName, StringComparison.Ordinal))
            {
                region.Name = candidate.ShapeName.Trim();
                changed = true;
            }

            ApplySeedBoundaryCandidate(db, region, candidate, now);
            changed = true;
        }

        if (changed)
        {
            await db.SaveChangesAsync();
        }

        return await LoadSeedRegionSpatialRowsAsync(db, serbia.Id);
    }

    private static Region? ResolveSeedRegionForCandidate(
        SeedCandidateBoundaryRow candidate,
        IReadOnlyList<Region> regions)
    {
        var byCandidateId = ResolveUniqueSeedRegion(regions
            .Where(region => region.BoundaryCandidateId == candidate.CandidateId));
        if (byCandidateId is not null)
        {
            return byCandidateId;
        }

        var byExternalKey = ResolveUniqueSeedRegion(regions
            .Where(region =>
                !string.IsNullOrWhiteSpace(region.BoundaryExternalKey) &&
                string.Equals(region.BoundaryExternalKey, candidate.ExternalKey, StringComparison.OrdinalIgnoreCase)));
        if (byExternalKey is not null)
        {
            return byExternalKey;
        }

        var byShapeKey = ResolveUniqueSeedRegion(regions
            .Where(region =>
                string.Equals(region.BoundaryAdmLevel, Adm1BoundaryLevel, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(region.BoundaryShapeGroup, candidate.ShapeGroup, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(region.BoundaryShapeId, candidate.ShapeId, StringComparison.OrdinalIgnoreCase)));
        if (byShapeKey is not null)
        {
            return byShapeKey;
        }

        var normalizedName = NormalizeSeedBoundaryName(candidate.ShapeName);
        return ResolveUniqueSeedRegion(regions
            .Where(region => NormalizeSeedBoundaryName(region.Name) == normalizedName));
    }

    private static Region? ResolveUniqueSeedRegion(IEnumerable<Region> regions)
    {
        var matches = regions.Take(2).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private static async Task<List<SeedRegionSpatialRow>> LoadSeedRegionSpatialRowsAsync(
        AppDbContext db,
        int territoryId)
    {
        var rows = await db.Regions
            .Where(region =>
                region.TerritoryId == territoryId &&
                region.BoundaryCandidateId != null &&
                region.BoundarySource == GeoBoundariesBoundarySource &&
                region.BoundaryAdmLevel == Adm1BoundaryLevel)
            .OrderBy(region => region.Name)
            .ThenBy(region => region.Id)
            .Select(region => new SeedRegionSpatialRow
            {
                Region = region,
                BoundaryGeometry = EF.Property<Geometry?>(region, BoundaryGeometryPropertyName)
            })
            .ToListAsync();

        return rows
            .Where(row => row.BoundaryGeometry is not null)
            .ToList();
    }

    private static async Task<Dictionary<string, Region>> EnsureSerbiaFallbackAdm1RegionsAsync(
        AppDbContext db,
        Territory serbia,
        DateTime now)
    {
        var regions = await db.Regions
            .Where(region => region.TerritoryId == serbia.Id)
            .OrderBy(region => region.Id)
            .ToListAsync();

        var resolved = new Dictionary<string, Region>(StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var definition in SerbiaSeedRegionFallbacks)
        {
            var acceptedNames = definition.Aliases
                .Prepend(definition.RegionName)
                .Select(NormalizeSeedBoundaryName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var matches = regions
                .Where(region => acceptedNames.Contains(NormalizeSeedBoundaryName(region.Name)))
                .ToList();

            var exactMatch = matches
                .Where(region => NormalizeSeedBoundaryName(region.Name) == NormalizeSeedBoundaryName(definition.RegionName))
                .Take(2)
                .ToList();

            Region region;
            if (exactMatch.Count == 1)
            {
                region = exactMatch[0];
            }
            else if (matches.Count == 1)
            {
                region = matches[0];
                if (!string.Equals(region.Name, definition.RegionName, StringComparison.Ordinal))
                {
                    region.Name = definition.RegionName;
                    region.UpdatedAt = now;
                    changed = true;
                }
            }
            else if (matches.Count == 0)
            {
                region = new Region
                {
                    Name = definition.RegionName,
                    Description = definition.Description,
                    TerritoryId = serbia.Id,
                    BoundaryShapeGroup = SerbiaIso3,
                    BoundaryAdmLevel = Adm1BoundaryLevel,
                    BoundarySource = SeedExplicitBoundarySource,
                    CreatedAt = now
                };

                db.Regions.Add(region);
                regions.Add(region);
                changed = true;
            }
            else
            {
                throw new InvalidOperationException(
                    $"Serbia seed cannot choose between multiple fallback ADM1 regions for {definition.RegionName}.");
            }

            if (region.Description is null && definition.Description is not null)
            {
                region.Description = definition.Description;
                region.UpdatedAt = now;
                changed = true;
            }

            if (region.BoundaryCandidateId is null)
            {
                if (string.IsNullOrWhiteSpace(region.BoundaryShapeGroup))
                {
                    region.BoundaryShapeGroup = SerbiaIso3;
                    changed = true;
                }

                if (string.IsNullOrWhiteSpace(region.BoundaryAdmLevel))
                {
                    region.BoundaryAdmLevel = Adm1BoundaryLevel;
                    changed = true;
                }

                if (string.IsNullOrWhiteSpace(region.BoundarySource))
                {
                    region.BoundarySource = SeedExplicitBoundarySource;
                    changed = true;
                }
            }

            resolved[definition.Key] = region;
        }

        if (changed)
        {
            await db.SaveChangesAsync();
        }

        return resolved;
    }

    private static SeedRegionSpatialRow? FindSeedRegionByPoint(
        IReadOnlyList<SeedRegionSpatialRow> regions,
        double latitude,
        double longitude)
    {
        var point = Wgs84GeometryFactory.CreatePoint(
            new NetTopologySuite.Geometries.Coordinate(longitude, latitude));
        point.SRID = 4326;

        var matches = regions
            .Where(region => region.BoundaryGeometry is not null && region.BoundaryGeometry.Covers(point))
            .Take(2)
            .ToList();

        return matches.Count switch
        {
            1 => matches[0],
            > 1 => throw new InvalidOperationException(
                $"Serbia ADM1 seed matched {matches.Count} regions for point {latitude.ToString(CultureInfo.InvariantCulture)}, {longitude.ToString(CultureInfo.InvariantCulture)}."),
            _ => null
        };
    }

    private static async Task RemoveUnreferencedLegacySerbiaRegionsAsync(AppDbContext db, Territory serbia)
    {
        var legacyRegions = await db.Regions
            .Where(region =>
                region.TerritoryId == serbia.Id &&
                LegacySerbiaSeedRegionNames.Contains(region.Name))
            .ToListAsync();

        foreach (var region in legacyRegions)
        {
            var hasDestinations = await db.Destinations.AnyAsync(destination => destination.RegionId == region.Id);
            if (hasDestinations)
            {
                continue;
            }

            var managerAssignments = await db.RegionalManagerRegions
                .Where(assignment => assignment.RegionId == region.Id)
                .ToListAsync();
            db.RegionalManagerRegions.RemoveRange(managerAssignments);
            db.Regions.Remove(region);
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync();
        }
    }

    private static async Task EnsureSerbiaTerritoryManagerAsync(AppDbContext db, Territory serbia)
    {
        var now = DateTime.UtcNow;
        var role = await EnsureSeedRoleAsync(
            db,
            TerritoryManagerRoleName,
            "Can manage territories and assigned regions.",
            now);
        var user = await EnsureSeedUserAsync(
            db,
            SerbiaTerritoryManagerEmail,
            SerbiaTerritoryManagerUsername,
            "Nikola",
            "Territory Manager",
            "Nikola Territory Manager",
            SerbiaTerritoryManagerPassword,
            "Serbia",
            now);

        if (!await db.UserRoles.AnyAsync(existing => existing.UserId == user.Id && existing.RoleId == role.Id))
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = role.Id
            });
        }

        if (!await db.TerritoryManagers.AnyAsync(existing => existing.UserId == user.Id))
        {
            db.TerritoryManagers.Add(new TerritoryManager
            {
                UserId = user.Id
            });
        }

        if (!await db.TerritoryManagerTerritories.AnyAsync(existing =>
                existing.UserId == user.Id &&
                existing.TerritoryId == serbia.Id))
        {
            db.TerritoryManagerTerritories.Add(new TerritoryManagerTerritory
            {
                UserId = user.Id,
                TerritoryId = serbia.Id,
                CreatedAt = now
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task EnsureSerbiaRegionalManagerAsync(AppDbContext db, Region region)
    {
        var now = DateTime.UtcNow;
        var role = await EnsureSeedRoleAsync(
            db,
            RegionalManagerRoleName,
            "Can review and approve regional user contributions.",
            now);
        var user = await EnsureSeedUserAsync(
            db,
            SerbiaRegionalManagerEmail,
            SerbiaRegionalManagerUsername,
            "Serbia",
            "Regional Manager",
            "Serbia Regional Manager",
            SerbiaRegionalManagerPassword,
            region.Name,
            now);

        if (!await db.UserRoles.AnyAsync(existing => existing.UserId == user.Id && existing.RoleId == role.Id))
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = role.Id
            });
        }

        var staleAssignments = await db.RegionalManagerRegions
            .Where(assignment =>
                assignment.UserId == user.Id &&
                assignment.Region.TerritoryId == region.TerritoryId &&
                assignment.RegionId != region.Id &&
                assignment.IsActive)
            .ToListAsync();

        foreach (var staleAssignment in staleAssignments)
        {
            staleAssignment.IsActive = false;
            staleAssignment.UpdatedAt = now;
        }

        var assignment = await db.RegionalManagerRegions.FindAsync(user.Id, region.Id);
        if (assignment is null)
        {
            db.RegionalManagerRegions.Add(new RegionalManagerRegion
            {
                UserId = user.Id,
                RegionId = region.Id,
                IsActive = true,
                CreatedAt = now
            });
        }
        else if (!assignment.IsActive)
        {
            assignment.IsActive = true;
            assignment.UpdatedAt = now;
        }

        await db.SaveChangesAsync();
    }

    private static async Task<Role> EnsureSeedRoleAsync(
        AppDbContext db,
        string name,
        string description,
        DateTime now)
    {
        var role = await db.Roles.FirstOrDefaultAsync(existing => existing.Name == name);
        if (role is not null)
        {
            return role;
        }

        role = new Role
        {
            Name = name,
            Description = description,
            CreatedAt = now
        };

        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return role;
    }

    private static async Task<User> EnsureSeedUserAsync(
        AppDbContext db,
        string email,
        string username,
        string firstName,
        string lastName,
        string displayName,
        string password,
        string location,
        DateTime now)
    {
        var normalizedEmail = email.ToLowerInvariant();
        var hasher = new PasswordHasher<User>();
        var user = await db.Users
            .OrderBy(existing => existing.Id)
            .FirstOrDefaultAsync(existing => existing.Email.ToLower() == normalizedEmail);

        if (user is null)
        {
            user = new User
            {
                Username = await ResolveSeedUsernameAsync(db, username),
                Email = normalizedEmail,
                FirstName = firstName,
                LastName = lastName,
                DisplayName = displayName,
                PhoneNumber = string.Empty,
                Location = location,
                NeedsInterestsOnboarding = false,
                CreatedAt = now
            };

            user.PasswordHash = hasher.HashPassword(user, password);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return user;
        }

        var changed = false;
        if (SeedPasswordNeedsReset(hasher, user, password))
        {
            user.PasswordHash = hasher.HashPassword(user, password);
            changed = true;
        }

        if (user.IsSuspended)
        {
            user.IsSuspended = false;
            user.SuspendedAt = null;
            user.SuspendedByUserId = null;
            changed = true;
        }

        if (user.IsDeleted)
        {
            user.IsDeleted = false;
            user.DeletedAt = null;
            user.DeletedByUserId = null;
            changed = true;
        }

        if (user.NeedsInterestsOnboarding)
        {
            user.NeedsInterestsOnboarding = false;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(user.FirstName))
        {
            user.FirstName = firstName;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(user.LastName))
        {
            user.LastName = lastName;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(user.DisplayName))
        {
            user.DisplayName = displayName;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(user.Location))
        {
            user.Location = location;
            changed = true;
        }

        if (changed)
        {
            user.UpdatedAt = now;
            await db.SaveChangesAsync();
        }

        return user;
    }

    private static async Task RemoveDeprecatedActivityPlaceTypeAsync(AppDbContext db)
    {
        var activityType = await db.PlaceTypes.FirstOrDefaultAsync(x => x.Name == "Activity");
        if (activityType is null)
        {
            return;
        }

        var fallbackType = await db.PlaceTypes.FirstOrDefaultAsync(x => x.Name == "Landmark")
            ?? await db.PlaceTypes.FirstOrDefaultAsync(x => x.Name == "Nature");

        if (fallbackType is not null)
        {
            var places = await db.Places
                .Where(x => x.PlaceTypeId == activityType.Id)
                .ToListAsync();

            foreach (var place in places)
            {
                place.PlaceTypeId = fallbackType.Id;
            }

            var contributions = await db.PlaceContributions
                .Where(x => x.PlaceTypeId == activityType.Id)
                .ToListAsync();

            foreach (var contribution in contributions)
            {
                contribution.PlaceTypeId = fallbackType.Id;
            }
        }

        var tagTypeLinks = await db.TagTypePlaceTypes
            .Where(x => x.PlaceTypeId == activityType.Id)
            .ToListAsync();

        db.TagTypePlaceTypes.RemoveRange(tagTypeLinks);
        db.PlaceTypes.Remove(activityType);
        await db.SaveChangesAsync();
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
                ImageRole = index == 0 ? EntityImageRole.Cover : index == 1 ? EntityImageRole.Card : EntityImageRole.Gallery,
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
                ImageRole = index == 0 ? EntityImageRole.Cover : index == 1 ? EntityImageRole.Card : EntityImageRole.Gallery,
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
                ImageRole = index == 0 ? EntityImageRole.Cover : index == 1 ? EntityImageRole.Card : EntityImageRole.Gallery,
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
                ImageRole = index == 0 ? EntityImageRole.Cover : index == 1 ? EntityImageRole.Card : EntityImageRole.Gallery,
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
                ImageRole = index == 0 ? EntityImageRole.Cover : index == 1 ? EntityImageRole.Card : EntityImageRole.Gallery,
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
                ImageRole = index == 0 ? EntityImageRole.Cover : index == 1 ? EntityImageRole.Card : EntityImageRole.Gallery,
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
        await AddRegionImagesIfMissingAsync(CroatiaSeedRegionName, Unsplash("1507525428034-b723cf961d3e"));
        await AddDestinationImagesIfMissingAsync("Dubrovnik", Unsplash("1555990793-77610ee3d7c2"), SeedMedia("dubrovnik-old-town"));

        await AddLocationImagesIfMissingAsync("Old Town", SeedMedia("dubrovnik-old-town"));
        await AddLocationImagesIfMissingAsync("Lapad", Unsplash("1596275281743-e4c30b6e0e27"));
        await AddLocationImagesIfMissingAsync("Gruz", Unsplash("1500530855697-b586d89ba3ee"));

        await AddPlaceImagesIfMissingAsync("Banje Beach", SeedWebp("banje-beach"));
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
                    IsFilterable = definition.IsFilterable,
                    FilterLabel = definition.FilterLabel,
                    FilterOrder = definition.FilterOrder,
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
                tagType.IsFilterable = definition.IsFilterable;
                tagType.FilterLabel = definition.FilterLabel;
                tagType.FilterOrder = definition.FilterOrder;
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
        // Legacy group. Kept so old rows are updated, but no longer shown as a filter.
        new("Destination Theme", "destination-theme", ForDestination: true, Description: "Legacy high-level destination tags.", IsFilterable: false),

        // Destination-level discovery filters. These describe what a destination is known for,
        // not every single place/location/event inside it.
        new("Destination Activity", "destination-activity", ForDestination: true, Description: "Activities the destination is known for.", IsFilterable: true, FilterLabel: "Activities", FilterOrder: 20),
        new("Destination Food", "destination-food", ForDestination: true, Description: "Food and drink scene the destination is known for.", IsFilterable: true, FilterLabel: "Food", FilterOrder: 30),
        new("Destination Stays", "destination-stays", ForDestination: true, Description: "Accommodation profile the destination is known for.", IsFilterable: true, FilterLabel: "Stays", FilterOrder: 40),
        new("Destination Vibe", "destination-vibe", ForDestination: true, Description: "Overall travel vibe of the destination.", IsFilterable: true, FilterLabel: "Vibe", FilterOrder: 50),
        new("Destination Landscape", "destination-landscape", ForDestination: true, Description: "Landscape and setting of the destination.", IsFilterable: true, FilterLabel: "Landscape", FilterOrder: 60),

        // Entity-level tags stay useful for Explore sections, recommendations and details,
        // but they are not destination discovery filters.
        new("Location Character", "location-character", ForLocation: true, Description: "Area-level character and context."),

        new("Cuisine", "cuisine", ForPlace: true, Description: "Food cuisine tags for restaurants and cafes.", AllowedPlaceTypes: ["Restaurant", "Cafe", "Bar"]),
        new("Dining Style", "dining-style", ForPlace: true, Description: "Dining style and service level.", AllowedPlaceTypes: ["Restaurant", "Cafe", "Bar"]),
        new("Stay Style", "stay-style", ForPlace: true, Description: "Accommodation style and positioning.", AllowedPlaceTypes: ["Stay"]),

        new("Activity", "activity", ForPlace: true, Description: "Activity tags that can become Explore homepage sections.", AllowedPlaceTypes: ["Beach", "Nature", "Landmark", "Museum", "Gallery"]),
        new("Explore Topic", "explore-topic", ForPlace: true, Description: "Broad place topics that can become Explore homepage sections.", AllowedPlaceTypes: ["Beach", "Nature", "Landmark", "Museum", "Gallery", "Attraction"]),
        new("Setting", "setting", ForPlace: true, Description: "Physical setting or atmosphere.", AllowedPlaceTypes: ["Beach", "Restaurant", "Cafe", "Bar", "Landmark", "Nature", "Stay", "Museum", "Gallery"]),
        new("Price Level", "price-level", ForPlace: true, Description: "Simple price positioning.", AllowedPlaceTypes: ["Restaurant", "Cafe", "Bar", "Stay"]),

        new("Event Category", "event-category", ForEvent: true, Description: "Event category and format."),
        new("Event Access", "event-access", ForEvent: true, Description: "Ticketing and access model.")
    ];

    private static List<SeedTagDefinition> BuildSeedTags() =>
    [
        // Destination discovery tags. These are direct destination-level signals.
        // They should not duplicate DestinationType and should not be inferred from every place.
        new("Sightseeing", "dest-sightseeing", "destination-activity", ForDestination: true),
        new("Museums", "dest-museums", "destination-activity", ForDestination: true),
        new("Nightlife", "dest-nightlife", "destination-activity", ForDestination: true),
        new("Festivals", "dest-festivals", "destination-activity", ForDestination: true),
        new("Hiking", "dest-hiking", "destination-activity", ForDestination: true),
        new("Skiing", "dest-skiing", "destination-activity", ForDestination: true),
        new("Swimming", "dest-swimming", "destination-activity", ForDestination: true),
        new("Wellness", "dest-wellness", "destination-activity", ForDestination: true),
        new("Boat Trips", "dest-boat-trips", "destination-activity", ForDestination: true),
        new("River Walks", "dest-river-walks", "destination-activity", ForDestination: true),

        new("Local Food", "dest-local-food", "destination-food", ForDestination: true),
        new("Fine Dining", "dest-fine-dining", "destination-food", ForDestination: true),
        new("Seafood", "dest-seafood", "destination-food", ForDestination: true),
        new("Grill", "dest-grill", "destination-food", ForDestination: true),
        new("Wine Bars", "dest-wine-bars", "destination-food", ForDestination: true),
        new("Cafe Culture", "dest-cafe-culture", "destination-food", ForDestination: true),
        new("Mountain Food", "dest-mountain-food", "destination-food", ForDestination: true),

        new("Luxury Hotels", "dest-luxury-hotels", "destination-stays", ForDestination: true),
        new("Boutique Hotels", "dest-boutique-hotels", "destination-stays", ForDestination: true),
        new("Budget Stays", "dest-budget-stays", "destination-stays", ForDestination: true),
        new("Family Stays", "dest-family-stays", "destination-stays", ForDestination: true),
        new("Spa Hotels", "dest-spa-hotels", "destination-stays", ForDestination: true),
        new("Mountain Lodges", "dest-mountain-lodges", "destination-stays", ForDestination: true),

        new("Historic", "dest-historic", "destination-vibe", ForDestination: true),
        new("Urban", "dest-urban", "destination-vibe", ForDestination: true),
        new("Relaxed", "dest-relaxed", "destination-vibe", ForDestination: true),
        new("Romantic", "dest-romantic", "destination-vibe", ForDestination: true),
        new("Family Friendly", "dest-family-friendly", "destination-vibe", ForDestination: true),
        new("Adventure", "dest-adventure", "destination-vibe", ForDestination: true),
        new("Culture", "dest-culture", "destination-vibe", ForDestination: true),
        new("Party", "dest-party", "destination-vibe", ForDestination: true),

        new("Coastal", "dest-coastal", "destination-landscape", ForDestination: true),
        new("Riverfront", "dest-riverfront", "destination-landscape", ForDestination: true),
        new("Mountain Views", "dest-mountain-views", "destination-landscape", ForDestination: true),
        new("Forest", "dest-forest", "destination-landscape", ForDestination: true),
        new("Lake", "dest-lake", "destination-landscape", ForDestination: true),
        new("Old Town", "dest-old-town", "destination-landscape", ForDestination: true),
        new("Island Nearby", "dest-island-nearby", "destination-landscape", ForDestination: true),

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
        new("Local Food", "local-food", "cuisine", ForPlace: true),

        new("Fine Dining", "fine-dining", "dining-style", ForPlace: true),
        new("Traditional", "traditional", "dining-style", ForPlace: true),
        new("Casual", "casual", "dining-style", ForPlace: true),
        new("Street Food", "street-food", "dining-style", ForPlace: true),
        new("Food Market", "food-market", "dining-style", ForPlace: true),
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
        new("Monastery", "monastery", "explore-topic", ForPlace: true, CanGenerateExploreSection: true),
        new("Religious Site", "religious-site", "explore-topic", ForPlace: true, CanGenerateExploreSection: true),
        new("Garden", "garden", "explore-topic", ForLocation: true, ForPlace: true, CanGenerateExploreSection: true),
        new("Park", "park", "explore-topic", ForLocation: true, ForPlace: true, CanGenerateExploreSection: true),
        new("Gallery", "gallery", "explore-topic", ForPlace: true, CanGenerateExploreSection: true),
        new("Nightlife", "nightlife", "explore-topic", ForPlace: true, ForEvent: true),
        new("Live Music", "live-music", "explore-topic", ForPlace: true, ForEvent: true),
        new("Skiing", "skiing", "activity", ForPlace: true, CanGenerateExploreSection: true),

        new("Budget", "budget", "price-level", ForPlace: true),
        new("Budget Friendly", "budget-friendly", "price-level", ForPlace: true),
        new("Luxury", "luxury", "price-level", ForPlace: true),
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




    private static List<SeedInterestTopicDefinition> BuildSeedInterestTopics() =>
    [
        new(
            "Beaches",
            "beaches",
            "Nature & Outdoors",
            "waves",
            10,
            "Beaches, swimming spots, coastal views and relaxed seaside days.",
            [
                new("swimming", 3), new("pebble-beach", 2.5), new("cave-beach", 2.25),
                new("sea-view", 2), new("waterfront", 1.75), new("dest-swimming", 2), new("dest-coastal", 1.75)
            ]),

        new(
            "Islands & Boat Trips",
            "islands-boat-trips",
            "Nature & Outdoors",
            "boat",
            20,
            "Islands, boat tours, island hopping and sea excursions.",
            [
                new("island-escape", 3), new("boat-tour", 3), new("island-hopping", 2.75),
                new("boat-trip-event", 2.25), new("diving-snorkeling", 1.75), new("dest-island-nearby", 2), new("dest-boat-trips", 2)
            ]),

        new(
            "Mountains & Hiking",
            "mountains-hiking",
            "Nature & Outdoors",
            "mountain",
            30,
            "Mountain views, hiking paths, forests, ski areas and active outdoor routes.",
            [
                new("hiking", 3), new("walking-route", 2.5), new("panoramic-view", 2.25),
                new("nature", 2), new("skiing", 2), new("dest-hiking", 2.25), new("dest-mountain-views", 2), new("dest-forest", 1.75), new("dest-skiing", 1.75)
            ]),

        new(
            "Gardens & Parks",
            "gardens-parks",
            "Nature & Outdoors",
            "flower",
            40,
            "Botanical gardens, parks, arboretums and calm green areas.",
            [
                new("botanical-garden", 3), new("garden", 3), new("park", 2.75), new("nature-reserve", 2.25),
                new("nature", 2), new("quiet-area", 1.5), new("walking-route", 1.5), new("dest-forest", 1.75), new("dest-relaxed", 1.25)
            ]),

        new(
            "Lakes & Rivers",
            "lakes-rivers",
            "Nature & Outdoors",
            "droplets",
            50,
            "Lakes, rivers, waterfront walks and calmer water-based places.",
            [
                new("waterfront", 3), new("walking-route", 2), new("nature", 2), new("quiet-area", 1.5),
                new("dest-lake", 2.5), new("dest-river-walks", 2.25), new("dest-riverfront", 2)
            ]),

        new(
            "Viewpoints & Sunsets",
            "viewpoints-sunsets",
            "Nature & Outdoors",
            "sunset",
            60,
            "Panoramic viewpoints, sunset spots, cable cars and scenic photo stops.",
            [
                new("panoramic-view", 3), new("sunset-view", 2.75), new("sea-view", 2), new("cable-car", 2),
                new("photo-walk", 1.75), new("photography-event", 1.75), new("dest-mountain-views", 2), new("dest-sightseeing", 1.5)
            ]),

        new(
            "Local Food",
            "local-food",
            "Food & Drink",
            "utensils",
            110,
            "Traditional restaurants, local favorites, markets and regional food experiences.",
            [
                new("local-food", 3), new("traditional", 3), new("local-favorite", 2.5), new("food-market", 2.25),
                new("food-market-event", 2.25), new("casual", 1.25), new("dest-local-food", 2.5), new("dest-mountain-food", 1.75)
            ]),

        new(
            "Seafood",
            "seafood",
            "Food & Drink",
            "fish",
            120,
            "Seafood restaurants, coastal dining and fresh fish experiences.",
            [
                new("seafood", 3), new("mediterranean", 2), new("waterfront-dining", 2), new("sea-view", 1.5),
                new("harbor-view", 1.25), new("dest-seafood", 2.5), new("dest-coastal", 1.5)
            ]),

        new(
            "Cafes & Coffee",
            "cafes-coffee",
            "Food & Drink",
            "coffee",
            130,
            "Specialty coffee, cafes, desserts and relaxed coffee stops.",
            [
                new("specialty-coffee", 3), new("breakfast", 1.75), new("dessert", 1.75), new("ice-cream", 1.5),
                new("casual", 1.25), new("dest-cafe-culture", 2.5)
            ]),

        new(
            "Wine & Bars",
            "wine-bars",
            "Food & Drink",
            "wine",
            140,
            "Wine bars, casual bars, nightlife spots and evening drinks.",
            [
                new("wine", 3), new("wine-bar", 3), new("nightlife", 2), new("evening-event", 1.75),
                new("hidden-gem", 1.25), new("dest-wine-bars", 2.5), new("dest-nightlife", 2), new("dest-party", 1.5)
            ]),

        new(
            "Fine Dining",
            "fine-dining-interest",
            "Food & Drink",
            "sparkles",
            150,
            "Premium restaurants, refined dining, romantic dinners and polished service.",
            [
                new("fine-dining", 3), new("premium", 2.5), new("luxury", 2), new("mediterranean", 1.5),
                new("waterfront-dining", 1.5), new("dest-fine-dining", 2.5)
            ]),

        new(
            "Street Food & Casual Eats",
            "street-food-casual",
            "Food & Drink",
            "sandwich",
            160,
            "Quick casual food, grills, desserts, simple local places and informal meals.",
            [
                new("street-food", 3), new("casual", 2.5), new("grill", 2.25), new("dessert", 1.5),
                new("ice-cream", 1.5), new("budget", 1.5), new("dest-grill", 2)
            ]),

        new(
            "Museums",
            "museums",
            "Culture & Heritage",
            "museum",
            210,
            "Museums, collections, exhibitions and indoor cultural visits.",
            [
                new("museum", 3), new("art", 2), new("history", 2), new("culture", 2),
                new("dest-museums", 2.5), new("dest-culture", 1.75)
            ]),

        new(
            "Monasteries & Religious Sites",
            "monasteries-religious-sites",
            "Culture & Heritage",
            "church",
            220,
            "Monasteries, religious heritage, cloisters, churches and quiet historic sites.",
            [
                new("monastery", 3), new("religious-site", 3), new("history", 2), new("culture", 2),
                new("architecture", 1.75), new("old-town", 1), new("dest-historic", 1.75), new("dest-culture", 1.5)
            ]),

        new(
            "Fortresses & City Walls",
            "fortresses-city-walls",
            "Culture & Heritage",
            "shield",
            230,
            "Fortresses, medieval walls, citadels and historic defensive landmarks.",
            [
                new("fortress", 3), new("medieval-walls", 3), new("history", 2.25), new("panoramic-view", 1.5),
                new("walking-route", 1.25), new("dest-historic", 2), new("dest-sightseeing", 1.5)
            ]),

        new(
            "Old Towns & Architecture",
            "old-towns-architecture",
            "Culture & Heritage",
            "landmark",
            240,
            "Old towns, historic streets, architecture, palaces and walkable heritage cores.",
            [
                new("old-town", 3), new("old-town-core", 2.75), new("architecture", 2.75), new("history", 2),
                new("culture", 1.75), new("walking-route", 1.5), new("dest-old-town", 2.5), new("dest-historic", 1.75)
            ]),

        new(
            "Galleries & Art",
            "galleries-art",
            "Culture & Heritage",
            "palette",
            250,
            "Galleries, art spaces, photography exhibitions and creative cultural content.",
            [
                new("gallery", 3), new("art", 3), new("museum", 1.75), new("culture", 1.75),
                new("photography-event", 2), new("hidden-gem", 1.25), new("dest-culture", 1.5)
            ]),

        new(
            "Guided Tours",
            "guided-tours",
            "Activities & Events",
            "map",
            310,
            "Guided walks, organized tours, photo walks and curated sightseeing experiences.",
            [
                new("guided-tour", 3), new("guided-event", 2.75), new("walking-tour-event", 2.5), new("photo-walk", 2),
                new("dest-sightseeing", 2), new("dest-adventure", 1.5)
            ]),

        new(
            "Adventure Activities",
            "adventure-activities",
            "Activities & Events",
            "compass",
            320,
            "Kayaking, hiking, active tours, cable cars and outdoor adventures.",
            [
                new("kayaking", 3), new("hiking", 2.5), new("outdoor-tour-event", 2.5), new("cable-car", 2),
                new("diving-snorkeling", 2), new("dest-adventure", 2.5), new("dest-hiking", 1.75)
            ]),

        new(
            "Festivals & Live Music",
            "festivals-live-music",
            "Activities & Events",
            "music",
            330,
            "Festivals, concerts, classical music, live performances and seasonal events.",
            [
                new("cultural-festival", 3), new("live-music-event", 3), new("classical-music-event", 2.5),
                new("live-music", 2), new("summer-event", 1.75), new("ticketed-event", 1), new("dest-festivals", 2.5), new("dest-nightlife", 1.5)
            ]),

        new(
            "Nightlife & Evening Events",
            "nightlife-evening-events",
            "Activities & Events",
            "moon",
            340,
            "Evening events, nightlife, bars, live music and after-dark local experiences.",
            [
                new("nightlife", 3), new("evening-event", 3), new("live-music-event", 2.25), new("wine-bar", 1.5),
                new("dest-nightlife", 2.5), new("dest-party", 2), new("dest-festivals", 1.25)
            ]),

        new(
            "Family Friendly",
            "family-friendly-interest",
            "Travel Style",
            "heart",
            410,
            "Easy places and experiences suitable for families and slower days.",
            [
                new("family-friendly", 3), new("family-stay", 2.25), new("relaxed-area", 1.75), new("swimming", 1.5),
                new("walking-route", 1.25), new("dest-family-friendly", 2.5), new("dest-family-stays", 2)
            ]),

        new(
            "Relaxed & Quiet",
            "relaxed-quiet",
            "Travel Style",
            "leaf",
            420,
            "Quiet areas, local neighborhoods, hidden gems, wellness and slower experiences.",
            [
                new("quiet-area", 3), new("relaxed-area", 2.75), new("hidden-gem", 2), new("wellness", 2),
                new("local-area", 1.5), new("dest-relaxed", 2.5), new("dest-wellness", 1.75)
            ]),

        new(
            "Luxury Travel",
            "luxury-travel",
            "Travel Style",
            "diamond",
            430,
            "Luxury stays, premium restaurants, boutique hotels and polished trip experiences.",
            [
                new("luxury", 3), new("luxury-stay", 3), new("premium", 2.5), new("fine-dining", 2),
                new("boutique-stay", 1.75), new("dest-luxury-hotels", 2.75), new("dest-fine-dining", 1.75), new("dest-spa-hotels", 1.5)
            ]),

        new(
            "Budget Friendly",
            "budget-friendly-interest",
            "Travel Style",
            "wallet",
            440,
            "Budget stays, casual food, free events and low-cost things to do.",
            [
                new("budget", 3), new("budget-friendly", 2.5), new("free-entry-event", 2.5), new("casual", 1.75),
                new("apartment-stay", 1.5), new("dest-budget-stays", 2.75)
            ]),

        new(
            "Boutique & Unique Stays",
            "boutique-unique-stays",
            "Travel Style",
            "bed",
            450,
            "Boutique stays, apartments, guesthouses and characterful accommodation.",
            [
                new("boutique-stay", 3), new("guesthouse-stay", 2.5), new("apartment-stay", 2.25),
                new("harbor-stay", 1.5), new("architecture", 1), new("dest-boutique-hotels", 2.5), new("dest-mountain-lodges", 1.5)
            ])
    ];

    private static async Task EnsureInterestTopicsAsync(AppDbContext db, IReadOnlyDictionary<string, Tag> tagLookup)
    {
        var now = DateTime.UtcNow;
        var existingTopicsBySlug = await db.InterestTopics
            .Include(topic => topic.TopicTags)
            .ToDictionaryAsync(topic => topic.Slug);

        foreach (var definition in BuildSeedInterestTopics())
        {
            if (!existingTopicsBySlug.TryGetValue(definition.Slug, out var topic))
            {
                topic = new InterestTopic
                {
                    Name = definition.Name,
                    Slug = definition.Slug,
                    Description = definition.Description,
                    IconKey = definition.IconKey,
                    Group = definition.Group,
                    SortOrder = definition.SortOrder,
                    IsActive = definition.IsActive,
                    ShowOnOnboarding = definition.ShowOnOnboarding,
                    CreatedAt = now
                };
                db.InterestTopics.Add(topic);
                await db.SaveChangesAsync();
                existingTopicsBySlug[definition.Slug] = topic;
            }
            else
            {
                topic.Name = definition.Name;
                topic.Description = definition.Description;
                topic.IconKey = definition.IconKey;
                topic.Group = definition.Group;
                topic.SortOrder = definition.SortOrder;
                topic.IsActive = definition.IsActive;
                topic.ShowOnOnboarding = definition.ShowOnOnboarding;
                topic.UpdatedAt = now;
            }

            var wantedLinks = definition.Tags
                .Where(x => tagLookup.ContainsKey(x.TagSlug))
                .Select(x => new { TagId = tagLookup[x.TagSlug].Id, x.Weight })
                .GroupBy(x => x.TagId)
                .ToDictionary(x => x.Key, x => x.Max(y => y.Weight));

            var existingLinks = await db.InterestTopicTags
                .Where(x => x.InterestTopicId == topic.Id)
                .ToListAsync();

            var existingTagIds = existingLinks.Select(x => x.TagId).ToHashSet();
            var linksToRemove = existingLinks.Where(x => !wantedLinks.ContainsKey(x.TagId)).ToList();
            if (linksToRemove.Count > 0)
            {
                db.InterestTopicTags.RemoveRange(linksToRemove);
            }

            foreach (var link in existingLinks.Where(x => wantedLinks.ContainsKey(x.TagId)))
            {
                link.Weight = wantedLinks[link.TagId];
            }

            foreach (var (tagId, weight) in wantedLinks)
            {
                if (existingTagIds.Contains(tagId))
                {
                    continue;
                }

                db.InterestTopicTags.Add(new InterestTopicTag
                {
                    InterestTopicId = topic.Id,
                    TagId = tagId,
                    Weight = weight
                });
            }

            await db.SaveChangesAsync();
        }

        var activeSeedSlugs = BuildSeedInterestTopics()
            .Select(x => x.Slug)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var obsoleteTopic in existingTopicsBySlug.Values.Where(x => !activeSeedSlugs.Contains(x.Slug)))
        {
            obsoleteTopic.IsActive = false;
            obsoleteTopic.ShowOnOnboarding = false;
            obsoleteTopic.UpdatedAt = now;
        }

        await db.SaveChangesAsync();

        await BackfillUserInterestTopicsFromLegacyTagsAsync(db);
        await BackfillUserInterestTopicSignalsFromLegacyTagSignalsAsync(db);
    }

    private static async Task BackfillUserInterestTopicsFromLegacyTagsAsync(AppDbContext db)
    {
        var legacySelections = await db.UserTags
            .AsNoTracking()
            .Select(x => new { x.UserId, x.TagId })
            .ToListAsync();

        if (legacySelections.Count == 0)
        {
            return;
        }

        var tagToTopicIds = await db.InterestTopicTags
            .AsNoTracking()
            .Where(x => x.InterestTopic.IsActive)
            .GroupBy(x => x.TagId)
            .ToDictionaryAsync(x => x.Key, x => x.Select(y => y.InterestTopicId).Distinct().ToList());

        var existing = await db.UserInterestTopics
            .AsNoTracking()
            .Select(x => new { x.UserId, x.InterestTopicId })
            .ToListAsync();

        var existingKeys = existing
            .Select(x => (x.UserId, x.InterestTopicId))
            .ToHashSet();

        var now = DateTime.UtcNow;
        foreach (var selection in legacySelections)
        {
            if (!tagToTopicIds.TryGetValue(selection.TagId, out var topicIds))
            {
                continue;
            }

            foreach (var topicId in topicIds)
            {
                var key = (selection.UserId, topicId);
                if (existingKeys.Contains(key))
                {
                    continue;
                }

                db.UserInterestTopics.Add(new UserInterestTopic
                {
                    UserId = selection.UserId,
                    InterestTopicId = topicId,
                    CreatedAt = now
                });
                existingKeys.Add(key);
            }
        }

        await db.SaveChangesAsync();
    }

    private static async Task BackfillUserInterestTopicSignalsFromLegacyTagSignalsAsync(AppDbContext db)
    {
        var tagSignals = await db.UserInterestSignals
            .AsNoTracking()
            .Where(signal => signal.Source == "inferred" && signal.Score > 0)
            .ToListAsync();

        if (tagSignals.Count == 0)
        {
            return;
        }

        var tagToTopicLinks = await db.InterestTopicTags
            .AsNoTracking()
            .Where(x => x.InterestTopic.IsActive)
            .Select(x => new { x.TagId, x.InterestTopicId, x.Weight })
            .ToListAsync();

        var linksByTagId = tagToTopicLinks
            .GroupBy(x => x.TagId)
            .ToDictionary(x => x.Key, x => x.ToList());

        var existingSignals = await db.UserInterestTopicSignals
            .ToListAsync();

        var byKey = existingSignals
            .ToDictionary(x => (x.UserId, x.InterestTopicId));

        var grouped = new Dictionary<(int UserId, int InterestTopicId), (double Score, DateTime LastInteractionAt, string? LastReason)>();

        foreach (var signal in tagSignals)
        {
            if (!linksByTagId.TryGetValue(signal.TagId, out var links))
            {
                continue;
            }

            foreach (var link in links)
            {
                var key = (signal.UserId, link.InterestTopicId);
                var weightedScore = signal.Score * Math.Max(0.1, link.Weight);

                if (grouped.TryGetValue(key, out var current))
                {
                    grouped[key] = (
                        current.Score + weightedScore,
                        signal.LastInteractionAt > current.LastInteractionAt ? signal.LastInteractionAt : current.LastInteractionAt,
                        signal.LastInteractionAt > current.LastInteractionAt ? signal.LastReason : current.LastReason);
                }
                else
                {
                    grouped[key] = (weightedScore, signal.LastInteractionAt, signal.LastReason);
                }
            }
        }

        foreach (var (key, value) in grouped)
        {
            if (byKey.TryGetValue(key, out var existing))
            {
                if (existing.Score < value.Score)
                {
                    existing.Score = Math.Min(value.Score, 1000.0);
                    existing.LastInteractionAt = value.LastInteractionAt;
                    existing.LastReason = value.LastReason;
                }
            }
            else
            {
                db.UserInterestTopicSignals.Add(new UserInterestTopicSignal
                {
                    UserId = key.UserId,
                    InterestTopicId = key.InterestTopicId,
                    Score = Math.Min(value.Score, 1000.0),
                    Source = "inferred",
                    LastInteractionAt = value.LastInteractionAt,
                    LastReason = value.LastReason
                });
            }
        }

        await db.SaveChangesAsync();
    }

    private static async Task EnsureDestinationDiscoveryTagAssignmentsAsync(
        AppDbContext db,
        IReadOnlyDictionary<string, Tag> tagLookup)
    {
        async Task SyncByNameAsync(string destinationName, params string[] tagSlugs)
        {
            var destination = await db.Destinations.FirstOrDefaultAsync(x => x.Name == destinationName);
            if (destination is null)
            {
                return;
            }

            await SyncDestinationDiscoveryTagsAsync(db, tagLookup, destination, tagSlugs);
        }

        await SyncByNameAsync("Dubrovnik",
            "dest-coastal",
            "dest-old-town",
            "dest-island-nearby",
            "dest-historic",
            "dest-romantic",
            "dest-culture",
            "dest-sightseeing",
            "dest-swimming",
            "dest-boat-trips",
            "dest-festivals",
            "dest-seafood",
            "dest-fine-dining",
            "dest-luxury-hotels",
            "dest-boutique-hotels");

        await SyncByNameAsync("Beograd",
            "dest-riverfront",
            "dest-urban",
            "dest-culture",
            "dest-party",
            "dest-sightseeing",
            "dest-museums",
            "dest-nightlife",
            "dest-festivals",
            "dest-river-walks",
            "dest-local-food",
            "dest-grill",
            "dest-cafe-culture",
            "dest-boutique-hotels",
            "dest-budget-stays");

        await SyncByNameAsync("Novi Sad",
            "dest-riverfront",
            "dest-relaxed",
            "dest-romantic",
            "dest-culture",
            "dest-festivals",
            "dest-river-walks",
            "dest-sightseeing",
            "dest-local-food",
            "dest-cafe-culture",
            "dest-boutique-hotels",
            "dest-budget-stays");

        await SyncByNameAsync("Zlatibor",
            "dest-mountain-views",
            "dest-lake",
            "dest-relaxed",
            "dest-family-friendly",
            "dest-adventure",
            "dest-hiking",
            "dest-wellness",
            "dest-local-food",
            "dest-mountain-food",
            "dest-family-stays",
            "dest-spa-hotels",
            "dest-mountain-lodges");

        await SyncByNameAsync("Kopaonik",
            "dest-mountain-views",
            "dest-adventure",
            "dest-skiing",
            "dest-hiking",
            "dest-wellness",
            "dest-mountain-food",
            "dest-luxury-hotels",
            "dest-spa-hotels",
            "dest-mountain-lodges",
            "dest-family-stays");

        await SyncByNameAsync("Tara",
            "dest-forest",
            "dest-lake",
            "dest-mountain-views",
            "dest-relaxed",
            "dest-family-friendly",
            "dest-adventure",
            "dest-hiking",
            "dest-boat-trips",
            "dest-local-food",
            "dest-family-stays",
            "dest-mountain-lodges");

        await SyncByNameAsync("Kragujevac",
            "dest-culture",
            "dest-museums",
            "dest-sightseeing",
            "dest-urban",
            "dest-relaxed",
            "dest-local-food",
            "dest-grill",
            "dest-cafe-culture",
            "dest-budget-stays",
            "dest-river-walks");
    }

    private static async Task SyncDestinationDiscoveryTagsAsync(
        AppDbContext db,
        IReadOnlyDictionary<string, Tag> tagLookup,
        Destination destination,
        params string[] tagSlugs)
    {
        var managedTagIds = tagLookup.Values
            .Where(tag => tag.TagType is not null && ManagedDestinationDiscoveryTagTypeSlugs.Contains(tag.TagType.Slug))
            .Select(tag => tag.Id)
            .ToHashSet();

        var wantedTagIds = tagSlugs
            .Where(tagLookup.ContainsKey)
            .Select(tagSlug => tagLookup[tagSlug].Id)
            .ToHashSet();

        var existingManagedTags = await db.DestinationTags
            .Where(x => x.DestinationId == destination.Id && managedTagIds.Contains(x.TagId))
            .ToListAsync();

        var tagsToRemove = existingManagedTags
            .Where(x => !wantedTagIds.Contains(x.TagId))
            .ToList();

        if (tagsToRemove.Count > 0)
        {
            db.DestinationTags.RemoveRange(tagsToRemove);
        }

        var existingTagIds = existingManagedTags
            .Select(x => x.TagId)
            .ToHashSet();

        foreach (var tagId in wantedTagIds)
        {
            if (existingTagIds.Contains(tagId))
            {
                continue;
            }

            db.DestinationTags.Add(new DestinationTag
            {
                DestinationId = destination.Id,
                TagId = tagId
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task EnsureSerbiaDemoDataAsync(AppDbContext db, IReadOnlyDictionary<string, Tag> tagLookup)
    {
        var now = DateTime.UtcNow;

        async Task<DestinationType> EnsureDestinationTypeAsync(string name, string description)
        {
            var destinationType = await db.DestinationTypes.FirstOrDefaultAsync(x => x.Name == name);
            if (destinationType is not null)
            {
                return destinationType;
            }

            destinationType = new DestinationType
            {
                Name = name,
                Description = description,
                CreatedAt = now
            };

            db.DestinationTypes.Add(destinationType);
            await db.SaveChangesAsync();
            return destinationType;
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

        async Task<PlaceType> EnsurePlaceTypeAsync(string name, string description, bool isOwnable = false)
        {
            var placeType = await db.PlaceTypes.FirstOrDefaultAsync(x => x.Name == name);
            if (placeType is not null)
            {
                if (placeType.IsOwnable != isOwnable)
                {
                    placeType.IsOwnable = isOwnable;
                    placeType.UpdatedAt = now;
                    await db.SaveChangesAsync();
                }

                return placeType;
            }

            placeType = new PlaceType
            {
                Name = name,
                Description = description,
                IsOwnable = isOwnable,
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

        async Task<Destination> EnsureDestinationAsync(
            string name,
            string description,
            DestinationType destinationType,
            Region region,
            double latitude,
            double longitude)
        {
            var destination = await db.Destinations.FirstOrDefaultAsync(x => x.Name == name);
            if (destination is not null)
            {
                var changed = false;
                if (destination.RegionId != region.Id)
                {
                    destination.RegionId = region.Id;
                    changed = true;
                }

                if (Math.Abs(destination.Latitude - latitude) > 1e-9 || Math.Abs(destination.Longitude - longitude) > 1e-9)
                {
                    destination.Latitude = latitude;
                    destination.Longitude = longitude;
                    changed = true;
                }
                if (changed) await db.SaveChangesAsync();
                return destination;
            }

            destination = new Destination
            {
                Name = name,
                Description = description,
                DestinationTypeId = destinationType.Id,
                RegionId = region.Id,
                Latitude = latitude,
                Longitude = longitude,
                CreatedAt = now
            };

            db.Destinations.Add(destination);
            await db.SaveChangesAsync();
            return destination;
        }

        async Task<Location> EnsureLocationAsync(
            Destination destination,
            string name,
            LocationType locationType,
            double latitude,
            double longitude,
            string? summary = null,
            string? description = null)
        {
            var location = await db.Locations.FirstOrDefaultAsync(x => x.DestinationId == destination.Id && x.Name == name);
            if (location is not null)
            {
                var changed = false;
                if (Math.Abs(location.Latitude - latitude) > 1e-9 || Math.Abs(location.Longitude - longitude) > 1e-9)
                {
                    location.Latitude = latitude;
                    location.Longitude = longitude;
                    changed = true;
                }
                if (summary is not null && string.IsNullOrWhiteSpace(location.Summary))
                {
                    location.Summary = summary;
                    changed = true;
                }
                if (description is not null && string.IsNullOrWhiteSpace(location.Description))
                {
                    location.Description = description;
                    changed = true;
                }
                if (changed) await db.SaveChangesAsync();
                return location;
            }

            location = new Location
            {
                Name = name,
                Summary = summary,
                Description = description ?? string.Empty,
                DestinationId = destination.Id,
                LocationTypeId = locationType.Id,
                Latitude = latitude,
                Longitude = longitude,
                CreatedAt = now
            };

            db.Locations.Add(location);
            await db.SaveChangesAsync();
            return location;
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
            string? openingHours,
            double rating,
            int reviewCount,
            string? contactEmail = null,
            string? number = null)
        {
            var place = await db.Places.FirstOrDefaultAsync(x => x.Name == name);
            if (place is not null)
            {
                var changed = false;

                if (place.PlaceTypeId != placeType.Id)
                {
                    place.PlaceTypeId = placeType.Id;
                    changed = true;
                }

                if (place.LocationId != location.Id)
                {
                    place.LocationId = location.Id;
                    changed = true;
                }

                if (Math.Abs(place.Latitude - latitude) > 1e-9 || Math.Abs(place.Longitude - longitude) > 1e-9)
                {
                    place.Latitude = latitude;
                    place.Longitude = longitude;
                    changed = true;
                }

                if (changed)
                {
                    await db.SaveChangesAsync();
                }

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
            Destination destination,
            Location location,
            string description,
            DateTime startDate,
            DateTime endDate,
            string ticketPrice,
            string duration,
            string hostedBy,
            Place? place = null,
            string? externalTicketLink = null,
            string? summary = null,
            string? venueDescription = null,
            string? arrivalTips = null,
            string? accessibility = null)
        {
            var eventEntity = await db.Events.FirstOrDefaultAsync(x => x.Name == name);
            if (eventEntity is not null)
            {
                if (externalTicketLink != null && eventEntity.ExternalTicketLink != externalTicketLink)
                {
                    eventEntity.ExternalTicketLink = externalTicketLink;
                    await db.SaveChangesAsync();
                }
                return eventEntity;
            }

            eventEntity = new Event
            {
                Name = name,
                EventTypeId = eventType.Id,
                DestinationId = destination.Id,
                LocationId = location.Id,
                PlaceId = place?.Id,
                Summary = summary,
                Description = description,
                StartDate = startDate,
                EndDate = endDate,
                TicketPrice = ticketPrice,
                Duration = duration,
                HostedBy = hostedBy,
                ExternalTicketLink = externalTicketLink,
                VenueDescription = venueDescription ?? (place is null ? location.Name : place.Name),
                ArrivalTips = arrivalTips ?? "Use public transport where possible and check local schedules before visiting.",
                Accessibility = accessibility ?? "Check official venue information before attending.",
                CreatedAt = now
            };

            db.Events.Add(eventEntity);
            await db.SaveChangesAsync();
            return eventEntity;
        }

        async Task AddDestinationTagsIfMissingAsync(Destination destination, params string[] tagSlugs)
        {
            await SyncDestinationDiscoveryTagsAsync(db, tagLookup, destination, tagSlugs);
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

        async Task RemoveBookingPartnerIfExistsAsync(Place place, BookingPartner partner)
        {
            var entry = await db.PlaceBookingPartners.FirstOrDefaultAsync(x =>
                x.PlaceId == place.Id &&
                x.BookingPartnerId == partner.Id);

            if (entry is null) return;

            db.PlaceBookingPartners.Remove(entry);
            await db.SaveChangesAsync();
        }

        async Task UpdatePlaceHoursAsync(Place place, string? openingHours, string? operatingHoursJson)
        {
            var changed = false;
            if (place.OpeningHours != openingHours) { place.OpeningHours = openingHours; changed = true; }
            var shouldBeEnabled = operatingHoursJson != null;
            if (place.OperatingHoursJson != operatingHoursJson) { place.OperatingHoursJson = operatingHoursJson; changed = true; }
            if (place.OperatingHoursEnabled != shouldBeEnabled) { place.OperatingHoursEnabled = shouldBeEnabled; changed = true; }
            if (changed) await db.SaveChangesAsync();
        }

        async Task ForceSetDestinationImageAsync(Destination destination, string url)
        {
            var existing = await db.EntityImages.Where(x =>
                x.TargetType == EntityImageTargetType.Destination &&
                x.DestinationId == destination.Id).ToListAsync();

            if (existing.Count == 1 && existing[0].Url == url) return;

            db.EntityImages.RemoveRange(existing);

            db.EntityImages.Add(new EntityImage
            {
                TargetType = EntityImageTargetType.Destination,
                DestinationId = destination.Id,
                Url = url,
                Caption = destination.Name,
                AltText = destination.Name,
                IsMain = true,
                ImageRole = EntityImageRole.Cover,
                SortOrder = 0,
                CreatedAt = now
            });

            await db.SaveChangesAsync();
        }

        async Task ForceSetPlaceImageAsync(Place place, string url)
        {
            var existing = await db.EntityImages.Where(x =>
                x.TargetType == EntityImageTargetType.Place &&
                x.PlaceId == place.Id).ToListAsync();

            if (existing.Count == 1 && existing[0].Url == url) return;

            db.EntityImages.RemoveRange(existing);

            db.EntityImages.Add(new EntityImage
            {
                TargetType = EntityImageTargetType.Place,
                PlaceId = place.Id,
                Url = url,
                Caption = place.Name,
                AltText = place.Name,
                IsMain = true,
                ImageRole = EntityImageRole.Cover,
                SortOrder = 0,
                CreatedAt = now
            });

            await db.SaveChangesAsync();
        }

        async Task RemoveEventIfExistsAsync(string name)
        {
            var evt = await db.Events.FirstOrDefaultAsync(x => x.Name == name);
            if (evt is null) return;

            var tags = await db.EventTags.Where(x => x.EventId == evt.Id).ToListAsync();
            db.EventTags.RemoveRange(tags);

            var images = await db.EntityImages.Where(x => x.EventId == evt.Id).ToListAsync();
            db.EntityImages.RemoveRange(images);

            db.Events.Remove(evt);
            await db.SaveChangesAsync();
        }

        async Task RemovePlaceIfExistsAsync(string name)
        {
            var place = await db.Places.FirstOrDefaultAsync(x => x.Name == name);
            if (place is null) return;

            var stay = await db.Stays.FirstOrDefaultAsync(x => x.PlaceId == place.Id);
            if (stay is not null)
            {
                var amenities = await db.StayStayAmenities.Where(x => x.StayPlaceId == place.Id).ToListAsync();
                db.StayStayAmenities.RemoveRange(amenities);
                db.Stays.Remove(stay);
            }

            var tags = await db.PlaceTags.Where(x => x.PlaceId == place.Id).ToListAsync();
            db.PlaceTags.RemoveRange(tags);

            var images = await db.EntityImages.Where(x => x.PlaceId == place.Id).ToListAsync();
            db.EntityImages.RemoveRange(images);

            var reviews = await db.PlaceReviews.Where(x => x.PlaceId == place.Id).ToListAsync();
            db.PlaceReviews.RemoveRange(reviews);

            var bookingPartners = await db.PlaceBookingPartners.Where(x => x.PlaceId == place.Id).ToListAsync();
            db.PlaceBookingPartners.RemoveRange(bookingPartners);

            db.Places.Remove(place);
            await db.SaveChangesAsync();
        }

        static string BuildHoursJson(params (string days, string hours)[] schedule)
        {
            var entries = schedule.Select(s => $"{{\"day\":\"{s.days}\",\"hours\":\"{s.hours}\"}}");
            return "[" + string.Join(",", entries) + "]";
        }

        static string BuildDailyHoursJson(string hours)
            => BuildHoursJson(("Mon", hours), ("Tue", hours), ("Wed", hours), ("Thu", hours), ("Fri", hours), ("Sat", hours), ("Sun", hours));

        async Task AddEntityImagesIfMissingAsync(EntityImageTargetType targetType, string title, int? destinationId = null, int? placeId = null, int? eventId = null, params string[] urls)
        {
            var exists = await db.EntityImages.AnyAsync(x =>
                x.TargetType == targetType &&
                ((destinationId != null && x.DestinationId == destinationId) ||
                 (placeId != null && x.PlaceId == placeId) ||
                 (eventId != null && x.EventId == eventId)));

            if (exists)
            {
                return;
            }

            for (var i = 0; i < urls.Length; i++)
            {
                db.EntityImages.Add(new EntityImage
                {
                    TargetType = targetType,
                    DestinationId = destinationId,
                    PlaceId = placeId,
                    EventId = eventId,
                    Url = urls[i],
                    Caption = title,
                    AltText = title,
                    IsMain = i == 0,
                    ImageRole = i == 0 ? EntityImageRole.Cover : i == 1 ? EntityImageRole.Card : EntityImageRole.Gallery,
                    SortOrder = i,
                    CreatedAt = now
                });
            }

            await db.SaveChangesAsync();
        }

        var serbia = await EnsureSerbiaTerritoryAsync(db, now);
        await EnsureSerbiaTerritoryManagerAsync(db, serbia);

        var serbiaRegions = await EnsureSerbiaAdm1RegionsFromCandidatesAsync(db, serbia, now);
        var fallbackRegions = await EnsureSerbiaFallbackAdm1RegionsAsync(db, serbia, now);

        Region ResolveSerbiaRegion(double latitude, double longitude, string fallbackKey) =>
            FindSeedRegionByPoint(serbiaRegions, latitude, longitude)?.Region
            ?? fallbackRegions[fallbackKey];

        var beogradRegion = ResolveSerbiaRegion(44.8125, 20.4612, "belgrade");
        var noviSadRegion = ResolveSerbiaRegion(45.2671, 19.8335, "south-backa");
        var zlatiborRegion = ResolveSerbiaRegion(43.7297, 19.7003, "zlatibor");
        var kopaonikRegion = ResolveSerbiaRegion(43.2856, 20.8095, "raska");
        var taraRegion = ResolveSerbiaRegion(43.9450, 19.4340, "zlatibor");
        var kragujevacRegion = ResolveSerbiaRegion(44.010590360886276, 20.915278268657637, "sumadija");

        var cityType = await EnsureDestinationTypeAsync("City", "Urban destination");
        var mountainType = await EnsureDestinationTypeAsync("Mountain", "Mountain and outdoor destination");
        var nationalParkType = await EnsureDestinationTypeAsync("National Park", "Protected nature destination");

        var neighborhoodType = await EnsureLocationTypeAsync("Neighborhood", "City neighborhood");
        var districtType = await EnsureLocationTypeAsync("District", "City district");
        var parkType = await EnsureLocationTypeAsync("Park Area", "Park or green urban area");
        var waterfrontType = await EnsureLocationTypeAsync("Waterfront Area", "River, lake or waterfront area");
        var mountainAreaType = await EnsureLocationTypeAsync("Mountain Area", "Mountain or outdoor area");

        var ptRestaurant = await EnsurePlaceTypeAsync("Restaurant", "Dining establishments", isOwnable: true);
        var ptCafe = await EnsurePlaceTypeAsync("Cafe", "Cafes and coffee shops", isOwnable: true);
        var ptBar = await EnsurePlaceTypeAsync("Bar", "Bars, wine bars and nightlife venues", isOwnable: true);
        var ptLandmark = await EnsurePlaceTypeAsync("Landmark", "Landmarks, monuments and historical places");
        var ptNature = await EnsurePlaceTypeAsync("Nature", "Nature spots and island escapes");
        var ptStay = await EnsurePlaceTypeAsync("Stay", "Hotels, apartments, villas and other accommodation", isOwnable: true);
        var ptMuseum = await EnsurePlaceTypeAsync("Museum", "Museums and cultural institutions");
        var ptGallery = await EnsurePlaceTypeAsync("Gallery", "Art galleries and exhibition spaces");
        var ptBeach = await EnsurePlaceTypeAsync("Beach", "Beaches and swimming areas");

        // New place types may not have existed when the taxonomy was first ensured,
        // so run the taxonomy sync once more to attach allowed TagType <-> PlaceType links.
        await EnsureSmartTagTaxonomyAsync(db);

        var hotelStayType = await EnsureStayTypeAsync("Hotel", "hotel", "Hotel accommodation");
        var apartmentStayType = await EnsureStayTypeAsync("Apartment", "apartment", "Private apartment accommodation");
        var guesthouseStayType = await EnsureStayTypeAsync("Guesthouse", "guesthouse", "Guesthouse accommodation");
        var wifi = await EnsureStayAmenityAsync("Free WiFi", "free-wifi", "wifi", "Wireless internet access");
        var breakfast = await EnsureStayAmenityAsync("Breakfast Included", "breakfast-included", "breakfast", "Breakfast included in the stay");
        var parking = await EnsureStayAmenityAsync("Parking", "parking", "parking", "Parking available");
        var spa = await EnsureStayAmenityAsync("Spa", "spa", "spa", "Spa and wellness facilities");
        var fitnessCenter = await EnsureStayAmenityAsync("Fitness Center", "fitness-center", "gym", "Fitness facilities available");
        var restaurantAmenity = await EnsureStayAmenityAsync("Restaurant", "restaurant", "restaurant", "On-site dining available");
        var airConditioning = await EnsureStayAmenityAsync("Air Conditioning", "air-conditioning", "ac", "Air-conditioned rooms");

        var festivalType = await EnsureEventTypeAsync("Festival", "Music and cultural festivals");
        var outdoorType = await EnsureEventTypeAsync("Outdoor", "Outdoor activities");
        var marketType = await EnsureEventTypeAsync("Market", "Food and craft markets");

        var beograd = await EnsureDestinationAsync(
            "Beograd",
            "Serbia's capital with riverfront walks, historic fortresses, nightlife, museums and layered urban neighborhoods.",
            cityType,
            beogradRegion,
            44.8125,
            20.4612);
        await AddDestinationTagsIfMissingAsync(beograd,
            "dest-riverfront",
            "dest-urban",
            "dest-culture",
            "dest-party",
            "dest-sightseeing",
            "dest-museums",
            "dest-nightlife",
            "dest-festivals",
            "dest-river-walks",
            "dest-local-food",
            "dest-grill",
            "dest-cafe-culture",
            "dest-boutique-hotels",
            "dest-budget-stays");
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Destination, "Beograd", destinationId: beograd.Id, urls: new[] { Unsplash("1546776310-eef45dd6d63c") });

        var stariGrad = await EnsureLocationAsync(beograd, "Stari Grad", neighborhoodType, 44.8176, 20.4571,
            summary: "Belgrade's historic core on the confluence hill, home to Kalemegdan Fortress, pedestrian Knez Mihailova, and riverside cafes.",
            description: "Stari Grad (Old Town) is the historic municipality at the heart of Belgrade, stretching from Kalemegdan Fortress down to the riverbanks of the Sava and Danube. " +
                "The main pedestrian street, Knez Mihailova, runs through it lined with 19th-century buildings, shops, galleries, and cafes. " +
                "Kalemegdan Park sits above both rivers with fortress walls, outdoor chess tables, and sweeping views. " +
                "The neighbourhood is home to most of Belgrade's major cultural institutions, museums, and the Serbian national theatre.");
        var dorcol = await EnsureLocationAsync(beograd, "Dorcol", neighborhoodType, 44.8230, 20.4599,
            summary: "A relaxed bohemian quarter along the Danube with independent cafes, creative studios, and the Skadarlija street.",
            description: "Dorćol is a historic riverside neighbourhood northeast of Stari Grad, bordered by the Danube. " +
                "It retains a quieter, more residential atmosphere than the city centre, with tree-lined streets, " +
                "independent coffee shops, craft beer bars, and the bohemian Skadarlija cobblestone lane — " +
                "lined with traditional Serbian restaurants (kafanas) and folk music in the evenings.");
        var savamala = await EnsureLocationAsync(beograd, "Savamala", districtType, 44.8113, 20.4504,
            summary: "Belgrade's creative and nightlife district along the Sava, transformed from a warehouse zone into galleries, clubs and restaurants.",
            description: "Savamala is a formerly industrial riverside quarter that has become Belgrade's leading arts and nightlife precinct. " +
                "Old warehouses and riverside buildings now house galleries, design studios, clubs, and bars. " +
                "The area is most active from Thursday to Sunday nights and is the starting point for Belgrade's famous floating club (splavovi) scene on the Sava. " +
                "It is also home to the annual Mikser festival and several independent creative venues.");
        var ada = await EnsureLocationAsync(beograd, "Ada Ciganlija", waterfrontType, 44.7850, 20.3958,
            summary: "Belgrade's river island beach and recreation park on the Sava, popular for swimming, cycling, sports and summer nightlife.",
            description: "Ada Ciganlija is a river island on the Sava that has been converted into a peninsula recreation area. " +
                "During summer it functions as Belgrade's main city beach, with a 4 km lake shore, water sports, beach bars, and volleyball courts. " +
                "The surrounding parkland has cycling and running paths that are busy year-round with locals. " +
                "In season, Ada becomes one of the most visited day-trip destinations in the city.");
        await AddLocationTagsIfMissingAsync(stariGrad, "old-town-core", "walkable-area", "local-area");
        await AddLocationTagsIfMissingAsync(dorcol, "local-area", "walkable-area", "casual");
        await AddLocationTagsIfMissingAsync(savamala, "waterfront-area", "local-area", "art");
        await AddLocationTagsIfMissingAsync(ada, "waterfront-area", "family-friendly", "nature");

        var kalemegdan = await EnsurePlaceAsync("Kalemegdan Fortress", "Historic fortress and park above the rivers", "Kalemegdan, Beograd", ptLandmark, stariGrad, "A large fortress park where the Sava and Danube meet, with viewpoints, museums and long walking paths.", 44.8231, 20.4502, "Open 24h", 4.8, 1240);
        var skadarlija = await EnsurePlaceAsync("Skadarlija Restaurant Street", "Traditional restaurants and old-town atmosphere", "Skadarlija, Beograd", ptRestaurant, stariGrad, "A cobbled dining street known for traditional restaurants, music and evening walks.", 44.8175, 20.4643, "11:00 - 00:00", 4.6, 720);
        var kafeterija = await EnsurePlaceAsync("Kafeterija Dorcol", "Specialty coffee in a local neighborhood", "Dorcol, Beograd", ptCafe, dorcol, "A casual coffee stop for specialty coffee, remote work breaks and neighborhood walks.", 44.8217, 20.4620, "08:00 - 22:00", 4.5, 280);
        var adaLake = await EnsurePlaceAsync("Ada Ciganlija Lake", "Urban lake, beach and recreation area", "Ada Ciganlija, Beograd", ptBeach, ada, "A large urban recreation area with beaches, cycling paths, cafes and summer swimming spots.", 44.7854, 20.3971, "Open 24h", 4.7, 950);
        var hotelMoskva = await EnsurePlaceAsync("Hotel Moskva", "Historic city hotel near Terazije", "Terazije 20, Beograd", ptStay, stariGrad, "A historic hotel and cafe in the city center, useful for visitors who want a classic central stay.", 44.8134, 20.4615, "Open 24h", 4.6, 610);
        var hotelMoskvaStay = await EnsureStayAsync(hotelMoskva, hotelStayType, 135m, "14:00", "11:00");
        await EnsureRestaurantAsync(skadarlija);
        await EnsureRestaurantAsync(kafeterija);
        await AddStayAmenitiesIfMissingAsync(hotelMoskvaStay, wifi, breakfast, airConditioning);
        await AddPlaceTagsIfMissingAsync(kalemegdan, "history", "culture", "fortress", "panoramic-view", "walking-route");
        await AddPlaceTagsIfMissingAsync(skadarlija, "traditional", "local-favorite", "casual", "dining-style", "walking-route");
        await AddPlaceTagsIfMissingAsync(kafeterija, "specialty-coffee", "casual", "budget", "local-favorite");
        await AddPlaceTagsIfMissingAsync(adaLake, "waterfront", "swimming", "family-friendly", "nature", "walking-route");
        await AddPlaceTagsIfMissingAsync(hotelMoskva, "boutique-stay", "old-town-nearby", "premium", "history");
        await ForceSetPlaceImageAsync(kalemegdan, SeedWebp("kalemegdan"));
        await ForceSetPlaceImageAsync(adaLake, SeedWebp("ada-ciganlija"));

        var noviSad = await EnsureDestinationAsync("Novi Sad", "Danube-side city known for Petrovaradin Fortress, relaxed streets, cafes and festival culture.", cityType, noviSadRegion, 45.2671, 19.8335);
        await AddDestinationTagsIfMissingAsync(noviSad,
            "dest-riverfront",
            "dest-relaxed",
            "dest-romantic",
            "dest-culture",
            "dest-festivals",
            "dest-river-walks",
            "dest-sightseeing",
            "dest-local-food",
            "dest-cafe-culture",
            "dest-boutique-hotels",
            "dest-budget-stays");
        var nsCenter = await EnsureLocationAsync(noviSad, "Novi Sad Center", neighborhoodType, 45.2551, 19.8452,
            summary: "The walkable city centre of Novi Sad with Liberty Square, the pedestrian zone, and the main cultural venues.",
            description: "Novi Sad Center encompasses the main pedestrian area around Liberty Square (Trg slobode), " +
                "the city's principal gathering point anchored by the Name of Mary Church and the town hall. " +
                "The surrounding streets are lined with 19th-century Austro-Hungarian architecture, sidewalk cafes, boutique shops, and galleries. " +
                "The centre is compact and walkable, and leads down to the Danube quay — a popular evening promenade.");
        var petrovaradin = await EnsureLocationAsync(noviSad, "Petrovaradin", districtType, 45.2528, 19.8625,
            summary: "The historic fortress neighbourhood across the river, with underground tunnels, artist studios, and the EXIT festival grounds.",
            description: "Petrovaradin is the district directly across the Danube from central Novi Sad, dominated by the 18th-century Petrovaradin Fortress — " +
                "one of the largest and best-preserved Baroque fortifications in Europe. " +
                "The fortress complex includes underground military tunnels open for tours, a clock tower with reversed clock hands, " +
                "a city history museum, and artist ateliers within the walls. " +
                "Each July it hosts the EXIT Music Festival, one of Europe's largest summer music events.");
        await AddLocationTagsIfMissingAsync(nsCenter, "walkable-area", "local-area", "casual");
        await AddLocationTagsIfMissingAsync(petrovaradin, "panoramic-view", "history", "fortress");
        var fortress = await EnsurePlaceAsync("Petrovaradin Fortress", "Fortress above the Danube", "Petrovaradin, Novi Sad", ptLandmark, petrovaradin, "Historic fortress with river views, galleries, tunnels and festival grounds.", 45.2528, 19.8625, "Open 24h", 4.8, 840);
        var strand = await EnsurePlaceAsync("Strand Beach", "Urban beach on the Danube", "Strand, Novi Sad", ptBeach, nsCenter, "A popular city beach and summer recreation area on the Danube.", 45.2399, 19.8379, "08:00 - 23:00", 4.5, 320);
        var fishZelenish = await EnsurePlaceAsync("Fish & Zelenish", "Modern local restaurant", "Novi Sad Center", ptRestaurant, nsCenter, "A central restaurant with local produce, fish, vegetables and relaxed service.", 45.2561, 19.8455, "12:00 - 23:00", 4.7, 230);
        await EnsureRestaurantAsync(fishZelenish);
        await AddPlaceTagsIfMissingAsync(fortress, "history", "culture", "fortress", "panoramic-view", "walking-route");
        await AddPlaceTagsIfMissingAsync(strand, "waterfront", "swimming", "family-friendly");
        await AddPlaceTagsIfMissingAsync(fishZelenish, "local-favorite", "casual", "traditional", "vegetarian-friendly", "local-food", "food-market");

        var zlatibor = await EnsureDestinationAsync("Zlatibor", "Mountain destination with viewpoints, lakes, pine landscapes, family stays and outdoor activities.", mountainType, zlatiborRegion, 43.7297, 19.7003);
        await AddDestinationTagsIfMissingAsync(zlatibor,
            "dest-mountain-views",
            "dest-lake",
            "dest-relaxed",
            "dest-family-friendly",
            "dest-adventure",
            "dest-hiking",
            "dest-wellness",
            "dest-local-food",
            "dest-mountain-food",
            "dest-family-stays",
            "dest-spa-hotels",
            "dest-mountain-lodges");
        var zlatiborCenter = await EnsureLocationAsync(zlatibor, "Zlatibor Center", districtType, 43.7297, 19.7003,
            summary: "The central resort plateau of Zlatibor with hotels, the lake promenade, the Gold Gondola departure point, and family amenities.",
            description: "Zlatibor Center is the main settlement on the Zlatibor plateau, sitting at around 1,000 metres elevation. " +
                "It is centred on the artificial Zlatibor Lake with a lakeside promenade popular with visitors year-round. " +
                "The area offers the highest concentration of hotels, restaurants, and tourist facilities on the plateau, " +
                "as well as the base station of the Gold Gondola cable car which ascends toward Tornik Peak.");
        var tornik = await EnsureLocationAsync(zlatibor, "Tornik", mountainAreaType, 43.6862, 19.6545,
            summary: "Zlatibor's highest peak area with ski slopes, a gondola station, and open mountain terrain for hiking and views.",
            description: "Tornik (1,496 m) is the highest point of the Zlatibor plateau and the location of the area's main ski infrastructure. " +
                "In winter it operates ski runs of varying difficulty connected by lifts. " +
                "In summer, the mountain is used for hiking, paragliding, and scenic walks with open views over the Dinaric plateau. " +
                "The Gold Gondola connects Zlatibor Center to the upper cable car station near Tornik summit in about 20 minutes.");
        await AddLocationTagsIfMissingAsync(zlatiborCenter, "family-friendly", "relaxed-area", "local-area");
        await AddLocationTagsIfMissingAsync(tornik, "panoramic-view", "hiking", "nature");
        var goldGondola = await EnsurePlaceAsync("Gold Gondola Zlatibor", "Long panoramic gondola ride", "Zlatibor Center", ptLandmark, zlatiborCenter, "Panoramic gondola route toward Tornik with mountain and lake views.", 43.7304, 19.6971, "09:00 - 17:00", 4.7, 500);
        var tornikViewpoint = await EnsurePlaceAsync("Tornik Viewpoint", "Mountain viewpoint and outdoor area", "Tornik, Zlatibor", ptNature, tornik, "High viewpoint area for mountain scenery, hiking and seasonal outdoor activities.", 43.6862, 19.6545, "Open 24h", 4.7, 260);
        await RemovePlaceIfExistsAsync("Zlatibor Mountain Lodge");
        await AddPlaceTagsIfMissingAsync(goldGondola, "cable-car", "panoramic-view", "family-friendly", "nature");
        await AddPlaceTagsIfMissingAsync(tornikViewpoint, "hiking", "panoramic-view", "nature", "walking-route");

        var kopaonik = await EnsureDestinationAsync("Kopaonik", "Mountain and ski destination with trails, viewpoints, hotels and winter activities.", mountainType, kopaonikRegion, 43.2856, 20.8095);
        await AddDestinationTagsIfMissingAsync(kopaonik,
            "dest-mountain-views",
            "dest-adventure",
            "dest-skiing",
            "dest-hiking",
            "dest-wellness",
            "dest-mountain-food",
            "dest-luxury-hotels",
            "dest-spa-hotels",
            "dest-mountain-lodges",
            "dest-family-stays");
        var skiCenter = await EnsureLocationAsync(kopaonik, "Kopaonik Ski Center", mountainAreaType, 43.2856, 20.8095,
            summary: "The main resort base of Kopaonik National Park with ski runs, mountain hotels, and year-round outdoor activities.",
            description: "Kopaonik Ski Center is the central hub of the Kopaonik resort, located within the national park at around 1,770 metres. " +
                "It is the largest ski resort in Serbia, with over 55 km of prepared ski runs connected by modern lifts. " +
                "In summer, the area is used for mountain biking, hiking, and nature tourism, with the national park's endemic flora and fauna. " +
                "The resort village has a pedestrian zone, hotels, restaurants, and ski rental facilities.");
        var pancic = await EnsureLocationAsync(kopaonik, "Pancic Peak", mountainAreaType, 43.2670, 20.8229,
            summary: "The highest point of Kopaonik at 2,017 m, named after botanist Josif Pančić, with far-reaching views on clear days.",
            description: "Pančić Peak (Pančićev vrh) is the highest point of the Kopaonik massif at 2,017 metres. " +
                "It is accessible on foot from the ski centre and offers panoramic views extending on clear days to distant ranges. " +
                "The peak is named after Josif Pančić, the 19th-century Serbian botanist who discovered the Pančić's spruce (Picea omorika) in this area. " +
                "A monument to Pančić stands near the summit, and the peak is a common hiking goal from the ski centre base.");
        await AddLocationTagsIfMissingAsync(skiCenter, "family-friendly", "local-area", "nature");
        await AddLocationTagsIfMissingAsync(pancic, "panoramic-view", "hiking", "nature");
        var skiCenterPlace = await EnsurePlaceAsync("Kopaonik Ski Center", "Main ski and outdoor activity area", "Kopaonik", ptNature, skiCenter, "Main mountain activity area for skiing, walking, viewpoints and seasonal outdoor activities.", 43.2856, 20.8095, "08:00 - 17:00", 4.7, 640);
        var hotelGrand = await EnsurePlaceAsync("Hotel Grand Kopaonik", "Central mountain hotel", "Kopaonik Center", ptStay, skiCenter, "Central mountain hotel near the ski area and resort services.", 43.2858, 20.8101, "Open 24h", 4.6, 420);
        var grandStay = await EnsureStayAsync(hotelGrand, hotelStayType, 180m, "15:00", "11:00");
        await AddStayAmenitiesIfMissingAsync(grandStay, wifi, breakfast, parking, spa, airConditioning);
        await AddPlaceTagsIfMissingAsync(skiCenterPlace, "hiking", "panoramic-view", "family-friendly");
        await AddPlaceTagsIfMissingAsync(hotelGrand, "luxury-stay", "premium", "wellness");

        var tara = await EnsureDestinationAsync("Tara", "National park destination with viewpoints, lakes, forest roads and quiet nature stays.", nationalParkType, taraRegion, 43.9450, 19.4340);
        await AddDestinationTagsIfMissingAsync(tara,
            "dest-forest",
            "dest-lake",
            "dest-mountain-views",
            "dest-relaxed",
            "dest-family-friendly",
            "dest-adventure",
            "dest-hiking",
            "dest-boat-trips",
            "dest-local-food",
            "dest-family-stays",
            "dest-mountain-lodges");
        var mitrovac = await EnsureLocationAsync(tara, "Mitrovac", mountainAreaType, 43.9312, 19.4306,
            summary: "The main settlement of Tara National Park, a base for hiking, viewpoints and forest walks.",
            description: "Mitrovac is the central hub of Tara National Park, situated at around 1,200 metres in a dense beech and fir forest. " +
                "It serves as the main starting point for hikes toward Banjska Stena viewpoint and other trails through the park. " +
                "The settlement has a visitor centre, hotels and mountain huts, and offers a quiet, nature-immersed base for exploring the park. " +
                "The surrounding forest is one of the best-preserved in Serbia, home to brown bears, wolves and a wide variety of bird species.");
        var zaovine = await EnsureLocationAsync(tara, "Zaovine", waterfrontType, 43.8768, 19.3956,
            summary: "A remote mountain lake area on Tara with scenic drives, peaceful shoreline walks and unspoilt nature.",
            description: "Zaovine is a small, quiet area in the western part of Tara National Park, centred around the artificial Zaovine Lake. " +
                "The lake was formed by damming the Zaovinska river and sits surrounded by dense forest at around 900 metres. " +
                "It is one of the most visually striking spots on Tara — calm, remote and largely untouched by mass tourism. " +
                "The area is ideal for scenic drives along the lake road, short lakeside walks and birdwatching in a peaceful setting.");
        await AddLocationTagsIfMissingAsync(mitrovac, "nature", "quiet-area", "family-friendly");
        await AddLocationTagsIfMissingAsync(zaovine, "waterfront", "nature", "panoramic-view");
        var banjskaStena = await EnsurePlaceAsync("Banjska Stena Viewpoint", "Iconic Tara viewpoint", "Tara National Park", ptNature, mitrovac, "A well-known viewpoint above the Drina canyon and one of Tara's classic short hikes.", 43.9534, 19.3909, "Open 24h", 4.9, 510);
        var zaovineLake = await EnsurePlaceAsync("Zaovine Lake", "Mountain lake and quiet nature area", "Zaovine, Tara", ptNature, zaovine, "A quiet lake area for scenic drives, short walks and nature-focused stays.", 43.8768, 19.3956, "Open 24h", 4.8, 330);
        await AddPlaceTagsIfMissingAsync(banjskaStena, "panoramic-view", "hiking", "nature", "walking-route");
        await AddPlaceTagsIfMissingAsync(zaovineLake, "waterfront", "quiet-area", "nature", "family-friendly");

        var kragujevac = await EnsureDestinationAsync(SerbiaRegionalManagerAnchorDestinationName, "Serbia's former royal capital and cultural heart of Šumadija, known for its first Serbian theatre, Šumarice Memorial Park and Bubanj lake.", cityType, kragujevacRegion, 44.010590360886276, 20.915278268657637);
        await AddDestinationTagsIfMissingAsync(kragujevac,
            "dest-culture",
            "dest-museums",
            "dest-sightseeing",
            "dest-urban",
            "dest-relaxed",
            "dest-local-food",
            "dest-grill",
            "dest-cafe-culture",
            "dest-budget-stays",
            "dest-river-walks");
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Destination, "Kragujevac", destinationId: kragujevac.Id, urls: new[] { Unsplash("1558019174-f9f5cb3b11a9") });
        var kgCenter = await EnsureLocationAsync(kragujevac, "Centar", districtType, 44.010590360886276, 20.915278268657637,
            summary: "The walkable city centre of Kragujevac with the main square, pedestrian streets, restaurants and cultural venues.",
            description: "Centar is the historic and commercial heart of Kragujevac, built around the main pedestrian zone and Trg vojvode Radomira Putnika square. " +
                "The area concentrates the city's main restaurants, cafés, pastry shops, museums and cultural institutions within easy walking distance. " +
                "Knežev Arsenal — one of the oldest national museums in Serbia — is located here, alongside the Serbian National Theatre and the main shopping streets. " +
                "The centre is compact and lively, making it the natural starting point for any visit to Kragujevac.");
        var kgSumarice = await EnsureLocationAsync(kragujevac, "Šumarice", parkType, 44.01608378833523, 20.885842264686087,
            summary: "A large memorial park west of the city centre, with forested paths, a lake and monuments to World War II victims.",
            description: "Šumarice Memorial Park is a large forest park on the western edge of Kragujevac, established to commemorate the Kragujevac massacre of October 1941, " +
                "in which German forces executed around 2,800 civilians. " +
                "The park is home to Šumaričko Lake, several memorial sculptures and monuments, and kilometres of forested walking and jogging paths. " +
                "It is a place of both historical significance and everyday recreation, visited by families, joggers and school groups throughout the year.");
        var kgBubanj = await EnsureLocationAsync(kragujevac, "Bubanj", waterfrontType, 44.01709232834811, 20.925827920265036,
            summary: "An eastern lakeside area with Bubanj Lake, fitness trails and green spaces for outdoor recreation.",
            description: "Bubanj is a residential and recreational area in the eastern part of Kragujevac, centred around Bubanj Lake — " +
                "an artificial lake popular with locals for morning runs, cycling and lakeside walks. " +
                "The surrounding greenery and maintained fitness trails make it one of the city's favourite spots for outdoor exercise and relaxed weekend outings. " +
                "The area has a calm, neighbourhood character and offers a pleasant alternative to the busier city centre.");
        await AddLocationTagsIfMissingAsync(kgCenter, "walkable-area", "local-area");
        await AddLocationTagsIfMissingAsync(kgSumarice, "nature", "history", "family-friendly");
        await AddLocationTagsIfMissingAsync(kgBubanj, "waterfront", "nature", "quiet-area");
        var spomenPark = await EnsurePlaceAsync("Spomen park Šumarice", "Memorial park dedicated to the victims of the 1941 massacre", "Park Šumarice, Kragujevac", ptLandmark, kgSumarice, "Šumarice Memorial Park is one of the most significant memorial parks in Serbia, dedicated to the 2,778 victims shot on 21 October 1941. The park covers 352 hectares and is adorned with monumental sculptures and memorials. The Museum of October 21 is also located within the park.", 44.01592, 20.88570, "Open 24h", 4.9, 420);
        var muzej21 = await EnsurePlaceAsync("Muzej 21. oktobar", "Memorial museum dedicated to the victims of 21 October 1941", "Park Šumarice, Kragujevac", ptMuseum, kgSumarice, "The Museum of October 21 in Šumarice Memorial Park is dedicated to the memory of 2,778 Kragujevac civilians, including students and teachers, shot on 21 October 1941. The moving exhibition of photographs, documents and personal belongings leaves a lasting impression on every visitor.", 44.0215005480748, 20.894342132628374, "09:00 - 18:00", 4.9, 378);
        var knezevansenal = await EnsurePlaceAsync("Knežev Arsenal", "National museum in a historic 1836 building", "Vojvode Putnika 1, Kragujevac", ptMuseum, kgCenter, "Knežev Arsenal, also known as the National Museum of Kragujevac, is housed in the historic 1836 Prince's Office building. The fourth-oldest national museum in Serbia preserves a rich collection of artefacts from the history, art and industrial heritage of Šumadija.", 44.00709093991931, 20.912182474605068, null, 4.6, 280);
        var jezeroBubanj = await EnsurePlaceAsync("Jezero Bubanj", "Artificial lake with fitness trails in the eastern part of the city", "Bubanj, Kragujevac", ptNature, kgBubanj, "Bubanj Lake is located in the eastern part of Kragujevac and is a popular spot for recreation, morning runs and lakeside walks. Surrounded by maintained fitness trails, benches and greenery, it is ideal for family outings and relaxation.", 44.01722, 20.92595, "Open 24h", 4.7, 312);
        var ambasadorRestoran = await EnsurePlaceAsync("Ambasador House", "Local and international cuisine restaurant in the city centre", "Centar, Kragujevac", ptRestaurant, kgCenter, "Ambasador House restaurant in the heart of Kragujevac is known for its wide menu of local and international cuisine, warm atmosphere and affordable prices. A favourite meeting place for locals and visitors alike for many years.", 44.02155482409981, 20.904514054554962, "08:00–00:00", 4.5, 187);
        var kafanaBalkan = await EnsurePlaceAsync("Kafana Balkan", "Traditional Serbian kafana with live music", "Centar, Kragujevac", ptRestaurant, kgCenter, "Kafana Balkan is an authentic Serbian kafana known for its home-style cooking, live music and warm atmosphere. One of the favourite spots in Kragujevac for an evening out in the spirit of traditional kafana culture, with sevdalinka and Serbian folk songs.", 44.01205768483894, 20.912913519847535, "08:00–00:00", 4.4, 145);
        var toroRestoran = await EnsurePlaceAsync("Toro", "Modern restaurant with premium grill and steaks", "Centar, Kragujevac", ptRestaurant, kgCenter, "Toro restaurant in central Kragujevac offers a premium grill experience — from carefully selected cuts of meat and steaks to a fine wine list. The modern interior and attentive service make it a favourite for special occasions and business dinners.", 44.02734826110409, 20.911625141325484, "08:00–00:00", 4.6, 203);
        var prvaGimnazija = await EnsurePlaceAsync("Prva kragujevačka gimnazija", "Oldest secondary school in Serbia, founded in 1833", "Kralja Petra I, Kragujevac", ptLandmark, kgCenter, "Prva Kragujevačka Gymnasium, founded in 1833 during the reign of Prince Miloš Obrenović, is the oldest secondary school in Serbia. The monumental building in the centre of Kragujevac stands as a symbol of the educational and cultural tradition of Šumadija.", 44.01068292385318, 20.9111652644484, "Mon–Fri 07:00–20:00", 4.6, 156);
        var knjazTeatar = await EnsurePlaceAsync("Knjazevsko-srpski teatar", "Cultural institution, heir to the first Serbian theatre of 1835", "Trg Topolivaca, Kragujevac", ptLandmark, kgCenter, "The National Theatre in Kragujevac is the heir of the Princely Serbian Theatre, established in 1835 when Kragujevac was the capital of the Principality of Serbia. One of the oldest theatres in the region, it remains an active cultural venue with a rich programme of theatrical and musical performances.", 44.01084434184476, 20.910283891424807, "Mon–Sat 10:00–14:00, 18:00–20:00", 4.7, 234);
        var hotelSumariceKg = await EnsurePlaceAsync("Hotel Šumarice", "Comfortable hotel near Šumarice Park", "Šumarice, Kragujevac", ptStay, kgSumarice, "Hotel Šumarice is situated near the Šumarice Memorial Park and offers comfortable accommodation with all necessary amenities. An ideal base for exploring the park, the nearby lake and the historic centre of Kragujevac.", 44.02119482532432, 20.87695101261244, "Open 24h", 4.4, 165);
        var hotelZelengora = await EnsurePlaceAsync("Hotel Zelengora", "Hotel in the centre of Kragujevac", "Centar, Kragujevac", ptStay, kgCenter, "Hotel Zelengora offers accessible accommodation in the central part of Kragujevac, suitable for family trips and short stays. Its proximity to the city centre and cultural attractions makes it a practical choice for visitors to Kragujevac.", 44.01261245597674, 20.91399511256099, "Open 24h", 4.3, 120);
        var hotelSumariceStay = await EnsureStayAsync(hotelSumariceKg, hotelStayType, 68m, "14:00", "11:00");
        var hotelZelengoraStay = await EnsureStayAsync(hotelZelengora, hotelStayType, 55m, "14:00", "11:00");
        await AddStayAmenitiesIfMissingAsync(hotelSumariceStay, wifi, parking, breakfast);
        await AddStayAmenitiesIfMissingAsync(hotelZelengoraStay, wifi, parking);
        var kgBpBooking = await EnsureBookingPartnerAsync("Booking.com", "dark");
        await RemoveBookingPartnerIfExistsAsync(hotelSumariceKg, kgBpBooking);
        await RemoveBookingPartnerIfExistsAsync(hotelZelengora, kgBpBooking);
        await EnsureRestaurantAsync(ambasadorRestoran);
        await EnsureRestaurantAsync(kafanaBalkan);
        await EnsureRestaurantAsync(toroRestoran);
        await AddPlaceTagsIfMissingAsync(spomenPark, "history", "culture", "nature", "walking-route", "family-friendly");
        await AddPlaceTagsIfMissingAsync(muzej21, "history", "culture", "museum", "family-friendly");
        await AddPlaceTagsIfMissingAsync(knezevansenal, "history", "culture", "museum", "architecture");
        await AddPlaceTagsIfMissingAsync(jezeroBubanj, "waterfront", "nature", "quiet-area", "walking-route", "family-friendly");
        await AddPlaceTagsIfMissingAsync(ambasadorRestoran, "local-food", "local-favorite", "casual");
        await AddPlaceTagsIfMissingAsync(kafanaBalkan, "local-food", "local-favorite", "traditional", "live-music");
        await AddPlaceTagsIfMissingAsync(toroRestoran, "local-food", "grill", "fine-dining", "local-favorite");
        await AddPlaceTagsIfMissingAsync(prvaGimnazija, "history", "culture", "architecture", "local-area");
        await AddPlaceTagsIfMissingAsync(knjazTeatar, "history", "culture", "local-favorite", "architecture");
        await AddPlaceTagsIfMissingAsync(hotelSumariceKg, "family-stay", "budget");
        await AddPlaceTagsIfMissingAsync(hotelZelengora, "family-stay", "budget");
        await UpdatePlaceHoursAsync(muzej21, "09:00–18:00",
            BuildHoursJson(("Mon", "09:00–18:00"), ("Tue", "08:00–18:00"), ("Wed", "08:00–18:00"), ("Thu", "08:00–18:00"), ("Fri", "09:00–18:00"), ("Sat", "09:00–16:00"), ("Sun", "09:00–16:00")));
        await UpdatePlaceHoursAsync(ambasadorRestoran, "08:00–00:00", BuildDailyHoursJson("08:00–00:00"));
        await UpdatePlaceHoursAsync(kafanaBalkan, "08:00–00:00", BuildDailyHoursJson("08:00–00:00"));
        await UpdatePlaceHoursAsync(toroRestoran, "08:00–00:00", BuildDailyHoursJson("08:00–00:00"));
        await UpdatePlaceHoursAsync(prvaGimnazija, "Mon–Fri 07:00–20:00",
            BuildHoursJson(("Mon", "07:00–20:00"), ("Tue", "07:00–20:00"), ("Wed", "07:00–20:00"), ("Thu", "07:00–20:00"), ("Fri", "07:00–20:00"), ("Sat", "Closed"), ("Sun", "Closed")));
        await UpdatePlaceHoursAsync(knjazTeatar, "Mon–Sat 10:00–14:00, 18:00–20:00",
            BuildHoursJson(("Mon", "10:00–14:00, 18:00–20:00"), ("Tue", "10:00–14:00, 18:00–20:00"), ("Wed", "10:00–14:00, 18:00–20:00"), ("Thu", "10:00–14:00, 18:00–20:00"), ("Fri", "10:00–14:00, 18:00–20:00"), ("Sat", "10:00–14:00, 18:00–20:00"), ("Sun", "Closed")));
        await ForceSetDestinationImageAsync(kragujevac, SeedWebp("kg"));
        await ForceSetDestinationImageAsync(beograd, SeedWebp("bg"));
        await ForceSetDestinationImageAsync(noviSad, SeedWebp("ns"));
        await ForceSetDestinationImageAsync(kopaonik, SeedWebp("kop"));
        await ForceSetDestinationImageAsync(tara, SeedWebp("tara"));
        await ForceSetDestinationImageAsync(zlatibor, SeedWebp("zlatibor"));
        var dubrovnikDest = await db.Destinations.FirstOrDefaultAsync(d => d.Name == "Dubrovnik");
        if (dubrovnikDest is not null)
            await ForceSetDestinationImageAsync(dubrovnikDest, SeedWebp("dubrovnik"));
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Place, spomenPark.Name, placeId: spomenPark.Id, urls: new[] { SeedWebp("spomen-park-sumarice") });
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Place, muzej21.Name, placeId: muzej21.Id, urls: new[] { SeedWebp("muzej21") });
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Place, knezevansenal.Name, placeId: knezevansenal.Id, urls: new[] { SeedWebp("knezev-arsenal"), SeedWebp("knezev-arsenal") });
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Place, jezeroBubanj.Name, placeId: jezeroBubanj.Id, urls: new[] { SeedWebp("jezero-bubanj") });
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Place, ambasadorRestoran.Name, placeId: ambasadorRestoran.Id, urls: new[] { SeedWebp("ambasador") });
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Place, kafanaBalkan.Name, placeId: kafanaBalkan.Id, urls: new[] { SeedWebp("kafana-balkan") });
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Place, toroRestoran.Name, placeId: toroRestoran.Id, urls: new[] { SeedWebp("toro") });
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Place, prvaGimnazija.Name, placeId: prvaGimnazija.Id, urls: new[] { SeedWebp("pkg") });
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Place, knjazTeatar.Name, placeId: knjazTeatar.Id, urls: new[] { SeedWebp("pozoriste") });
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Place, hotelSumariceKg.Name, placeId: hotelSumariceKg.Id, urls: new[] { SeedWebp("hotel-sumarice") });
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Place, hotelZelengora.Name, placeId: hotelZelengora.Id, urls: new[] { SeedWebp("hotel-zelengora") });
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Place, hotelMoskva.Name, placeId: hotelMoskva.Id, urls: new[] { SeedWebp("hotel-moskva") });
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Place, kafeterija.Name, placeId: kafeterija.Id, urls: new[] { SeedWebp("kafeterija-dorcol") });
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Place, skadarlija.Name, placeId: skadarlija.Id, urls: new[] { SeedWebp("restoran_velika_skadarlija") });
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Place, fortress.Name, placeId: fortress.Id, urls: new[] { SeedWebp("petrovaradin") });
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Place, strand.Name, placeId: strand.Id, urls: new[] { SeedWebp("strand-dunav") });
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Place, fishZelenish.Name, placeId: fishZelenish.Id, urls: new[] { SeedWebp("fish-and-zelenish") });
        await ForceSetPlaceImageAsync(goldGondola, SeedWebp("gondola"));
        await ForceSetPlaceImageAsync(tornikViewpoint, SeedWebp("tornik"));
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Place, skiCenterPlace.Name, placeId: skiCenterPlace.Id, urls: new[] { SeedWebp("kop-ski") });
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Place, hotelGrand.Name, placeId: hotelGrand.Id, urls: new[] { SeedWebp("grand-kopaonik") });
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Place, banjskaStena.Name, placeId: banjskaStena.Id, urls: new[] { SeedWebp("banjska-stena") });
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Place, zaovineLake.Name, placeId: zaovineLake.Id, urls: new[] { SeedWebp("lake-zaovine") });

        await RemoveEventIfExistsAsync("Belgrade Summer Nights");
        await RemoveEventIfExistsAsync("Tara Nature Walk");
        await RemoveEventIfExistsAsync("Novi Sad Local Food Market");
        await RemoveEventIfExistsAsync("Šumarice Park Guided Walk");

        var belgradeMusicWeek = await EnsureEventAsync(
            "Belgrade Music Week 2026",
            festivalType,
            beograd,
            savamala,
            "Belgrade Music Week is an international music festival and conference held across multiple venues in Belgrade. The festival features live performances across genres including pop, rock, electronic and alternative, alongside music industry panels and showcases connecting regional and international artists and professionals.",
            new DateTime(2026, 6, 5, 18, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 7, 23, 59, 0, DateTimeKind.Utc),
            "From 2,000 RSD",
            "3 days",
            "Belgrade Music Week",
            externalTicketLink: "https://efinity.rs/event-details/belgrade_mw_26_32222",
            summary: "Multi-venue international music festival and industry showcase held across Belgrade.",
            venueDescription: "The festival takes place across multiple venues in central Belgrade, including clubs, galleries and open-air stages in the Savamala and Stari Grad districts.",
            arrivalTips: "Belgrade is easily accessible by air, bus and rail. Venues are concentrated in the city centre and Savamala district. Check the official Belgrade Music Week website for the full programme and venue map.",
            accessibility: "Accessibility varies by venue. Check the official programme for specific venue details before attending.");
        await AddEventTagsIfMissingAsync(belgradeMusicWeek, "live-music-event", "cultural-festival", "ticketed-event", "summer-event");
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Event, belgradeMusicWeek.Name, eventId: belgradeMusicWeek.Id, urls: new[] { SeedWebp("music-week") });

        var taraUltraFest = await EnsureEventAsync(
            "Tara Ultra Outdoor Fest",
            outdoorType,
            tara,
            mitrovac,
            "Tara Ultra Outdoor Fest is an outdoor adventure festival centered in Bajina Bašta with trails through Tara National Park. The event features trail running across three distances — TUT62 (62 km ITRA certified), TUT28 (28 km ITRA certified) and TUT12 (13 km) — alongside rafting on the Drina River, SUP training, hiking, ecological workshops and a children's playground.",
            new DateTime(2026, 7, 4, 8, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 5, 18, 0, 0, DateTimeKind.Utc),
            "Varies by category",
            "2 days",
            "Turistička organizacija Tara-Drina",
            place: banjskaStena,
            externalTicketLink: "http://taraultratrail.rs/",
            summary: "Adventure trail running festival with three route distances through Tara National Park, plus rafting, SUP and outdoor workshops.",
            venueDescription: "The festival is centred in Bajina Bašta at the foot of Tara National Park. Trails pass through some of the most scenic parts of the park, including canyon viewpoints and riverside paths along the Drina.",
            arrivalTips: "Bajina Bašta is reachable by bus from Belgrade (around 3 hours). Register in advance at taraultratrail.rs. Accommodation in Bajina Bašta and on Tara fills up quickly for festival weekend — book early.",
            accessibility: "Trail distances vary in difficulty. The TUT12 is suitable for recreational runners. The festival also offers non-competitive activities including hiking and workshops for all fitness levels.");
        await AddEventTagsIfMissingAsync(taraUltraFest, "outdoor-tour-event", "nature-walk-event", "adventure-event", "ticketed-event");
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Event, taraUltraFest.Name, eventId: taraUltraFest.Id, urls: new[] { SeedWebp("Tara-Ultra") });

        var noviSadNocniBazar = await EnsureEventAsync(
            "Novosadski Noćni Bazar",
            marketType,
            noviSad,
            nsCenter,
            "Novosadski Noćni Bazar is a monthly evening bazaar held at the Riblja pijaca (Fish Market) in Novi Sad. Small producers, artists and creatives gather to offer handmade products, gastronomic specialties and good entertainment in a relaxed open-air setting.",
            new DateTime(2026, 6, 5, 18, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 5, 23, 0, 0, DateTimeKind.Utc),
            "Free",
            "5h",
            "nocnibazar.rs",
            summary: "Monthly evening open-air bazaar at Novi Sad's Fish Market, featuring local producers, artisans and live entertainment.",
            venueDescription: "The bazaar takes place at Riblja pijaca (Fish Market), a well-known outdoor market space in central Novi Sad, easily reachable on foot from the main square and the Danube waterfront.",
            arrivalTips: "The bazar is free to enter and runs on the first Friday of each month. The event may be rescheduled in case of bad weather — check the official website nocnibazar.rs before attending.",
            accessibility: "The event is held in an open outdoor market space. Check the official website for any specific accessibility information.");
        await AddEventTagsIfMissingAsync(noviSadNocniBazar, "food-market-event", "local-event", "free-entry-event", "cultural-festival");
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Event, noviSadNocniBazar.Name, eventId: noviSadNocniBazar.Id, urls: new[] { SeedWebp("novosadski-nocni-bazar") });

        var zlatiborFolkFest = await EnsureEventAsync(
            "Zlatibor Folk Fest 2026",
            festivalType,
            zlatibor,
            zlatiborCenter,
            "Zlatibor Folk Fest is the first international folklore festival on Zlatibor, bringing together folk ensembles from Serbia, Turkey and North Macedonia for two concert performances. Organised by the Cultural-Artistic Society 'Zlatibor' from Čajetina, the festival takes place at King's Square and the summer stage in Tripkova.",
            new DateTime(2026, 5, 28, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 1, 22, 0, 0, DateTimeKind.Utc),
            "Free",
            "5 days",
            "KUD Zlatibor, Čajetina",
            summary: "First international folklore festival on Zlatibor, featuring ensembles from Serbia, Turkey and North Macedonia across two outdoor concert evenings.",
            venueDescription: "Concerts take place at King's Square (Kraljev trg) in the heart of Zlatibor resort, and at the summer stage in the nearby settlement of Tripkova.",
            arrivalTips: "Zlatibor is reachable from Belgrade by bus in around 3 hours. Accommodation on the mountain is widely available. The concert venues are easily accessible on foot from the main resort area.",
            accessibility: "Concerts are held at open-air venues. Check local information for specific accessibility arrangements.");
        await AddEventTagsIfMissingAsync(zlatiborFolkFest, "cultural-festival", "local-event", "free-entry-event");
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Event, zlatiborFolkFest.Name, eventId: zlatiborFolkFest.Id, urls: new[] { SeedWebp("zlatibor-folklor") });

        // ─── New Kragujevac places, hotels and events ──────────────────────────────

        var ptShopping = await EnsurePlaceTypeAsync("Shopping", "Shopping centres and retail areas");

        // Official Website booking partner for Zelengora and Šumarice
        var officialSite = await EnsureBookingPartnerAsync("Official Website", "muted");
        await AddBookingPartnerIfMissingAsync(hotelZelengora, officialSite, "https://www.hotelzelengora.com/");
        await AddBookingPartnerIfMissingAsync(hotelSumariceKg, officialSite, "https://hotelsumarice.com/");

        // New hotels
        var hotelPresidentDeL = await EnsurePlaceAsync(
            "Hotel President De Luxe",
            "Contemporary garni hotel in the city centre",
            "Centar, Kragujevac",
            ptStay,
            kgCenter,
            "Hotel President De Luxe is a contemporary garni hotel in the heart of Kragujevac. The hotel offers modern, well-appointed rooms and suites with city views, complimentary Wi-Fi and air conditioning throughout. Its central location makes it an ideal base for exploring the city's historical sights, restaurants and cultural venues.",
            44.01695589014767,
            20.915319210164554,
            "Open 24h",
            4.6,
            142);
        var hotelPresidentStay = await EnsureStayAsync(hotelPresidentDeL, hotelStayType, 80m, "14:00", "11:00");
        await AddStayAmenitiesIfMissingAsync(hotelPresidentStay, wifi, airConditioning, parking);
        await AddBookingPartnerIfMissingAsync(hotelPresidentDeL, kgBpBooking, "https://www.booking.com/hotel/rs/president-de-luxe-doo.html");
        await AddPlaceTagsIfMissingAsync(hotelPresidentDeL, "family-stay", "budget");

        var hotelIndustrial = await EnsurePlaceAsync(
            "Industrial 1853",
            "Design hotel in a restored historic building",
            "Kragujevac",
            ptStay,
            kgCenter,
            "Industrial 1853 is a design hotel housed in a beautifully restored historic building, blending industrial heritage with contemporary comfort. Each room is uniquely designed with exposed brick and premium furnishings. Located within easy reach of Kragujevac's landmarks, the hotel is popular with travellers seeking character and style.",
            44.00413643424874,
            20.91286120866569,
            "Open 24h",
            4.7,
            89);
        var hotelIndustrialStay = await EnsureStayAsync(hotelIndustrial, hotelStayType, 95m, "15:00", "11:00");
        await AddStayAmenitiesIfMissingAsync(hotelIndustrialStay, wifi, airConditioning, breakfast);
        await AddBookingPartnerIfMissingAsync(hotelIndustrial, kgBpBooking, "https://www.booking.com/hotel/rs/industrial-1853.html");
        await AddPlaceTagsIfMissingAsync(hotelIndustrial, "family-stay", "premium");

        var hotelKragujevac = await EnsurePlaceAsync(
            "Hotel Kragujevac",
            "Central hotel with a long tradition",
            "Centar, Kragujevac",
            ptStay,
            kgCenter,
            "Hotel Kragujevac is a well-established hotel in the centre of the city, offering comfortable rooms and a full range of hotel services. A popular choice for both business travellers and tourists, the hotel combines a convenient central location with reliable service and easy access to Kragujevac's main attractions.",
            44.010119399470184,
            20.917077311019384,
            "Open 24h",
            4.4,
            215);
        var hotelKragujevacStay = await EnsureStayAsync(hotelKragujevac, hotelStayType, 72m, "14:00", "11:00");
        await AddStayAmenitiesIfMissingAsync(hotelKragujevacStay, wifi, parking, airConditioning);
        await AddBookingPartnerIfMissingAsync(hotelKragujevac, kgBpBooking, "https://www.booking.com/hotel/rs/kragujevac.html");
        await AddPlaceTagsIfMissingAsync(hotelKragujevac, "family-stay", "budget");

        // New restaurants / cafes / pastry shops
        var peron = await EnsurePlaceAsync(
            "Peron",
            "Popular pancake and crêpe restaurant",
            "Centar, Kragujevac",
            ptRestaurant,
            kgCenter,
            "Peron is a beloved pancake and crêpe restaurant in central Kragujevac, known for its extensive menu of sweet and savoury crêpes, waffles and desserts. A favourite among locals for quick sweet treats, afternoon snacks or casual dessert outings.",
            44.01033346991599,
            20.917272596738187,
            "Mon–Sat 08:00–00:00",
            4.5,
            167);
        await EnsureRestaurantAsync(peron);
        await AddPlaceTagsIfMissingAsync(peron, "local-food", "casual", "local-favorite", "dessert");
        await UpdatePlaceHoursAsync(peron, "Mon–Sat 08:00–00:00",
            BuildHoursJson(("Mon", "08:00–00:00"), ("Tue", "08:00–00:00"), ("Wed", "08:00–00:00"), ("Thu", "08:00–00:00"), ("Fri", "08:00–00:00"), ("Sat", "08:00–00:00"), ("Sun", "10:00–00:00")));

        var velikPark = await EnsurePlaceAsync(
            "Veliki Park",
            "Large central park for walks and relaxation",
            "Centar, Kragujevac",
            ptNature,
            kgCenter,
            "Veliki Park (Great Park) is a spacious urban green space in the heart of Kragujevac, ideal for leisurely strolls, morning jogs and family outings. Lined with mature trees and walkways, the park offers a peaceful retreat in the city with benches, seasonal flowers and pleasant scenery.",
            44.01721017188732,
            20.905147810517068,
            "Open 24h",
            4.7,
            298);
        await AddPlaceTagsIfMissingAsync(velikPark, "nature", "walking-route", "family-friendly", "quiet-area");

        var sumarickoJezero = await EnsurePlaceAsync(
            "Šumaričko jezero",
            "Natural lake inside Šumarice Memorial Park",
            "Park Šumarice, Kragujevac",
            ptNature,
            kgSumarice,
            "Šumaričko Lake is a serene natural lake nestled within Šumarice Memorial Park. Surrounded by forests and parkland, it is a tranquil spot for walks along the shoreline, birdwatching and quiet reflection. The lake area is popular with families, joggers and nature lovers visiting the broader memorial park complex.",
            44.02947408248394,
            20.877459346904196,
            "Open 24h",
            4.8,
            211);
        await AddPlaceTagsIfMissingAsync(sumarickoJezero, "waterfront", "nature", "quiet-area", "walking-route", "family-friendly");

        var srce = await EnsurePlaceAsync(
            "Poslastičarnica Srce",
            "Traditional Serbian pastry shop and cake house",
            "Centar, Kragujevac",
            ptCafe,
            kgCenter,
            "Poslastičarnica Srce is a well-loved pastry shop in the centre of Kragujevac, offering a wide selection of traditional Serbian cakes, pastries, tortes and sweet treats. A go-to spot for locals with a sweet tooth, known for its home-style recipes and reliably delicious cakes.",
            44.01318592662767,
            20.91249812400943,
            "Mon–Thu 08:00–00:00",
            4.6,
            134);
        await AddPlaceTagsIfMissingAsync(srce, "dessert", "casual", "local-favorite", "local-food");
        await UpdatePlaceHoursAsync(srce, "Mon–Thu 08:00–00:00",
            BuildHoursJson(("Mon", "08:00–00:00"), ("Tue", "08:00–00:00"), ("Wed", "08:00–00:00"), ("Thu", "08:00–00:00"), ("Fri", "08:00–01:00"), ("Sat", "08:00–01:00"), ("Sun", "11:00–00:00")));

        var nocciola = await EnsurePlaceAsync(
            "Nocciola",
            "Artisan ice cream and dessert shop",
            "Centar, Kragujevac",
            ptCafe,
            kgCenter,
            "Nocciola is a popular artisan ice cream and dessert shop in Kragujevac, known for its creative flavours and high-quality ingredients. The warm, welcoming interior makes it a pleasant stop for ice cream, creamy desserts and coffee, enjoyed by both locals and visitors throughout the year.",
            44.01132485906991,
            20.914450117129455,
            "09:00–23:00",
            4.7,
            189);
        await AddPlaceTagsIfMissingAsync(nocciola, "dessert", "ice-cream", "casual", "local-favorite", "local-food");
        await UpdatePlaceHoursAsync(nocciola, "09:00–23:00", BuildDailyHoursJson("09:00–23:00"));

        var mademoiselle = await EnsurePlaceAsync(
            "Mademoiselle",
            "French-inspired café and pastry shop",
            "Centar, Kragujevac",
            ptCafe,
            kgCenter,
            "Mademoiselle is a charming French-inspired café and pastry shop in Kragujevac, offering freshly baked croissants, cakes and specialty coffee. The elegant interior and carefully curated sweet selection make it a favourite spot for a refined breakfast or afternoon coffee break in the city centre.",
            44.01761090881236,
            20.912911308802148,
            "Mon–Sat 08:00–00:00",
            4.6,
            156);
        await AddPlaceTagsIfMissingAsync(mademoiselle, "dessert", "specialty-coffee", "casual", "local-favorite", "local-food");
        await UpdatePlaceHoursAsync(mademoiselle, "Mon–Sat 08:00–00:00",
            BuildHoursJson(("Mon", "08:00–00:00"), ("Tue", "08:00–00:00"), ("Wed", "08:00–00:00"), ("Thu", "08:00–00:00"), ("Fri", "08:00–00:00"), ("Sat", "08:00–00:00"), ("Sun", "Closed")));

        var kafemat27 = await EnsurePlaceAsync(
            "Kafemat 27",
            "Cosy neighbourhood café",
            "Centar, Kragujevac",
            ptCafe,
            kgCenter,
            "Kafemat 27 is a cosy neighbourhood café in the heart of Kragujevac, popular for its relaxed atmosphere, good coffee and light bites. A regular stop for locals on their morning routine or afternoon break, known for its friendly service and comfortable seating.",
            44.01375433075671,
            20.912939901465172,
            "Mon–Thu 07:00–00:00",
            4.4,
            118);
        await AddPlaceTagsIfMissingAsync(kafemat27, "specialty-coffee", "casual", "local-favorite");
        await UpdatePlaceHoursAsync(kafemat27, "Mon–Thu 07:00–00:00",
            BuildHoursJson(("Mon", "07:00–00:00"), ("Tue", "07:00–00:00"), ("Wed", "07:00–00:00"), ("Thu", "07:00–00:00"), ("Fri", "07:00–01:00"), ("Sat", "07:00–01:00"), ("Sun", "08:00–00:00")));

        var cafeteria = await EnsurePlaceAsync(
            "Cafeteria",
            "City centre café with a wide menu",
            "Centar, Kragujevac",
            ptCafe,
            kgCenter,
            "Cafeteria is a well-positioned café in central Kragujevac, offering a varied selection of coffee, soft drinks, juices and light snacks. A popular meeting spot for locals, it is known for its accessible prices, relaxed vibe and convenient central location.",
            44.01046094483562,
            20.915536038992816,
            "Mon–Thu 07:30–00:00",
            4.3,
            102);
        await AddPlaceTagsIfMissingAsync(cafeteria, "specialty-coffee", "casual", "local-area");
        await UpdatePlaceHoursAsync(cafeteria, "Mon–Thu 07:30–00:00",
            BuildHoursJson(("Mon", "07:30–00:00"), ("Tue", "07:30–00:00"), ("Wed", "07:30–00:00"), ("Thu", "07:30–00:00"), ("Fri", "07:30–01:00"), ("Sat", "07:30–01:00"), ("Sun", "07:30–00:00")));

        var bigFashion = await EnsurePlaceAsync(
            "Big Fashion Kragujevac",
            "Modern shopping centre on the edge of the city",
            "Kragujevac",
            ptShopping,
            kgCenter,
            "Big Fashion Kragujevac is a modern shopping centre located on the western side of the city. The mall houses a wide range of international and domestic fashion brands, a food court and entertainment facilities. A popular destination for family shopping trips and leisure outings.",
            44.008706935951636,
            20.895814154695795,
            "09:00 - 22:00",
            4.2,
            324);
        await AddPlaceTagsIfMissingAsync(bigFashion, "family-friendly", "local-area");

        // New Kragujevac events (both at Knežev Arsenal)
        var arsenalFest = await EnsureEventAsync(
            "Arsenal Fest 2026",
            festivalType,
            kragujevac,
            kgCenter,
            "Arsenal Fest is one of Serbia's most celebrated music festivals, held annually on the grounds of the historic Knežev Arsenal in Kragujevac. The festival features three stages and brings together acclaimed regional and international artists across rock, alternative, pop and electronic genres. Over three evenings, the Arsenal's courtyard transforms into a vibrant outdoor concert venue, with food stalls, an open-air bar area and a festival village atmosphere.",
            new DateTime(2026, 6, 26, 18, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 28, 23, 59, 0, DateTimeKind.Utc),
            "From 1,500 RSD",
            "3 days",
            "Arsenal Fest",
            place: knezevansenal,
            externalTicketLink: "https://arsenalfest.rs/en/tickets/",
            summary: "Three-day open-air music festival at the historic Knežev Arsenal, featuring local and international artists across multiple stages.",
            venueDescription: "Knežev Arsenal (the Prince's Arsenal) is a 19th-century historic complex in central Kragujevac, used as the site of one of Serbia's most iconic outdoor music festivals. The open-air stage area within the arsenal grounds offers a unique backdrop of historic walls and spacious outdoor terraces.",
            arrivalTips: "Kragujevac is easily reachable by bus from Belgrade (around 2 hours). The Arsenal venue is a 10-minute walk from the city centre. Tickets can be purchased online at arsenalfest.rs or at the venue box office on the day of the event.",
            accessibility: "The festival grounds are largely flat and accessible. Check the official Arsenal Fest website for full accessibility information and assistance services before attending.");
        await AddEventTagsIfMissingAsync(arsenalFest, "cultural-festival", "live-music-event", "ticketed-event", "summer-event");
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Event, arsenalFest.Name, eventId: arsenalFest.Id, urls: new[] { SeedWebp("arsenal") });

        var vibeFest = await EnsureEventAsync(
            "Vibe Festival 2026",
            festivalType,
            kragujevac,
            kgCenter,
            "Vibe Festival is a summer music festival held at the open-air grounds of Knežev Arsenal in Kragujevac. The festival showcases a diverse lineup of domestic and regional electronic, pop and urban artists across multiple stages. With a laid-back festival village atmosphere, food and drinks area and a welcoming crowd, Vibe Festival has quickly established itself as a highlight of Kragujevac's summer cultural calendar.",
            new DateTime(2026, 7, 23, 18, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 25, 23, 59, 0, DateTimeKind.Utc),
            "4,500 RSD (3-day pass)",
            "3 days",
            "Vibe Festival",
            place: knezevansenal,
            externalTicketLink: "https://bilet.gigstix.com/rs/store/gigstix_v2/sectionGroup/index/8982",
            summary: "Three-day summer music festival at Knežev Arsenal featuring electronic, pop and urban artists from the region.",
            venueDescription: "Knežev Arsenal is a 19th-century historic complex in central Kragujevac that hosts some of the city's biggest summer events. The spacious outdoor grounds provide an atmospheric setting for the festival's multiple stages and artist village.",
            arrivalTips: "The festival venue at Knežev Arsenal is centrally located and walkable from the city centre. Parking is available nearby. Plan your arrival early as queues can form before the main acts. Day tickets and 3-day passes are available online.",
            accessibility: "The festival site is on largely flat ground. Check the official Vibe Festival information for details on accessible areas and assistance services before attending.");
        await AddEventTagsIfMissingAsync(vibeFest, "cultural-festival", "live-music-event", "ticketed-event", "summer-event");
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Event, vibeFest.Name, eventId: vibeFest.Id, urls: new[] { SeedWebp("vibe") });

        // Image assignments for new places
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Place, hotelPresidentDeL.Name, placeId: hotelPresidentDeL.Id, urls: new[] { SeedWebp("garni-hotel") });
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Place, hotelIndustrial.Name, placeId: hotelIndustrial.Id, urls: new[] { SeedWebp("industrial") });
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Place, hotelKragujevac.Name, placeId: hotelKragujevac.Id, urls: new[] { SeedWebp("hotel-kragujevac") });
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Place, peron.Name, placeId: peron.Id, urls: new[] { SeedWebp("peron") });
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Place, velikPark.Name, placeId: velikPark.Id, urls: new[] { SeedWebp("veliki-park") });
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Place, sumarickoJezero.Name, placeId: sumarickoJezero.Id, urls: new[] { SeedWebp("sumaricko-jezero") });
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Place, srce.Name, placeId: srce.Id, urls: new[] { SeedWebp("srce") });
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Place, nocciola.Name, placeId: nocciola.Id, urls: new[] { SeedWebp("nocciola") });
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Place, mademoiselle.Name, placeId: mademoiselle.Id, urls: new[] { SeedWebp("mademoiselle") });
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Place, kafemat27.Name, placeId: kafemat27.Id, urls: new[] { SeedWebp("kafemat27") });
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Place, cafeteria.Name, placeId: cafeteria.Id, urls: new[] { SeedWebp("cafeteria") });
        await AddEntityImagesIfMissingAsync(EntityImageTargetType.Place, bigFashion.Name, placeId: bigFashion.Id, urls: new[] { SeedWebp("big-kg") });

        await RemoveUnreferencedLegacySerbiaRegionsAsync(db, serbia);
        await EnsureSerbiaRegionalManagerAsync(db, kragujevacRegion);
    }

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

        async Task<Location> EnsureLocationAsync(
            string name,
            LocationType locationType,
            double latitude,
            double longitude,
            string? summary = null,
            string? description = null)
        {
            var location = await db.Locations.FirstOrDefaultAsync(x =>
                x.DestinationId == dubrovnik.Id &&
                x.Name == name);

            if (location is not null)
            {
                var changed = false;
                if (summary is not null && string.IsNullOrWhiteSpace(location.Summary))
                {
                    location.Summary = summary;
                    changed = true;
                }
                if (description is not null && string.IsNullOrWhiteSpace(location.Description))
                {
                    location.Description = description;
                    changed = true;
                }
                if (changed) await db.SaveChangesAsync();
                return location;
            }

            location = new Location
            {
                Name = name,
                Summary = summary,
                Description = description ?? string.Empty,
                DestinationId = dubrovnik.Id,
                LocationTypeId = locationType.Id,
                Latitude = latitude,
                Longitude = longitude,
                Status = "Published",
                CreatedAt = now,
                PublishedAt = now
            };

            db.Locations.Add(location);
            await db.SaveChangesAsync();
            return location;
        }

        async Task<PlaceType> EnsurePlaceTypeAsync(string name, string description, bool isOwnable = false)
        {
            var placeType = await db.PlaceTypes.FirstOrDefaultAsync(x => x.Name == name);
            if (placeType is not null)
            {
                if (placeType.IsOwnable != isOwnable)
                {
                    placeType.IsOwnable = isOwnable;
                    placeType.UpdatedAt = now;
                    await db.SaveChangesAsync();
                }

                return placeType;
            }

            placeType = new PlaceType
            {
                Name = name,
                Description = description,
                IsOwnable = isOwnable,
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
            string? openingHours,
            double rating,
            int reviewCount,
            string? contactEmail = null,
            string? number = null)
        {
            var place = await db.Places.FirstOrDefaultAsync(x => x.Name == name);
            if (place is not null)
            {
                var changed = false;

                if (place.PlaceTypeId != placeType.Id)
                {
                    place.PlaceTypeId = placeType.Id;
                    changed = true;
                }

                if (place.LocationId != location.Id)
                {
                    place.LocationId = location.Id;
                    changed = true;
                }

                if (Math.Abs(place.Latitude - latitude) > 1e-9 || Math.Abs(place.Longitude - longitude) > 1e-9)
                {
                    place.Latitude = latitude;
                    place.Longitude = longitude;
                    changed = true;
                }

                if (changed)
                {
                    await db.SaveChangesAsync();
                }

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
                DestinationId = location.DestinationId,
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
                    ImageRole = i == 0 ? EntityImageRole.Cover : i == 1 ? EntityImageRole.Card : EntityImageRole.Gallery,
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
                    ImageRole = i == 0 ? EntityImageRole.Cover : i == 1 ? EntityImageRole.Card : EntityImageRole.Gallery,
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

        var oldTown = await EnsureLocationAsync("Old Town", neighborhoodType, 42.6413, 18.1090,
            summary: "The historic walled core of Dubrovnik, encircled by medieval fortifications and open to the Adriatic.",
            description: "Dubrovnik's Old Town is a UNESCO World Heritage Site enclosed by some of the best-preserved medieval city walls in Europe. " +
                "The main pedestrian artery, Stradun, connects the Pile Gate to the Ploče Gate and is lined with limestone-paved streets, " +
                "Renaissance churches, baroque fountains, and open-air cafes. " +
                "Key landmarks include the Rector's Palace, the Franciscan Monastery with its 14th-century pharmacy, " +
                "and a network of steep lanes leading up to the walls walkway with panoramic sea views.");
        var ploce = await EnsureLocationAsync("Ploce", districtType, 42.6422, 18.1180,
            summary: "The upscale residential and beach district east of the Old Town walls, home to pebble coves and sea-view terraces.",
            description: "Ploče is the district immediately east of the Ploče Gate, the eastern entrance to Dubrovnik's Old Town. " +
                "It offers a quieter, more residential feel than the walled city itself, with tree-lined lanes, boutique hotels, " +
                "and direct access to Banje Beach — the closest sandy and pebble beach to the city centre. " +
                "Several luxury hotels are located here, taking advantage of the elevated views over the Adriatic and toward Lokrum Island.");
        var lapad = await EnsureLocationAsync("Lapad", districtType, 42.6559, 18.0712,
            summary: "A peninsula district west of the city centre with family beaches, a resort strip, and a relaxed bay promenade.",
            description: "Lapad is a green peninsula to the west of Dubrovnik's centre, separated from the city by the Gruž harbour. " +
                "It is the main resort area, characterised by a long promenade running along Lapad Bay, " +
                "a concentration of hotels and apartment complexes, and several pebble and sand beaches suited to families.");
        var gruz = await EnsureLocationAsync("Gruz", districtType, 42.6592, 18.0863,
            summary: "The working harbour and ferry port of Dubrovnik, with a daily market, local restaurants, and island-boat connections.",
            description: "Gruž is Dubrovnik's main port and harbour area. " +
                "It serves as the departure point for ferries to the Elaphiti Islands, Korčula, Split, and international lines. " +
                "The Gruž morning market is one of the best places to buy fresh seafood, produce, and local cheese. " +
                "Several well-regarded konobas and seafood restaurants are found near the waterfront.");
        var pile = await EnsureLocationAsync("Pile", neighborhoodType, 42.6423, 18.1051,
            summary: "The gateway neighbourhood outside the Old Town's main western entrance, a hub for transport, bars and coastal views.",
            description: "Pile is the busy area immediately outside the Pile Gate — the main western entrance to the Old Town. " +
                "It serves as the city's main transport junction, with bus stops, taxi ranks, and the beginning of the main road to the airport. " +
                "The neighbourhood offers a mix of visitor facilities, restaurants, and a scenic coastal walk toward Fort Lovrijenac.");
        var srd = await EnsureLocationAsync("Mount Srd", viewpointType, 42.6492, 18.1112,
            summary: "The limestone ridge above Dubrovnik with a cable car, fort ruins, and 360-degree views of the Adriatic and old town rooftops.",
            description: "Mount Srđ rises sharply above Dubrovnik's Old Town to about 412 metres. " +
                "A modern cable car connects the city to the summit in under four minutes. " +
                "At the top, Fort Imperial (a Napoleonic-era fortification) houses a museum of the 1991–1995 siege. " +
                "The panoramic views take in the Old Town, the Elaphiti Islands, and on clear days as far as the Pelješac Peninsula.");
        var lokrum = await EnsureLocationAsync("Lokrum", islandType, 42.6268, 18.1222,
            summary: "A forested nature reserve island 10 minutes by boat from the Old Town harbour, with peacocks, a botanical garden and swimming coves.",
            description: "Lokrum is a small uninhabited island just offshore from Dubrovnik, declared a nature reserve in 1976. " +
                "Regular boat services run from the Old Town harbour throughout the day in season. " +
                "The island has a medieval Benedictine monastery, a botanical garden, salt-lake swimming areas, and rocky coves for sea swimming. " +
                "Peacocks roam freely among the pine and cypress trees, and the island is a popular half-day escape from the city.");
        var trsteno = await EnsureLocationAsync("Trsteno", districtType, 42.7158, 17.9784,
            summary: "A small coastal village 18 km northwest of Dubrovnik, known for its Renaissance arboretum — one of the oldest in Croatia.",
            description: "Trsteno is a quiet coastal settlement on the Dubrovnik Riviera, about 18 kilometres northwest of the city. " +
                "It is best known for the Trsteno Arboretum, a Renaissance-era garden and tree park founded in the 15th century by the Gučetić noble family. " +
                "The arboretum features a pair of enormous plane trees estimated to be over 500 years old, ornamental fountains, and sea views. " +
                "The location was used as a filming location for the King's Landing gardens in Game of Thrones.");

        await AddLocationTagsIfMissingAsync(pile, "old-town-nearby", "walkable-area");
        await AddLocationTagsIfMissingAsync(srd, "panoramic-view", "nature");
        await AddLocationTagsIfMissingAsync(lokrum, "island-escape", "nature");
        await AddLocationTagsIfMissingAsync(trsteno, "relaxed-area", "nature");

        var ptBeach = await EnsurePlaceTypeAsync("Beach", "Beaches and coastal swimming areas");
        var ptRestaurant = await EnsurePlaceTypeAsync("Restaurant", "Dining establishments", isOwnable: true);
        var ptCafe = await EnsurePlaceTypeAsync("Cafe", "Cafes and coffee shops", isOwnable: true);
        var ptBar = await EnsurePlaceTypeAsync("Bar", "Bars, wine bars and nightlife venues", isOwnable: true);
        var ptLandmark = await EnsurePlaceTypeAsync("Landmark", "Landmarks, monuments and historical places");
        var ptNature = await EnsurePlaceTypeAsync("Nature", "Nature spots and island escapes");
        var ptStay = await EnsurePlaceTypeAsync("Stay", "Hotels, apartments, villas and other accommodation", isOwnable: true);
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
        var fitnessCenter = await EnsureStayAmenityAsync("Fitness Center", "fitness-center", "gym", "Fitness facilities available");
        var restaurantAmenity = await EnsureStayAmenityAsync("Restaurant", "restaurant", "restaurant", "On-site dining available");
        var airConditioning = await EnsureStayAmenityAsync("Air Conditioning", "air-conditioning", "ac", "Air-conditioned rooms");

        var outdoor = await EnsureAmenityAsync("Outdoor Seating", "outdoor");
        var seaView = await EnsureAmenityAsync("Sea View", "sea");
        var family = await EnsureAmenityAsync("Family Friendly", "family");
        var wifiAmenity = await EnsureAmenityAsync("Free WiFi", "wifi");
        await EnsureAmenityAsync("Swimming Pool", "pool");
        await EnsureAmenityAsync("Parking", "parking");
        await EnsureAmenityAsync("Spa", "spa");
        await EnsureAmenityAsync("Gym", "gym");
        await EnsureAmenityAsync("Breakfast", "breakfast");
        await EnsureAmenityAsync("Air Conditioning", "ac");
        await EnsureAmenityAsync("Restaurant", "restaurant");

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
            ptLandmark,
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
            ptLandmark,
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
            ptLandmark,
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
            ptLandmark,
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
            ptLandmark,
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
            ptLandmark,
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
        await AddPlaceTagsIfMissingAsync(tajMahal, "traditional", "casual", "croatian", "old-town", "local-favorite", "local-food");
        await AddPlaceTagsIfMissingAsync(arsenal, "breakfast", "specialty-coffee", "waterfront-dining", "sea-view", "old-town");
        await AddPlaceTagsIfMissingAsync(dvino, "wine", "wine-bar", "nightlife", "casual", "hidden-gem", "old-town");
        await AddPlaceTagsIfMissingAsync(peppino, "dessert", "ice-cream", "casual", "budget", "old-town");
        await AddPlaceTagsIfMissingAsync(orsan, "seafood", "grill", "waterfront-dining", "harbor-view", "local-favorite");
        await AddPlaceTagsIfMissingAsync(pantarul, "croatian", "mediterranean", "casual", "local-favorite");

        await AddPlaceTagsIfMissingAsync(rectorsPalace, "history", "culture", "art", "architecture", "museum", "old-town");
        await AddPlaceTagsIfMissingAsync(sponzaPalace, "history", "culture", "architecture", "old-town", "walking-route");
        await AddPlaceTagsIfMissingAsync(franciscan, "history", "culture", "art", "museum", "old-town", "monastery", "religious-site", "architecture");
        await AddPlaceTagsIfMissingAsync(warPhoto, "art", "history", "museum", "gallery", "hidden-gem", "old-town");
        await AddPlaceTagsIfMissingAsync(lovrijenac, "history", "culture", "fortress", "panoramic-view", "walking-route");
        await AddPlaceTagsIfMissingAsync(mountSrd, "nature", "hiking", "panoramic-view", "sunset-view", "walking-route");
        await AddPlaceTagsIfMissingAsync(trstenoArboretum, "nature", "botanical-garden", "garden", "park", "walking-route", "quiet-area");
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

        await AddEventTagsIfMissingAsync(stradunClassical, "classical-music-event", "live-music", "ticketed-event", "evening-event");
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
        var placesByName = (await db.Places
                .OrderBy(place => place.Id)
                .ToListAsync())
            .GroupBy(place => place.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

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
        await EnsureRoleAsync(db, "ContentModerator", "Can moderate reported reviews and content.");
        await EnsureRoleAsync(db, "RegionalManager", "Can review and approve regional user contributions.");
        await EnsureRoleAsync(db, "TerritoryManager", "Can manage territories and assigned regions.");
        await EnsureRoleAsync(db, "TerritoryAssociate", "Can manage assigned territories except associate assignment and protected role removal.");
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

    private static async Task EnsureDemoRegionalManagerAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        var normalizedEmail = DemoRegionalManagerEmail.ToLowerInvariant();
        var hasher = new PasswordHasher<User>();

        var region = await db.Regions
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(x => x.Name == DemoRegionalManagerRegionName);

        region ??= await db.Regions
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync();

        if (region is null)
        {
            return;
        }

        var role = await db.Roles.FirstOrDefaultAsync(x => x.Name == RegionalManagerRoleName);
        if (role is null)
        {
            role = new Role
            {
                Name = RegionalManagerRoleName,
                Description = "Can review and approve regional user contributions.",
                CreatedAt = now
            };

            db.Roles.Add(role);
            await db.SaveChangesAsync();
        }

        var user = await db.Users
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(x => x.Email.ToLower() == normalizedEmail);

        if (user is null)
        {
            user = new User
            {
                Username = await ResolveSeedUsernameAsync(db, DemoRegionalManagerUsername),
                Email = normalizedEmail,
                FirstName = "Pera",
                LastName = "Regional Manager",
                PasswordHash = string.Empty,
                DisplayName = "Pera",
                PhoneNumber = string.Empty,
                Location = region.Name,
                NeedsInterestsOnboarding = false,
                CreatedAt = now
            };

            user.PasswordHash = hasher.HashPassword(user, DemoRegionalManagerPassword);
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }
        else if (SeedPasswordNeedsReset(hasher, user, DemoRegionalManagerPassword))
        {
            user.PasswordHash = hasher.HashPassword(user, DemoRegionalManagerPassword);
            user.UpdatedAt = now;
            await db.SaveChangesAsync();
        }

        var hasRole = await db.UserRoles.AnyAsync(x => x.UserId == user.Id && x.RoleId == role.Id);
        if (!hasRole)
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = role.Id
            });
        }

        var assignment = await db.RegionalManagerRegions.FindAsync(user.Id, region.Id);
        if (assignment is null)
        {
            db.RegionalManagerRegions.Add(new RegionalManagerRegion
            {
                UserId = user.Id,
                RegionId = region.Id,
                IsActive = true,
                CreatedAt = now
            });
        }
        else if (!assignment.IsActive)
        {
            assignment.IsActive = true;
            assignment.UpdatedAt = now;
        }

        await db.SaveChangesAsync();
    }

    private static async Task<string> ResolveSeedUsernameAsync(AppDbContext db, string username)
    {
        if (!await db.Users.AnyAsync(x => x.Username.ToLower() == username.ToLowerInvariant()))
        {
            return username;
        }

        for (var suffix = 2; suffix < 100; suffix++)
        {
            var candidate = $"{username}-{suffix}";
            if (!await db.Users.AnyAsync(x => x.Username.ToLower() == candidate.ToLowerInvariant()))
            {
                return candidate;
            }
        }

        return $"{username}-{Guid.NewGuid():N}";
    }

    private static bool SeedPasswordNeedsReset(PasswordHasher<User> hasher, User user, string password)
    {
        if (string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            return true;
        }

        try
        {
            return hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Success;
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (FormatException)
        {
            return true;
        }
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

    private sealed class SeedRegionBoundaryRow
    {
        public int RegionId { get; set; }
        public string RegionName { get; set; } = string.Empty;
        public string? CountryIso3 { get; set; }
    }

    private sealed class SeedCandidateBoundaryRow
    {
        public int CandidateId { get; set; }
        public string ReleaseType { get; set; } = string.Empty;
        public string ShapeGroup { get; set; } = string.Empty;
        public string ShapeId { get; set; } = string.Empty;
        public string ShapeName { get; set; } = string.Empty;
        public string? ShapeType { get; set; }
        public string ExternalKey { get; set; } = string.Empty;
        public Geometry? BoundaryGeometry { get; set; }
        public Geometry? BoundaryDisplayGeometry { get; set; }
    }

    private sealed class SeedRegionSpatialRow
    {
        public Region Region { get; set; } = null!;
        public Geometry? BoundaryGeometry { get; set; }
    }

    private static string NormalizeKey(string value) =>
        value.Trim().ToLowerInvariant().Replace(" ", "-", StringComparison.Ordinal);
}

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Geotrack.Domain.Entities;
using Geotrack.Domain.Entities.Analytics;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Interests;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Tags;
using Geotrack.Domain.Entities.Users;
using Geotrack.Infrastructure.Search;
using Geotrack.Infrastructure.Services.Agent;
using Geotrack.Infrastructure.Spatial;
using Microsoft.EntityFrameworkCore;
using Geometry = NetTopologySuite.Geometries.Geometry;
using Point = NetTopologySuite.Geometries.Point;

namespace Geotrack.Infrastructure;

public class AppDbContext : DbContext
{
    public DbSet<Territory> Territories => Set<Territory>();
    public DbSet<AdministrativeBoundaryCandidate> AdministrativeBoundaryCandidates => Set<AdministrativeBoundaryCandidate>();
    public DbSet<Region> Regions => Set<Region>();
    public DbSet<RegionalManagerRegion> RegionalManagerRegions => Set<RegionalManagerRegion>();
    public DbSet<TerritoryManager> TerritoryManagers => Set<TerritoryManager>();
    public DbSet<TerritoryManagerTerritory> TerritoryManagerTerritories => Set<TerritoryManagerTerritory>();

    public DbSet<Destination> Destinations => Set<Destination>();
    public DbSet<DestinationType> DestinationTypes => Set<DestinationType>();

    public DbSet<Location> Locations => Set<Location>();
    public DbSet<LocationType> LocationTypes => Set<LocationType>();

    public DbSet<Place> Places => Set<Place>();
    public DbSet<PlaceType> PlaceTypes => Set<PlaceType>();
    public DbSet<Amenity> Amenities => Set<Amenity>();
    public DbSet<PlaceAmenity> PlaceAmenities => Set<PlaceAmenity>();
    public DbSet<BookingPartner> BookingPartners => Set<BookingPartner>();
    public DbSet<PlaceBookingPartner> PlaceBookingPartners => Set<PlaceBookingPartner>();
    public DbSet<PlaceContribution> PlaceContributions => Set<PlaceContribution>();
    public DbSet<PlaceContributionImage> PlaceContributionImages => Set<PlaceContributionImage>();

    public DbSet<RestaurantType> RestaurantTypes => Set<RestaurantType>();
    public DbSet<Stay> Stays => Set<Stay>();
    public DbSet<StayType> StayTypes => Set<StayType>();
    public DbSet<StayAmenity> StayAmenities => Set<StayAmenity>();
    public DbSet<StayStayAmenity> StayStayAmenities => Set<StayStayAmenity>();

    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventType> EventTypes => Set<EventType>();

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<UserTag> UserTags => Set<UserTag>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<CollectionItem> CollectionItems => Set<CollectionItem>();
    public DbSet<SupportRequest> SupportRequests => Set<SupportRequest>();
    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();
    public DbSet<AdminNotification> AdminNotifications => Set<AdminNotification>();
    public DbSet<AdminNotificationReadState> AdminNotificationReadStates => Set<AdminNotificationReadState>();
    public DbSet<UserPlace> UserPlaces => Set<UserPlace>();
    public DbSet<UserAiProviderKey> UserAiProviderKeys => Set<UserAiProviderKey>();

    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<TagType> TagTypes => Set<TagType>();
    public DbSet<DestinationTag> DestinationTags => Set<DestinationTag>();
    public DbSet<LocationTag> LocationTags => Set<LocationTag>();
    public DbSet<PlaceTag> PlaceTags => Set<PlaceTag>();
    public DbSet<EventTag> EventTags => Set<EventTag>();

    public DbSet<DestinationReview> DestinationReviews => Set<DestinationReview>();
    public DbSet<LocationReview> LocationReviews => Set<LocationReview>();
    public DbSet<PlaceReview> PlaceReviews => Set<PlaceReview>();
    public DbSet<EventReview> EventReviews => Set<EventReview>();
    public DbSet<ReviewReport> ReviewReports => Set<ReviewReport>();
    public DbSet<ModerationTerm> ModerationTerms => Set<ModerationTerm>();
    public DbSet<ModerationAuditLog> ModerationAuditLogs => Set<ModerationAuditLog>();

    public DbSet<AnalyticsEvent> AnalyticsEvents => Set<AnalyticsEvent>();
    public DbSet<AnonymousSession> AnonymousSessions => Set<AnonymousSession>();
    public DbSet<UserInterestSignal> UserInterestSignals => Set<UserInterestSignal>();

    public DbSet<InterestTopic> InterestTopics => Set<InterestTopic>();
    public DbSet<InterestTopicTag> InterestTopicTags => Set<InterestTopicTag>();
    public DbSet<UserInterestTopic> UserInterestTopics => Set<UserInterestTopic>();
    public DbSet<UserInterestTopicSignal> UserInterestTopicSignals => Set<UserInterestTopicSignal>();

    public DbSet<TagTypePlaceType> TagTypePlaceTypes => Set<TagTypePlaceType>();
    public DbSet<Restaurant> Restaurants => Set<Restaurant>();

    public DbSet<EntityImage> EntityImages => Set<EntityImage>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public override int SaveChanges()
    {
        SyncDerivedColumns();
        return base.SaveChanges();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        SyncDerivedColumns();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SyncDerivedColumns();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        SyncDerivedColumns();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasPostgresExtension("postgis");
        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder
            .HasDbFunction(typeof(AgentPostgresDbFunctions).GetMethod(nameof(AgentPostgresDbFunctions.Unaccent))!)
            .HasName("unaccent")
            .IsBuiltIn();

        ConfigureTerritories(modelBuilder);
        ConfigureAdministrativeBoundaryCandidates(modelBuilder);
        ConfigureRegions(modelBuilder);
        ConfigureRegionalManagers(modelBuilder);
        ConfigureTerritoryManagers(modelBuilder);

        ConfigureDestinations(modelBuilder);
        ConfigureLocations(modelBuilder);
        ConfigurePlaces(modelBuilder);
        ConfigureEvents(modelBuilder);

        ConfigureLookupTables(modelBuilder);
        ConfigureUsers(modelBuilder);
        ConfigureTags(modelBuilder);
        ConfigureInterests(modelBuilder);
        ConfigureReviews(modelBuilder);
        ConfigureCollections(modelBuilder);
        ConfigureSupportAndNotifications(modelBuilder);
        ConfigurePlaceContributions(modelBuilder);
        ConfigureEntityImages(modelBuilder);
        ConfigureAnalytics(modelBuilder);
    }

    private static void ConfigureTerritories(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Territory>(entity =>
        {
            entity.ToTable("Territories");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(x => x.TerritoryKind)
                .IsRequired()
                .HasMaxLength(32)
                .HasDefaultValue(TerritoryKinds.Country);

            entity.Property(x => x.CountryCode)
                .HasMaxLength(2);

            entity.Property(x => x.CountryIso3)
                .HasMaxLength(3);

            entity.Property(x => x.BoundaryShapeType)
                .HasMaxLength(32);

            entity.Property(x => x.BoundaryShapeGroup)
                .HasMaxLength(128);

            entity.Property(x => x.BoundaryShapeId)
                .HasMaxLength(128);

            entity.Property(x => x.BoundaryExternalKey)
                .HasMaxLength(300);

            entity.Property(x => x.BoundarySource)
                .HasMaxLength(32);

            entity.Property(x => x.BoundarySourceUrl)
                .HasMaxLength(1000);

            entity.Property(x => x.BoundaryAdmLevel)
                .HasMaxLength(16);

            entity.Property(x => x.BoundaryReleaseType)
                .HasMaxLength(32);

            entity.Property(x => x.BoundaryLicense)
                .HasMaxLength(200);

            entity.HasIndex(x => x.Name)
                .IsUnique();

            entity.HasIndex(x => x.CountryCode)
                .IsUnique()
                .HasFilter("\"CountryCode\" IS NOT NULL");

            entity.HasIndex(x => x.CountryIso3)
                .IsUnique()
                .HasFilter("\"CountryIso3\" IS NOT NULL");

            entity.HasIndex(x => x.BoundaryExternalKey)
                .IsUnique()
                .HasFilter("\"BoundaryExternalKey\" IS NOT NULL");

            entity.Property<Geometry?>("BoundaryGeometry")
                .HasColumnType("geometry (geometry, 4326)");

            entity.HasIndex("BoundaryGeometry")
                .HasMethod("GIST");

            entity.Property<Geometry?>("BoundaryDisplayGeometry")
                .HasColumnType("geometry (geometry, 4326)");

            entity.HasMany(x => x.Regions)
                .WithOne(x => x.Territory)
                .HasForeignKey(x => x.TerritoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureAdministrativeBoundaryCandidates(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AdministrativeBoundaryCandidate>(entity =>
        {
            entity.ToTable("AdministrativeBoundaryCandidates");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Source)
                .IsRequired()
                .HasMaxLength(32);

            entity.Property(x => x.ReleaseType)
                .IsRequired()
                .HasMaxLength(32);

            entity.Property(x => x.AdmLevel)
                .IsRequired()
                .HasMaxLength(16);

            entity.Property(x => x.ShapeGroup)
                .IsRequired()
                .HasMaxLength(128);

            entity.Property(x => x.ShapeId)
                .IsRequired()
                .HasMaxLength(128);

            entity.Property(x => x.ShapeName)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(x => x.ShapeType)
                .HasMaxLength(32);

            entity.Property(x => x.ExternalKey)
                .IsRequired()
                .HasMaxLength(300);

            entity.Property(x => x.CountryIso3)
                .IsRequired()
                .HasMaxLength(3);

            entity.Property(x => x.ImportedAt)
                .IsRequired();

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.HasOne(x => x.CountryTerritory)
                .WithMany()
                .HasForeignKey(x => x.CountryTerritoryId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(x => x.ExternalKey)
                .IsUnique();

            entity.HasIndex(x => new { x.Source, x.AdmLevel, x.ShapeGroup, x.ShapeId })
                .IsUnique();

            entity.HasIndex(x => new { x.CountryIso3, x.AdmLevel });

            entity.HasIndex(x => x.CountryTerritoryId);

            entity.Property<Geometry?>("BoundaryGeometry")
                .HasColumnType("geometry (geometry, 4326)");

            entity.HasIndex("BoundaryGeometry")
                .HasMethod("GIST");

            entity.Property<Geometry?>("BoundaryDisplayGeometry")
                .HasColumnType("geometry (geometry, 4326)");
        });
    }

    private static void ConfigureRegions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Region>(entity =>
        {
            entity.ToTable("Regions");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(x => x.BoundaryShapeType)
                .HasMaxLength(32);

            entity.Property(x => x.BoundaryShapeGroup)
                .HasMaxLength(128);

            entity.Property(x => x.BoundaryShapeId)
                .HasMaxLength(128);

            entity.Property(x => x.BoundaryExternalKey)
                .HasMaxLength(300);

            entity.Property(x => x.BoundarySource)
                .HasMaxLength(32);

            entity.Property(x => x.BoundaryAdmLevel)
                .HasMaxLength(16);

            entity.Property(x => x.BoundaryReleaseType)
                .HasMaxLength(32);

            entity.Property(x => x.BoundaryLicense)
                .HasMaxLength(200);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.HasOne(x => x.Territory)
                .WithMany(x => x.Regions)
                .HasForeignKey(x => x.TerritoryId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.BoundaryCandidate)
                .WithMany()
                .HasForeignKey(x => x.BoundaryCandidateId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(x => x.TerritoryId);

            entity.HasIndex(x => new { x.TerritoryId, x.Name })
                .IsUnique();

            entity.HasIndex(x => new { x.TerritoryId, x.BoundaryExternalKey })
                .IsUnique()
                .HasFilter("\"BoundaryExternalKey\" IS NOT NULL");

            entity.HasIndex(x => new { x.TerritoryId, x.BoundaryCandidateId })
                .IsUnique()
                .HasFilter("\"BoundaryCandidateId\" IS NOT NULL");

            entity.HasIndex(x => x.BoundaryCandidateId);

            entity.HasIndex(x => x.CreatedByUserId);

            entity.Property<Geometry?>("BoundaryGeometry")
                .HasColumnType("geometry (geometry, 4326)");

            entity.HasIndex("BoundaryGeometry")
                .HasMethod("GIST");

            entity.Property<Geometry?>("BoundaryDisplayGeometry")
                .HasColumnType("geometry (geometry, 4326)");
        });
    }

    private static void ConfigureRegionalManagers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RegionalManagerRegion>(entity =>
        {
            entity.ToTable("RegionalManagerRegions");

            entity.HasKey(x => new { x.UserId, x.RegionId });

            entity.Property(x => x.IsActive)
                .IsRequired()
                .HasDefaultValue(true);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.HasOne(x => x.User)
                .WithMany(x => x.RegionalManagerRegions)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Region property is currently named RegionalManagerRegions in your entity,
            // even though it contains RegionalManagerRegion rows.
            entity.HasOne(x => x.Region)
                .WithMany(x => x.RegionalManagerRegions)
                .HasForeignKey(x => x.RegionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.RegionId);
        });
    }

    private static void ConfigureTerritoryManagers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TerritoryManager>(entity =>
        {
            entity.ToTable("TerritoryManagers");

            entity.HasKey(x => x.UserId);

            entity.HasOne(x => x.User)
                .WithOne(x => x.TerritoryManager)
                .HasForeignKey<TerritoryManager>(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TerritoryManagerTerritory>(entity =>
        {
            entity.ToTable("TerritoryManagerTerritories");

            entity.HasKey(x => new { x.UserId, x.TerritoryId });

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.Property(x => x.RegionSetupCompletedAt);

            entity.HasOne(x => x.TerritoryManager)
                .WithMany(x => x.TerritoryManagerTerritories)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Territory)
                .WithMany(x => x.TerritoryManagerTerritories)
                .HasForeignKey(x => x.TerritoryId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.TerritoryId);
        });
    }

    private static void ConfigureDestinations(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Destination>(entity =>
        {
            entity.ToTable("Destinations");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(x => x.SearchText)
                .IsRequired()
                .HasMaxLength(12000)
                .HasDefaultValue(string.Empty);

            entity.Property(x => x.Description)
                .HasMaxLength(8000);

            entity.Property(x => x.Summary)
                .HasMaxLength(160);

            entity.Property(x => x.InternalReferenceId)
                .HasMaxLength(32);

            entity.Property(x => x.Status)
                .IsRequired()
                .HasMaxLength(32)
                .HasDefaultValue("Published");

            entity.Property(x => x.BoundaryNote)
                .HasMaxLength(500);

            entity.Property(x => x.BoundaryShapeType)
                .HasMaxLength(32);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.HasOne(x => x.DestinationType)
                .WithMany(x => x.Destinations)
                .HasForeignKey(x => x.DestinationTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Region)
                .WithMany(x => x.Destinations)
                .HasForeignKey(x => x.RegionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => x.DestinationTypeId);
            entity.HasIndex(x => x.RegionId);
            entity.HasIndex(x => x.CreatedByUserId);
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.InternalReferenceId)
                .IsUnique()
                .HasFilter("\"InternalReferenceId\" IS NOT NULL");

            entity.Property<Point?>(SpatialPointFactory.CoordinatesPropertyName)
                .HasColumnType("geography (point)");

            entity.HasIndex(SpatialPointFactory.CoordinatesPropertyName)
                .HasMethod("GIST");

            entity.Property<Geometry?>("BoundaryGeometry")
                .HasColumnType("geography (polygon)");

            entity.HasIndex("BoundaryGeometry")
                .HasMethod("GIST");
        });
    }

    private static void ConfigureLocations(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Location>(entity =>
        {
            entity.ToTable("Locations");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(x => x.SearchText)
                .IsRequired()
                .HasMaxLength(12000)
                .HasDefaultValue(string.Empty);

            entity.Property(x => x.Description)
                .HasMaxLength(8000);

            entity.Property(x => x.Summary)
                .HasMaxLength(160);

            entity.Property(x => x.InternalReferenceId)
                .HasMaxLength(32);

            entity.Property(x => x.Status)
                .IsRequired()
                .HasMaxLength(32)
                .HasDefaultValue("Published");

            entity.Property(x => x.BoundaryNote)
                .HasMaxLength(500);

            entity.Property(x => x.BoundaryShapeType)
                .HasMaxLength(32);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.HasOne(x => x.Destination)
                .WithMany(x => x.Locations)
                .HasForeignKey(x => x.DestinationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.LocationType)
                .WithMany(x => x.Locations)
                .HasForeignKey(x => x.LocationTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => x.DestinationId);
            entity.HasIndex(x => x.LocationTypeId);
            entity.HasIndex(x => x.CreatedByUserId);
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.InternalReferenceId)
                .IsUnique()
                .HasFilter("\"InternalReferenceId\" IS NOT NULL");

            entity.Property<Point?>(SpatialPointFactory.CoordinatesPropertyName)
                .HasColumnType("geography (point)");

            entity.HasIndex(SpatialPointFactory.CoordinatesPropertyName)
                .HasMethod("GIST");

            entity.Property<Geometry?>("BoundaryGeometry")
                .HasColumnType("geography (polygon)");

            entity.HasIndex("BoundaryGeometry")
                .HasMethod("GIST");
        });
    }

    private static void ConfigurePlaces(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Place>(entity =>
        {
            entity.ToTable("Places");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(x => x.SearchText)
                .IsRequired()
                .HasMaxLength(12000)
                .HasDefaultValue(string.Empty);

            entity.Property(x => x.Subtitle)
                .HasMaxLength(500);

            entity.Property(x => x.InternalReferenceId)
                .HasMaxLength(32);

            entity.Property(x => x.Status)
                .IsRequired()
                .HasMaxLength(32)
                .HasDefaultValue("Published");

            entity.Property(x => x.Address)
                .HasMaxLength(500);

            entity.Property(x => x.OpeningHours)
                .HasMaxLength(200);

            entity.Property(x => x.OperatingHoursEnabled)
                .HasDefaultValue(false);

            entity.Property(x => x.OperatingHoursJson)
                .HasColumnType("jsonb");

            entity.Property(x => x.Description)
                .HasMaxLength(2000);

            entity.Property(x => x.ContactEmail)
                .HasMaxLength(200);

            entity.Property(x => x.Number)
                .HasMaxLength(50);

            entity.Property(x => x.Website)
                .HasMaxLength(500);

            entity.Property(x => x.AdmissionPriceRange)
                .HasMaxLength(200);

            entity.Property(x => x.BookingExternalLink)
                .HasMaxLength(500);

            entity.Property(x => x.BestTimeToVisit)
                .HasMaxLength(500);

            entity.Property(x => x.VisitorTips)
                .HasMaxLength(1000);

            entity.Property(x => x.AccessNotes)
                .HasMaxLength(1000);

            entity.Property(x => x.AccessibilityNotes)
                .HasMaxLength(1000);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.HasOne(x => x.PlaceType)
                .WithMany(x => x.Places)
                .HasForeignKey(x => x.PlaceTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Location)
                .WithMany(x => x.Places)
                .HasForeignKey(x => x.LocationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.PlaceTypeId);
            entity.HasIndex(x => x.LocationId);
            entity.HasIndex(x => x.CreatedByUserId);
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.InternalReferenceId)
                .IsUnique()
                .HasFilter("\"InternalReferenceId\" IS NOT NULL");

            entity.Property<Point?>(SpatialPointFactory.CoordinatesPropertyName)
                .HasColumnType("geography (point)");

            entity.HasIndex(SpatialPointFactory.CoordinatesPropertyName)
                .HasMethod("GIST");
        });

        modelBuilder.Entity<Amenity>(entity =>
        {
            entity.ToTable("Amenities");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.Icon)
                .HasMaxLength(50);

            entity.HasIndex(x => x.Name)
                .IsUnique();
        });

        modelBuilder.Entity<PlaceAmenity>(entity =>
        {
            entity.ToTable("PlaceAmenities");

            entity.HasKey(x => new { x.PlaceId, x.AmenityId });

            entity.HasOne(x => x.Place)
                .WithMany(x => x.PlaceAmenities)
                .HasForeignKey(x => x.PlaceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Amenity)
                .WithMany(x => x.PlaceAmenities)
                .HasForeignKey(x => x.AmenityId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.AmenityId);
        });

        modelBuilder.Entity<BookingPartner>(entity =>
        {
            entity.ToTable("BookingPartners");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.Variant)
                .IsRequired()
                .HasMaxLength(20);

            entity.HasIndex(x => x.Name)
                .IsUnique();
        });

        modelBuilder.Entity<PlaceBookingPartner>(entity =>
        {
            entity.ToTable("PlaceBookingPartners");

            entity.HasKey(x => new { x.PlaceId, x.BookingPartnerId });

            entity.Property(x => x.Url)
                .HasMaxLength(500);

            entity.HasOne(x => x.Place)
                .WithMany(x => x.PlaceBookingPartners)
                .HasForeignKey(x => x.PlaceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.BookingPartner)
                .WithMany(x => x.PlaceBookingPartners)
                .HasForeignKey(x => x.BookingPartnerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.BookingPartnerId);
        });

        modelBuilder.Entity<Restaurant>(entity =>
        {
            entity.ToTable("Restaurants");

            entity.HasKey(x => x.PlaceId);

            entity.HasOne(x => x.Place)
                .WithOne()
                .HasForeignKey<Restaurant>(x => x.PlaceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Stay>(entity =>
        {
            entity.ToTable("Stays");

            // Shared primary key: Stays.PlaceId is both PK and FK to Places.Id.
            entity.HasKey(x => x.PlaceId);

            entity.Property(x => x.PricePerNight)
                .HasColumnType("decimal(10,2)");

            entity.Property(x => x.CheckInTime)
                .HasMaxLength(20);

            entity.Property(x => x.CheckOutTime)
                .HasMaxLength(20);

            entity.HasOne(x => x.Place)
                .WithOne(x => x.Stay)
                .HasForeignKey<Stay>(x => x.PlaceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.StayType)
                .WithMany(x => x.Stays)
                .HasForeignKey(x => x.StayTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => x.StayTypeId);
        });

        modelBuilder.Entity<StayStayAmenity>(entity =>
        {
            entity.ToTable("StayStayAmenities");

            entity.HasKey(x => new { x.StayPlaceId, x.StayAmenityId });

            entity.HasOne(x => x.Stay)
                .WithMany(x => x.StayAmenities)
                .HasForeignKey(x => x.StayPlaceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.StayAmenity)
                .WithMany(x => x.StayAmenities)
                .HasForeignKey(x => x.StayAmenityId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.StayAmenityId);
        });
    }

    private static void ConfigureEvents(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Event>(entity =>
        {
            entity.ToTable("Events");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(x => x.SearchText)
                .IsRequired()
                .HasMaxLength(12000)
                .HasDefaultValue(string.Empty);

            entity.Property(x => x.Summary)
                .HasMaxLength(160);

            entity.Property(x => x.InternalReferenceId)
                .HasMaxLength(50);

            entity.Property(x => x.Status)
                .IsRequired()
                .HasMaxLength(40);

            entity.Property(x => x.Description)
                .HasMaxLength(8000);

            entity.Property(x => x.ParentEntityType)
                .IsRequired()
                .HasMaxLength(40);

            entity.Property(x => x.VenueName)
                .HasMaxLength(200);

            entity.Property(x => x.TicketPrice)
                .HasMaxLength(100);

            entity.Property(x => x.ExternalTicketLink)
                .HasMaxLength(500);

            entity.Property(x => x.Duration)
                .HasMaxLength(100);

            entity.Property(x => x.HostedBy)
                .HasMaxLength(200);

            entity.Property(x => x.ContactEmail)
                .HasMaxLength(200);

            entity.Property(x => x.AgeRestriction)
                .HasMaxLength(100);

            entity.Property(x => x.VenueDescription)
                .HasMaxLength(2000);

            entity.Property(x => x.ArrivalTips)
                .HasMaxLength(2000);

            entity.Property(x => x.Accessibility)
                .HasMaxLength(1000);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.HasOne(x => x.EventType)
                .WithMany(x => x.Events)
                .HasForeignKey(x => x.EventTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Destination)
                .WithMany(x => x.Events)
                .HasForeignKey(x => x.DestinationId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Location)
                .WithMany(x => x.Events)
                .HasForeignKey(x => x.LocationId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Place)
                .WithMany(x => x.Events)
                .HasForeignKey(x => x.PlaceId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(x => x.EventTypeId);
            entity.HasIndex(x => x.DestinationId);
            entity.HasIndex(x => x.LocationId);
            entity.HasIndex(x => x.PlaceId);
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.InternalReferenceId)
                .IsUnique()
                .HasFilter("\"InternalReferenceId\" IS NOT NULL");

            entity.Property<Point?>(SpatialPointFactory.CoordinatesPropertyName)
                .HasColumnType("geography (point)");

            entity.HasIndex(SpatialPointFactory.CoordinatesPropertyName)
                .HasMethod("GIST");
        });
    }

    private static void ConfigureLookupTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DestinationType>(entity =>
        {
            entity.ToTable("DestinationTypes");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.Description)
                .HasMaxLength(1000);

            entity.HasIndex(x => x.Name)
                .IsUnique();
        });

        modelBuilder.Entity<LocationType>(entity =>
        {
            entity.ToTable("LocationTypes");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.Description)
                .HasMaxLength(1000);

            entity.HasIndex(x => x.Name)
                .IsUnique();
        });

        modelBuilder.Entity<PlaceType>(entity =>
        {
            entity.ToTable("PlaceTypes");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.Description)
                .HasMaxLength(1000);

            entity.Property(x => x.IsOwnable)
                .HasDefaultValue(false);

            entity.HasIndex(x => x.Name)
                .IsUnique();
        });

        modelBuilder.Entity<EventType>(entity =>
        {
            entity.ToTable("EventTypes");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.Description)
                .HasMaxLength(1000);

            entity.HasIndex(x => x.Name)
                .IsUnique();
        });

        modelBuilder.Entity<RestaurantType>(entity =>
        {
            entity.ToTable("RestaurantTypes");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.Slug)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.Description)
                .HasMaxLength(500);

            entity.HasIndex(x => x.Name)
                .IsUnique();

            entity.HasIndex(x => x.Slug)
                .IsUnique();
        });

        modelBuilder.Entity<StayType>(entity =>
        {
            entity.ToTable("StayTypes");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.Slug)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.Description)
                .HasMaxLength(500);

            entity.HasIndex(x => x.Name)
                .IsUnique();

            entity.HasIndex(x => x.Slug)
                .IsUnique();
        });

        modelBuilder.Entity<StayAmenity>(entity =>
        {
            entity.ToTable("StayAmenities");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.Slug)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.Description)
                .HasMaxLength(500);

            entity.Property(x => x.IconKey)
                .HasMaxLength(100);

            entity.HasIndex(x => x.Name)
                .IsUnique();

            entity.HasIndex(x => x.Slug)
                .IsUnique();
        });
    }

    private static void ConfigureUsers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("Roles");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.Description)
                .HasMaxLength(1000);

            entity.HasIndex(x => x.Name)
                .IsUnique();
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Username)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.Email)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(x => x.FirstName)
                .HasMaxLength(100);

            entity.Property(x => x.LastName)
                .HasMaxLength(100);

            entity.Property(x => x.PasswordHash)
                .HasMaxLength(500);

            entity.Property(x => x.PhoneNumber)
                .HasMaxLength(50);

            entity.Property(x => x.AvatarUrl)
                .HasMaxLength(1000);

            entity.Property(x => x.DisplayName)
                .HasMaxLength(100);

            entity.Property(x => x.ShortBio)
                .HasMaxLength(300);

            entity.Property(x => x.Location)
                .HasMaxLength(200);

            entity.Property(x => x.LastLoginAt);

            entity.Property(x => x.IsSuspended)
                .HasDefaultValue(false);

            entity.Property(x => x.SuspendedAt);

            entity.Property(x => x.SuspendedByUserId);

            entity.Property(x => x.IsDeleted)
                .HasDefaultValue(false);

            entity.Property(x => x.DeletedAt);

            entity.Property(x => x.DeletedByUserId);

            entity.Property(x => x.InterfaceLanguage)
                .IsRequired()
                .HasMaxLength(10)
                .HasDefaultValue("en-US");

            entity.Property(x => x.Timezone)
                .IsRequired()
                .HasMaxLength(100)
                .HasDefaultValue("UTC");

            entity.Property(x => x.NeedsInterestsOnboarding)
                .HasDefaultValue(false);

            entity.Property(x => x.GeneralNotificationsEnabled)
                .HasDefaultValue(true);

            entity.Property(x => x.TravelRecommendationsEnabled)
                .HasDefaultValue(true);

            entity.Property(x => x.NearbyEventsEnabled)
                .HasDefaultValue(true);

            entity.Property(x => x.CollectionUpdatesEnabled)
                .HasDefaultValue(true);

            entity.Property(x => x.EmailNotificationsEnabled)
                .HasDefaultValue(false);

            entity.Property(x => x.EmailRequestsEnabled)
                .HasDefaultValue(true);

            entity.Property(x => x.EmailNewReviewEnabled)
                .HasDefaultValue(true);

            entity.Property(x => x.AnalyticsEnabled)
                .HasDefaultValue(true);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.HasIndex(x => x.Username)
                .IsUnique();

            entity.HasIndex(x => x.Email)
                .IsUnique();

            entity.HasIndex(x => new { x.IsDeleted, x.IsSuspended });
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.ToTable("UserRoles");

            entity.HasKey(x => new { x.UserId, x.RoleId });

            entity.HasOne(x => x.User)
                .WithMany(x => x.UserRoles)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Role)
                .WithMany(x => x.UserRoles)
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.RoleId);
        });

        modelBuilder.Entity<UserPlace>(entity =>
        {
            entity.ToTable("UserPlaces");

            entity.HasKey(x => new { x.UserId, x.PlaceId });

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.HasOne(x => x.User)
                .WithMany(x => x.UserPlaces)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Place)
                .WithMany(x => x.UserPlaces)
                .HasForeignKey(x => x.PlaceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.PlaceId);
        });

        modelBuilder.Entity<UserAiProviderKey>(entity =>
        {
            entity.ToTable("UserAiProviderKeys");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Provider)
                .IsRequired()
                .HasMaxLength(40);

            entity.Property(x => x.EncryptedApiKey)
                .IsRequired()
                .HasMaxLength(4000);

            entity.Property(x => x.KeyLast4)
                .IsRequired()
                .HasMaxLength(8);

            entity.Property(x => x.DisplayName)
                .HasMaxLength(120);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.Property(x => x.UpdatedAt)
                .IsRequired();

            entity.Property(x => x.IsActive)
                .HasDefaultValue(true);

            entity.Property(x => x.FailedUseCount)
                .HasDefaultValue(0);

            entity.HasOne(x => x.User)
                .WithMany(x => x.AiProviderKeys)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.UserId, x.Provider });
            entity.HasIndex(x => new { x.UserId, x.Provider, x.IsActive })
                .IsUnique()
                .HasFilter("\"IsActive\" = true");
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("RefreshTokens");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.TokenHash)
                .IsRequired()
                .HasMaxLength(64);

            entity.Property(x => x.IsPersistent)
                .IsRequired();

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.Property(x => x.ExpiresAt)
                .IsRequired();

            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.TokenHash)
                .IsUnique();

            entity.HasIndex(x => new { x.UserId, x.IsRevoked, x.ExpiresAt });
        });

        modelBuilder.Entity<Favorite>(entity =>
        {
            entity.ToTable("Favorites");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.EntityType)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.UserId, x.EntityType, x.EntityId })
                .IsUnique();
        });
    }

    private static void ConfigureTags(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TagType>(entity =>
        {
            entity.ToTable("TagTypes");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.Slug)
                .IsRequired()
                .HasMaxLength(120);

            entity.HasIndex(x => x.Slug)
                .IsUnique();

            entity.Property(x => x.Description)
                .HasMaxLength(500);

            entity.Property(x => x.IsFilterable)
                .IsRequired()
                .HasDefaultValue(false);

            entity.Property(x => x.FilterLabel)
                .HasMaxLength(100);

            entity.Property(x => x.FilterOrder)
                .IsRequired()
                .HasDefaultValue(0);

            entity.HasIndex(x => new { x.IsFilterable, x.FilterOrder });

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.HasMany(x => x.Tags)
                .WithOne(x => x.TagType)
                .HasForeignKey(x => x.TagTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(x => x.PlaceTypes)
                .WithOne(x => x.TagType)
                .HasForeignKey(x => x.TagTypeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Tag>(entity =>
        {
            entity.ToTable("Tags");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.Slug)
                .IsRequired()
                .HasMaxLength(120);

            entity.HasIndex(x => x.Slug)
                .IsUnique();

            entity.Property(x => x.Description)
                .HasMaxLength(500);

            entity.Property(x => x.ImageUrl)
                .HasMaxLength(500);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.HasOne(x => x.TagType)
                .WithMany(x => x.Tags)
                .HasForeignKey(x => x.TagTypeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TagTypePlaceType>(entity =>
        {
            entity.ToTable("TagTypePlaceTypes");

            entity.HasKey(x => new { x.TagTypeId, x.PlaceTypeId });

            entity.HasOne(x => x.TagType)
                .WithMany(x => x.PlaceTypes)
                .HasForeignKey(x => x.TagTypeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.PlaceType)
                .WithMany(x => x.TagTypes)
                .HasForeignKey(x => x.PlaceTypeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.PlaceTypeId);
        });

        modelBuilder.Entity<DestinationTag>(entity =>
        {
            entity.ToTable("DestinationTags");

            entity.HasKey(x => new { x.DestinationId, x.TagId });

            entity.HasOne(x => x.Destination)
                .WithMany(x => x.DestinationTags)
                .HasForeignKey(x => x.DestinationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Tag)
                .WithMany(x => x.DestinationTags)
                .HasForeignKey(x => x.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LocationTag>(entity =>
        {
            entity.ToTable("LocationTags");

            entity.HasKey(x => new { x.LocationId, x.TagId });

            entity.HasOne(x => x.Location)
                .WithMany(x => x.LocationTags)
                .HasForeignKey(x => x.LocationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Tag)
                .WithMany(x => x.LocationTags)
                .HasForeignKey(x => x.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlaceTag>(entity =>
        {
            entity.ToTable("PlaceTags");

            entity.HasKey(x => new { x.PlaceId, x.TagId });

            entity.HasOne(x => x.Place)
                .WithMany(x => x.PlaceTags)
                .HasForeignKey(x => x.PlaceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Tag)
                .WithMany(x => x.PlaceTags)
                .HasForeignKey(x => x.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EventTag>(entity =>
        {
            entity.ToTable("EventTags");

            entity.HasKey(x => new { x.EventId, x.TagId });

            entity.HasOne(x => x.Event)
                .WithMany(x => x.EventTags)
                .HasForeignKey(x => x.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Tag)
                .WithMany(x => x.EventTags)
                .HasForeignKey(x => x.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserTag>(entity =>
        {
            entity.ToTable("UserTags");

            entity.HasKey(x => new { x.UserId, x.TagId });

            entity.HasOne(x => x.User)
                .WithMany(x => x.UserTags)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Tag)
                .WithMany(x => x.UserTags)
                .HasForeignKey(x => x.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }


    private static void ConfigureInterests(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InterestTopic>(entity =>
        {
            entity.ToTable("InterestTopics");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(120);

            entity.Property(x => x.Slug)
                .IsRequired()
                .HasMaxLength(140);

            entity.Property(x => x.Description)
                .HasMaxLength(500);

            entity.Property(x => x.IconKey)
                .HasMaxLength(80);

            entity.Property(x => x.Group)
                .HasMaxLength(120);

            entity.Property(x => x.IsActive)
                .IsRequired()
                .HasDefaultValue(true);

            entity.Property(x => x.ShowOnOnboarding)
                .IsRequired()
                .HasDefaultValue(true);

            entity.Property(x => x.SortOrder)
                .IsRequired()
                .HasDefaultValue(0);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.HasIndex(x => x.Slug)
                .IsUnique();

            entity.HasIndex(x => new { x.IsActive, x.ShowOnOnboarding, x.SortOrder });
        });

        modelBuilder.Entity<InterestTopicTag>(entity =>
        {
            entity.ToTable("InterestTopicTags");

            entity.HasKey(x => new { x.InterestTopicId, x.TagId });

            entity.Property(x => x.Weight)
                .IsRequired()
                .HasDefaultValue(1.0);

            entity.HasOne(x => x.InterestTopic)
                .WithMany(x => x.TopicTags)
                .HasForeignKey(x => x.InterestTopicId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Tag)
                .WithMany()
                .HasForeignKey(x => x.TagId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.TagId);
        });

        modelBuilder.Entity<UserInterestTopic>(entity =>
        {
            entity.ToTable("UserInterestTopics");

            entity.HasKey(x => new { x.UserId, x.InterestTopicId });

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.HasOne(x => x.User)
                .WithMany(x => x.UserInterestTopics)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.InterestTopic)
                .WithMany(x => x.UserInterests)
                .HasForeignKey(x => x.InterestTopicId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.InterestTopicId);
        });

        modelBuilder.Entity<UserInterestTopicSignal>(entity =>
        {
            entity.ToTable("UserInterestTopicSignals");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Score)
                .IsRequired();

            entity.Property(x => x.Source)
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue("inferred");

            entity.Property(x => x.LastInteractionAt)
                .IsRequired();

            entity.Property(x => x.LastReason)
                .HasMaxLength(120);

            entity.Property(x => x.IsDismissed)
                .HasDefaultValue(false);

            entity.Property(x => x.EvidenceSinceDismissal)
                .HasDefaultValue(0.0);

            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.InterestTopic)
                .WithMany(x => x.UserSignals)
                .HasForeignKey(x => x.InterestTopicId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.UserId, x.InterestTopicId })
                .IsUnique();

            entity.HasIndex(x => new { x.UserId, x.Score });
        });
    }

    private static void ConfigureReviews(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DestinationReview>(entity =>
        {
            entity.ToTable("DestinationReviews");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Comment)
                .HasMaxLength(2000);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Destination)
                .WithMany(x => x.DestinationReviews)
                .HasForeignKey(x => x.DestinationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.UserId);
            entity.HasIndex(x => x.DestinationId);

            entity.HasIndex(x => new { x.UserId, x.DestinationId })
                .IsUnique();
        });

        modelBuilder.Entity<LocationReview>(entity =>
        {
            entity.ToTable("LocationReviews");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Comment)
                .HasMaxLength(2000);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Location)
                .WithMany(x => x.LocationReviews)
                .HasForeignKey(x => x.LocationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.UserId);
            entity.HasIndex(x => x.LocationId);

            entity.HasIndex(x => new { x.UserId, x.LocationId })
                .IsUnique();
        });

        modelBuilder.Entity<PlaceReview>(entity =>
        {
            entity.ToTable("PlaceReviews");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Comment)
                .HasMaxLength(2000);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Place)
                .WithMany(x => x.PlaceReviews)
                .HasForeignKey(x => x.PlaceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.UserId);
            entity.HasIndex(x => x.PlaceId);

            entity.HasIndex(x => new { x.UserId, x.PlaceId })
                .IsUnique();
        });

        modelBuilder.Entity<EventReview>(entity =>
        {
            entity.ToTable("EventReviews");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Comment)
                .HasMaxLength(2000);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Event)
                .WithMany(x => x.EventReviews)
                .HasForeignKey(x => x.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.UserId);
            entity.HasIndex(x => x.EventId);

            entity.HasIndex(x => new { x.UserId, x.EventId })
                .IsUnique();
        });

        modelBuilder.Entity<ReviewReport>(entity =>
        {
            entity.ToTable("ReviewReports");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.TargetType)
                .HasConversion<string>()
                .HasMaxLength(40);

            entity.Property(x => x.Reason)
                .HasConversion<string>()
                .HasMaxLength(40);

            entity.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(40);

            entity.Property(x => x.Decision)
                .HasConversion<string>()
                .HasMaxLength(40);

            entity.Property(x => x.Note)
                .HasMaxLength(1000);

            entity.Property(x => x.DecisionNote)
                .HasMaxLength(1000);

            entity.Property(x => x.EntityName)
                .HasMaxLength(200);

            entity.Property(x => x.RegionName)
                .HasMaxLength(200);

            entity.Property(x => x.ReviewAuthorName)
                .HasMaxLength(200);

            entity.Property(x => x.ReviewCommentSnapshot)
                .HasMaxLength(2000);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.HasOne(x => x.ReporterUser)
                .WithMany()
                .HasForeignKey(x => x.ReporterUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.ReviewedByUser)
                .WithMany()
                .HasForeignKey(x => x.ReviewedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.PlaceReview)
                .WithMany()
                .HasForeignKey(x => x.PlaceReviewId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.EventReview)
                .WithMany()
                .HasForeignKey(x => x.EventReviewId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(x => new { x.ReporterUserId, x.TargetType, x.ReviewId })
                .IsUnique();

            entity.HasIndex(x => new { x.TargetType, x.ReviewId });
            entity.HasIndex(x => new { x.RegionId, x.Status, x.CreatedAt });
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.PlaceReviewId);
            entity.HasIndex(x => x.EventReviewId);
            entity.HasIndex(x => x.ReviewedByUserId);
        });

        modelBuilder.Entity<ModerationTerm>(entity =>
        {
            entity.ToTable("ModerationTerms");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Term)
                .IsRequired()
                .HasMaxLength(160);

            entity.Property(x => x.NormalizedTerm)
                .IsRequired()
                .HasMaxLength(160);

            entity.Property(x => x.LanguageCode)
                .IsRequired()
                .HasMaxLength(10);

            entity.Property(x => x.Category)
                .HasConversion<string>()
                .HasMaxLength(40);

            entity.Property(x => x.MatchType)
                .HasConversion<string>()
                .HasMaxLength(40);

            entity.Property(x => x.IsActive)
                .IsRequired();

            entity.Property(x => x.Severity)
                .IsRequired();

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.HasOne(x => x.CreatedByUser)
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.UpdatedByUser)
                .WithMany()
                .HasForeignKey(x => x.UpdatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(x => new { x.LanguageCode, x.NormalizedTerm, x.Category, x.MatchType })
                .IsUnique()
                .HasDatabaseName("IX_ModerationTerms_UQ_NormalizedRule");

            entity.HasIndex(x => new { x.IsActive, x.LanguageCode });
            entity.HasIndex(x => x.Category);
            entity.HasIndex(x => x.MatchType);
            entity.HasIndex(x => x.CreatedByUserId);
            entity.HasIndex(x => x.UpdatedByUserId);
        });

        modelBuilder.Entity<ModerationAuditLog>(entity =>
        {
            entity.ToTable("ModerationAuditLogs");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.TargetType)
                .IsRequired()
                .HasMaxLength(80);

            entity.Property(x => x.Action)
                .IsRequired()
                .HasMaxLength(80);

            entity.Property(x => x.PreviousStatus)
                .HasMaxLength(80);

            entity.Property(x => x.NewStatus)
                .HasMaxLength(80);

            entity.Property(x => x.Reason)
                .HasMaxLength(1000);

            entity.Property(x => x.InternalNote)
                .HasMaxLength(2000);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.Property(x => x.CorrelationId)
                .HasMaxLength(128);

            entity.Property(x => x.SourceArea)
                .HasMaxLength(80);

            entity.Property(x => x.MetadataJson)
                .HasMaxLength(4000);

            entity.HasOne(x => x.ModeratorUser)
                .WithMany()
                .HasForeignKey(x => x.ModeratorUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => new { x.TargetType, x.TargetId, x.CreatedAt });
            entity.HasIndex(x => new { x.ModeratorUserId, x.CreatedAt });
            entity.HasIndex(x => new { x.Action, x.CreatedAt });
            entity.HasIndex(x => x.CreatedAt);
        });
    }

    private static void ConfigureCollections(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Collection>(entity =>
        {
            entity.ToTable("Collections");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(x => x.Description)
                .HasMaxLength(1000);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.HasOne(x => x.User)
                .WithMany(x => x.Collections)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CollectionItem>(entity =>
        {
            entity.ToTable("CollectionItems");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.HasOne(x => x.Collection)
                .WithMany(x => x.CollectionItems)
                .HasForeignKey(x => x.CollectionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Destination)
                .WithMany()
                .HasForeignKey(x => x.DestinationId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.Location)
                .WithMany()
                .HasForeignKey(x => x.LocationId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.Event)
                .WithMany()
                .HasForeignKey(x => x.EventId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.Place)
                .WithMany()
                .HasForeignKey(x => x.PlaceId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(x => x.CollectionId);
            entity.HasIndex(x => x.DestinationId);
            entity.HasIndex(x => x.LocationId);
            entity.HasIndex(x => x.EventId);
            entity.HasIndex(x => x.PlaceId);
        });
    }

    private static void ConfigureSupportAndNotifications(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SupportRequest>(entity =>
        {
            entity.ToTable("SupportRequests");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Category)
                .HasMaxLength(80);

            entity.Property(x => x.Subject)
                .HasMaxLength(160);

            entity.Property(x => x.Message)
                .HasMaxLength(2000);

            entity.Property(x => x.Status)
                .HasMaxLength(40);

            entity.Property(x => x.Priority)
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue(SupportRequestPriorities.Normal);

            entity.Property(x => x.PageUrl)
                .HasMaxLength(500);

            entity.Property(x => x.UserAgent)
                .HasMaxLength(500);

            entity.Property(x => x.DeviceInfo)
                .HasMaxLength(300);

            entity.Property(x => x.Browser)
                .HasMaxLength(120);

            entity.Property(x => x.Platform)
                .HasMaxLength(120);

            entity.Property(x => x.AttachmentUrl)
                .HasMaxLength(1000);

            entity.Property(x => x.InternalNote)
                .HasMaxLength(2000);

            entity.Property(x => x.ResolutionMessage)
                .HasMaxLength(2000);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.HasOne(x => x.User)
                .WithMany(x => x.SupportRequests)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.ResolvedByUser)
                .WithMany()
                .HasForeignKey(x => x.ResolvedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.AssignedAdmin)
                .WithMany()
                .HasForeignKey(x => x.AssignedAdminId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(x => new { x.UserId, x.CreatedAt });
            entity.HasIndex(x => new { x.Status, x.CreatedAt });
            entity.HasIndex(x => x.Priority);
            entity.HasIndex(x => x.ResolvedByUserId);
            entity.HasIndex(x => x.AssignedAdminId);
        });

        modelBuilder.Entity<UserNotification>(entity =>
        {
            entity.ToTable("UserNotifications");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Type)
                .IsRequired()
                .HasMaxLength(80);

            entity.Property(x => x.Title)
                .IsRequired()
                .HasMaxLength(160);

            entity.Property(x => x.Message)
                .IsRequired()
                .HasMaxLength(1000);

            entity.Property(x => x.RelatedEntityType)
                .HasMaxLength(80);

            entity.Property(x => x.ActionUrl)
                .HasMaxLength(500);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.HasOne(x => x.User)
                .WithMany(x => x.Notifications)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.UserId, x.CreatedAt });
            entity.HasIndex(x => new { x.UserId, x.ReadAt });
            entity.HasIndex(x => new { x.RelatedEntityType, x.RelatedEntityId });
        });

        modelBuilder.Entity<AdminNotification>(entity =>
        {
            entity.ToTable("AdminNotifications");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.AudienceRole)
                .IsRequired()
                .HasMaxLength(80);

            entity.Property(x => x.Type)
                .IsRequired()
                .HasMaxLength(80);

            entity.Property(x => x.Title)
                .IsRequired()
                .HasMaxLength(160);

            entity.Property(x => x.Message)
                .IsRequired()
                .HasMaxLength(1000);

            entity.Property(x => x.Severity)
                .IsRequired()
                .HasMaxLength(40);

            entity.Property(x => x.TargetType)
                .IsRequired()
                .HasMaxLength(80);

            entity.Property(x => x.TargetRoute)
                .HasMaxLength(500);

            entity.Property(x => x.ScopeType)
                .IsRequired()
                .HasMaxLength(80)
                .HasDefaultValue("Global");

            entity.Property(x => x.ScopeLabel)
                .HasMaxLength(160);

            entity.Property(x => x.MetadataJson)
                .HasColumnType("jsonb");

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.HasMany(x => x.ReadStates)
                .WithOne(x => x.AdminNotification)
                .HasForeignKey(x => x.AdminNotificationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.AudienceRole, x.CreatedAt });
            entity.HasIndex(x => new { x.AudienceRole, x.ScopeType, x.ScopeId, x.CreatedAt });
            entity.HasIndex(x => new { x.AudienceRole, x.Type, x.TargetType, x.TargetId, x.ScopeType, x.ScopeId })
                .IsUnique();
        });

        modelBuilder.Entity<AdminNotificationReadState>(entity =>
        {
            entity.ToTable("AdminNotificationReadStates");

            entity.HasKey(x => new { x.AdminNotificationId, x.UserId });

            entity.Property(x => x.ReadAt)
                .IsRequired();

            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.UserId, x.ReadAt });
        });
    }

    private static void ConfigurePlaceContributions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlaceContribution>(entity =>
        {
            entity.ToTable("PlaceContributions");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Type)
                .HasConversion<string>()
                .HasMaxLength(40)
                .IsRequired();

            entity.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(40)
                .IsRequired();

            entity.Property(x => x.OriginalSnapshotJson)
                .HasColumnType("jsonb");

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(x => x.Subtitle)
                .HasMaxLength(500);

            entity.Property(x => x.Address)
                .HasMaxLength(500);

            entity.Property(x => x.OpeningHours)
                .HasMaxLength(200);

            entity.Property(x => x.Description)
                .HasMaxLength(2000);

            entity.Property(x => x.ContactEmail)
                .HasMaxLength(200);

            entity.Property(x => x.PhoneNumber)
                .HasMaxLength(50);

            entity.Property(x => x.Website)
                .HasMaxLength(500);

            entity.Property(x => x.PricePerNight)
                .HasColumnType("decimal(10,2)");

            entity.Property(x => x.CheckInTime)
                .HasMaxLength(20);

            entity.Property(x => x.CheckOutTime)
                .HasMaxLength(20);

            entity.Property(x => x.ReviewNote)
                .HasMaxLength(1000);

            entity.Property(x => x.TagsJson)
                .HasColumnType("jsonb");

            entity.Property(x => x.AmenitiesJson)
                .HasColumnType("jsonb");

            entity.Property(x => x.StayAmenitiesJson)
                .HasColumnType("jsonb");

            entity.Property(x => x.BookingPartnersJson)
                .HasColumnType("jsonb");

            entity.Property(x => x.SubmittedAt)
                .IsRequired();

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.HasOne(x => x.User)
                .WithMany(x => x.PlaceContributions)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.ReviewedByUser)
                .WithMany()
                .HasForeignKey(x => x.ReviewedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.Place)
                .WithMany(x => x.PlaceContributions)
                .HasForeignKey(x => x.PlaceId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.PlaceType)
                .WithMany()
                .HasForeignKey(x => x.PlaceTypeId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.Location)
                .WithMany()
                .HasForeignKey(x => x.LocationId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.RestaurantType)
                .WithMany()
                .HasForeignKey(x => x.RestaurantTypeId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.StayType)
                .WithMany()
                .HasForeignKey(x => x.StayTypeId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(x => new { x.Status, x.SubmittedAt });
            entity.HasIndex(x => new { x.Type, x.Status });
            entity.HasIndex(x => x.UserId);
            entity.HasIndex(x => x.PlaceId);
            entity.HasIndex(x => x.PlaceTypeId);
            entity.HasIndex(x => x.LocationId);
            entity.HasIndex(x => x.RestaurantTypeId);
            entity.HasIndex(x => x.StayTypeId);
            entity.HasIndex(x => x.ReviewedByUserId);
        });

        modelBuilder.Entity<PlaceContributionImage>(entity =>
        {
            entity.ToTable("PlaceContributionImages");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Url)
                .IsRequired()
                .HasMaxLength(1000);

            entity.Property(x => x.AltText)
                .HasMaxLength(300);

            entity.Property(x => x.Caption)
                .HasMaxLength(500);

            entity.Property(x => x.ImageRole)
                .HasMaxLength(20)
                .HasDefaultValue("gallery")
                .IsRequired();

            entity.Property(x => x.ImagePosition)
                .HasMaxLength(40);

            entity.Property(x => x.ImageSize)
                .HasMaxLength(40);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.HasOne(x => x.PlaceContribution)
                .WithMany(x => x.Images)
                .HasForeignKey(x => x.PlaceContributionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.PlaceContributionId);

            entity.HasIndex(x => x.PlaceContributionId)
                .IsUnique()
                .HasFilter("\"IsMain\" = true");
        });
    }

    private static void ConfigureEntityImages(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EntityImage>(entity =>
        {
            entity.ToTable("EntityImages");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.TargetType)
                .HasConversion<string>()
                .HasMaxLength(40)
                .IsRequired();

            entity.Property(x => x.Url)
                .IsRequired()
                .HasMaxLength(1000);

            entity.Property(x => x.AltText)
                .HasMaxLength(300);

            entity.Property(x => x.Caption)
                .HasMaxLength(500);

            entity.Property(x => x.ImageRole)
                .HasMaxLength(20)
                .HasDefaultValue("gallery")
                .IsRequired();

            entity.Property(x => x.ImagePosition)
                .HasMaxLength(40);

            entity.Property(x => x.ImageSize)
                .HasMaxLength(40);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.HasOne(x => x.Place)
                .WithMany(x => x.Images)
                .HasForeignKey(x => x.PlaceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Event)
                .WithMany(x => x.Images)
                .HasForeignKey(x => x.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Region)
                .WithMany(x => x.Images)
                .HasForeignKey(x => x.RegionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Destination)
                .WithMany(x => x.Images)
                .HasForeignKey(x => x.DestinationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Territory)
                .WithMany(x => x.Images)
                .HasForeignKey(x => x.TerritoryId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Location)
                .WithMany(x => x.Images)
                .HasForeignKey(x => x.LocationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.PlaceId);
            entity.HasIndex(x => x.EventId);
            entity.HasIndex(x => x.RegionId);
            entity.HasIndex(x => x.DestinationId);
            entity.HasIndex(x => x.TerritoryId);
            entity.HasIndex(x => x.LocationId);

            entity.HasIndex(x => x.PlaceId)
                .IsUnique()
                .HasFilter("\"PlaceId\" IS NOT NULL AND \"IsMain\" = true");

            entity.HasIndex(x => x.EventId)
                .IsUnique()
                .HasFilter("\"EventId\" IS NOT NULL AND \"IsMain\" = true");

            entity.HasIndex(x => x.RegionId)
                .IsUnique()
                .HasFilter("\"RegionId\" IS NOT NULL AND \"IsMain\" = true");

            entity.HasIndex(x => x.DestinationId)
                .IsUnique()
                .HasFilter("\"DestinationId\" IS NOT NULL AND \"IsMain\" = true");

            entity.HasIndex(x => x.TerritoryId)
                .IsUnique()
                .HasFilter("\"TerritoryId\" IS NOT NULL AND \"IsMain\" = true");

            entity.HasIndex(x => x.LocationId)
                .IsUnique()
                .HasFilter("\"LocationId\" IS NOT NULL AND \"IsMain\" = true");
        });
    }

    private static void ConfigureAnalytics(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AnonymousSession>(entity =>
        {
            entity.ToTable("AnonymousSessions");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.Property(x => x.LastSeenAt)
                .IsRequired();

            entity.Property(x => x.EntryRoute)
                .HasMaxLength(300);

            entity.Property(x => x.CoarseRegion)
                .HasMaxLength(120);

            entity.Property(x => x.CoarseCountry)
                .HasMaxLength(120);

            entity.Property(x => x.UserAgentFamily)
                .IsRequired()
                .HasMaxLength(40)
                .HasDefaultValue("other");

            entity.HasIndex(x => x.LastSeenAt);
        });

        modelBuilder.Entity<AnalyticsEvent>(entity =>
        {
            entity.ToTable("AnalyticsEvents");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.EventType)
                .IsRequired()
                .HasMaxLength(80);

            entity.Property(x => x.EntityType)
                .HasMaxLength(40);

            entity.Property(x => x.Route)
                .HasMaxLength(300);

            entity.Property(x => x.DurationMs);

            entity.Property(x => x.MetadataJson)
                .IsRequired()
                .HasColumnType("jsonb")
                .HasDefaultValue("{}");

            entity.Property(x => x.OccurredAt)
                .IsRequired();

            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.AnonymousSession)
                .WithMany()
                .HasForeignKey(x => x.AnonymousSessionId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.Destination)
                .WithMany()
                .HasForeignKey(x => x.DestinationId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(x => x.OccurredAt);
            entity.HasIndex(x => new { x.UserId, x.OccurredAt });
            entity.HasIndex(x => new { x.AnonymousSessionId, x.OccurredAt });
            entity.HasIndex(x => new { x.EventType, x.OccurredAt });
            entity.HasIndex(x => new { x.EntityType, x.EntityId, x.OccurredAt });
            entity.HasIndex(x => new { x.DestinationId, x.OccurredAt });
        });

        modelBuilder.Entity<UserInterestSignal>(entity =>
        {
            entity.ToTable("UserInterestSignals");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Score)
                .IsRequired();

            entity.Property(x => x.Source)
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue("inferred");

            entity.Property(x => x.LastInteractionAt)
                .IsRequired();

            entity.Property(x => x.LastReason)
                .HasMaxLength(80);

            entity.Property(x => x.IsDismissed)
                .HasDefaultValue(false);

            entity.Property(x => x.EvidenceSinceDismissal)
                .HasDefaultValue(0.0);

            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Tag)
                .WithMany()
                .HasForeignKey(x => x.TagId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.UserId, x.TagId })
                .IsUnique();

            entity.HasIndex(x => new { x.UserId, x.Score });
        });

    }

    private void SyncDerivedColumns()
    {
        foreach (var entry in ChangeTracker.Entries<Destination>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.SearchText = SearchTextNormalizer.Normalize(
                    entry.Entity.Name,
                    entry.Entity.Summary,
                    entry.Entity.Description,
                    entry.Entity.InternalReferenceId);
                entry.Property<Point?>(SpatialPointFactory.CoordinatesPropertyName).CurrentValue =
                    SpatialPointFactory.Create(entry.Entity.Latitude, entry.Entity.Longitude);
            }
        }

        foreach (var entry in ChangeTracker.Entries<Location>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.SearchText = SearchTextNormalizer.Normalize(
                    entry.Entity.Name,
                    entry.Entity.Summary,
                    entry.Entity.Description,
                    entry.Entity.InternalReferenceId);
                entry.Property<Point?>(SpatialPointFactory.CoordinatesPropertyName).CurrentValue =
                    SpatialPointFactory.Create(entry.Entity.Latitude, entry.Entity.Longitude);
            }
        }

        foreach (var entry in ChangeTracker.Entries<Place>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.SearchText = SearchTextNormalizer.Normalize(
                    entry.Entity.Name,
                    entry.Entity.Subtitle,
                    entry.Entity.Address,
                    entry.Entity.Description,
                    entry.Entity.InternalReferenceId);
                entry.Property<Point?>(SpatialPointFactory.CoordinatesPropertyName).CurrentValue =
                    SpatialPointFactory.Create(entry.Entity.Latitude, entry.Entity.Longitude);
            }
        }

        foreach (var entry in ChangeTracker.Entries<Event>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            entry.Entity.SearchText = SearchTextNormalizer.Normalize(
                entry.Entity.Name,
                entry.Entity.Summary,
                entry.Entity.Description,
                entry.Entity.VenueName,
                entry.Entity.InternalReferenceId);
            entry.Property<Point?>(SpatialPointFactory.CoordinatesPropertyName).CurrentValue =
                ResolveEventCoordinates(entry.Entity);
        }
    }

    private Point? ResolveEventCoordinates(Event entity)
    {
        if (entity.Place is not null)
        {
            return SpatialPointFactory.Create(entity.Place.Latitude, entity.Place.Longitude);
        }

        if (entity.Location is not null)
        {
            return SpatialPointFactory.Create(entity.Location.Latitude, entity.Location.Longitude);
        }

        if (entity.Destination is not null)
        {
            return SpatialPointFactory.Create(entity.Destination.Latitude, entity.Destination.Longitude);
        }

        if (entity.PlaceId.HasValue)
        {
            var localPlace = Places.Local.FirstOrDefault(place => place.Id == entity.PlaceId.Value);
            if (localPlace is not null)
            {
                return SpatialPointFactory.Create(localPlace.Latitude, localPlace.Longitude);
            }
        }

        if (entity.LocationId.HasValue)
        {
            var localLocation = Locations.Local.FirstOrDefault(location => location.Id == entity.LocationId.Value);
            if (localLocation is not null)
            {
                return SpatialPointFactory.Create(localLocation.Latitude, localLocation.Longitude);
            }
        }

        var localDestination = Destinations.Local.FirstOrDefault(destination => destination.Id == entity.DestinationId);
        if (localDestination is not null)
        {
            return SpatialPointFactory.Create(localDestination.Latitude, localDestination.Longitude);
        }

        return null;
    }
}

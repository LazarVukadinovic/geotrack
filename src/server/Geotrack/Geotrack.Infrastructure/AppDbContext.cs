using System;
using Geotrack.Domain.Entities;
using Geotrack.Domain.Entities.Core;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Tags;
using Geotrack.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure;

public class AppDbContext : DbContext
{
    public DbSet<Region> Regions => Set<Region>();
    public DbSet<RegionalOrganization> RegionalOrganizations => Set<RegionalOrganization>();
    public DbSet<RegionalOrganizationRegion> RegionalOrganizationRegions => Set<RegionalOrganizationRegion>();

    public DbSet<Destination> Destinations => Set<Destination>();
    public DbSet<DestinationType> DestinationTypes => Set<DestinationType>();

    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventType> EventTypes => Set<EventType>();

    public DbSet<Location> Locations => Set<Location>();
    public DbSet<LocationType> LocationTypes => Set<LocationType>();

    public DbSet<Place> Places => Set<Place>();
    public DbSet<PlaceType> PlaceTypes => Set<PlaceType>();
    public DbSet<Amenity> Amenities => Set<Amenity>();
    public DbSet<PlaceAmenity> PlaceAmenities => Set<PlaceAmenity>();
    public DbSet<BookingPartner> BookingPartners => Set<BookingPartner>();
    public DbSet<PlaceBookingPartner> PlaceBookingPartners => Set<PlaceBookingPartner>();

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<UserTag> UserTags => Set<UserTag>();
    public DbSet<Favorite> Favorites => Set<Favorite>();

    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<DestinationTag> DestinationTags => Set<DestinationTag>();
    public DbSet<LocationTag> LocationTags => Set<LocationTag>();
    public DbSet<PlaceTag> PlaceTags => Set<PlaceTag>();
    public DbSet<EventTag> EventTags => Set<EventTag>();

    public DbSet<DestinationReview> DestinationReviews => Set<DestinationReview>();
    public DbSet<LocationReview> LocationReviews => Set<LocationReview>();
    public DbSet<PlaceReview> PlaceReviews => Set<PlaceReview>();
    public DbSet<EventReview> EventReviews => Set<EventReview>();

    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<CollectionItem> CollectionItems => Set<CollectionItem>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Region>(entity =>
        {
            entity.ToTable("Regions");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .HasMaxLength(200);

            entity.HasIndex(x => x.Name)
                .IsUnique();
        });

        modelBuilder.Entity<RegionalOrganization>(entity =>
        {
            entity.ToTable("RegionalOrganizations");

            entity.HasKey(x => x.UserId);

            entity.HasOne(x => x.User)
                .WithOne(x => x.RegionalOrganization)
                .HasForeignKey<RegionalOrganization>(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RegionalOrganizationRegion>(entity =>
        {
            entity.ToTable("RegionalOrganizationRegions");

            entity.HasKey(x => new { x.UserId, x.RegionId });

            entity.HasOne(x => x.RegionalOrganization)
                .WithMany(x => x.RegionalOrganizationRegions)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Region)
                .WithMany(x => x.RegionalOrganizationRegions)
                .HasForeignKey(x => x.RegionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.RegionId);
        });

        modelBuilder.Entity<Destination>(entity =>
        {
            entity.ToTable("Destinations");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .HasMaxLength(200);

            entity.Property(x => x.Description)
                .HasMaxLength(2000);

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
        });

        modelBuilder.Entity<DestinationType>(entity =>
        {
            entity.ToTable("DestinationTypes");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .HasMaxLength(100);

            entity.Property(x => x.Description)
                .HasMaxLength(1000);

            entity.HasIndex(x => x.Name)
                .IsUnique();
        });

        modelBuilder.Entity<Event>(entity =>
        {
            entity.ToTable("Events");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .HasMaxLength(200);

            entity.Property(x => x.Description)
                .HasMaxLength(2000);

            entity.HasOne(x => x.EventType)
                .WithMany(x => x.Events)
                .HasForeignKey(x => x.EventTypeId)
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
            entity.HasIndex(x => x.LocationId);
            entity.HasIndex(x => x.PlaceId);
        });

        modelBuilder.Entity<EventType>(entity =>
        {
            entity.ToTable("EventTypes");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .HasMaxLength(100);

            entity.Property(x => x.Description)
                .HasMaxLength(1000);

            entity.HasIndex(x => x.Name)
                .IsUnique();
        });

        modelBuilder.Entity<Location>(entity =>
        {
            entity.ToTable("Locations");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .HasMaxLength(200);

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
        });

        modelBuilder.Entity<LocationType>(entity =>
        {
            entity.ToTable("LocationTypes");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .HasMaxLength(100);

            entity.Property(x => x.Description)
                .HasMaxLength(1000);

            entity.HasIndex(x => x.Name)
                .IsUnique();
        });

        modelBuilder.Entity<Place>(entity =>
        {
            entity.ToTable("Places");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .HasMaxLength(200);

            entity.Property(x => x.Description)
                .HasMaxLength(2000);

            entity.Property(x => x.ContactEmail)
                .HasMaxLength(200);

            entity.Property(x => x.Number)
                .HasMaxLength(50);

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
        });

        modelBuilder.Entity<PlaceType>(entity =>
        {
            entity.ToTable("PlaceTypes");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .HasMaxLength(100);

            entity.Property(x => x.Description)
                .HasMaxLength(1000);

            entity.HasIndex(x => x.Name)
                .IsUnique();
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("Roles");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
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
                .HasMaxLength(100);

            entity.Property(x => x.Email)
                .HasMaxLength(200);

            entity.Property(x => x.FirstName)
                .HasMaxLength(100);

            entity.Property(x => x.LastName)
                .HasMaxLength(100);

            entity.Property(x => x.PasswordHash)
                .HasMaxLength(500);

            entity.Property(x => x.PhoneNumber)
                .HasMaxLength(50);

            entity.HasIndex(x => x.Username)
                .IsUnique();

            entity.HasIndex(x => x.Email)
                .IsUnique();
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

        modelBuilder.Entity<Tag>(entity =>
        {
            entity.ToTable("Tags");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .HasMaxLength(100);

            entity.Property(x => x.Slug)
                .HasMaxLength(100);

            entity.Property(x => x.Description)
                .HasMaxLength(1000);

            entity.HasOne<PlaceType>()
                .WithMany()
                .HasForeignKey(x => x.PlaceTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => x.Name)
                .IsUnique();

            entity.HasIndex(x => x.Slug)
                .IsUnique();

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

            entity.HasIndex(x => x.TagId);
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

            entity.HasIndex(x => x.TagId);
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

            entity.HasIndex(x => x.TagId);
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

            entity.HasIndex(x => x.TagId);
        });

        modelBuilder.Entity<DestinationReview>(entity =>
        {
            entity.ToTable("DestinationReviews");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Comment)
                .HasMaxLength(2000);

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
                .OnDelete(DeleteBehavior.ClientSetNull);

            entity.HasOne(x => x.Location)
                .WithMany()
                .HasForeignKey(x => x.LocationId)
                .OnDelete(DeleteBehavior.ClientSetNull);

            entity.HasOne(x => x.Event)
                .WithMany()
                .HasForeignKey(x => x.EventId)
                .OnDelete(DeleteBehavior.ClientSetNull);

            entity.HasOne(x => x.Place)
                .WithMany()
                .HasForeignKey(x => x.PlaceId)
                .OnDelete(DeleteBehavior.ClientSetNull);
        });

        modelBuilder.Entity<UserTag>(entity =>
        {
            entity.ToTable("UserTags");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.HasOne(x => x.User)
                .WithMany(x => x.UserTags)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Tag)
                .WithMany(x => x.UserTags)
                .HasForeignKey(x => x.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // New entities for frontend support

        modelBuilder.Entity<Amenity>(entity =>
        {
            entity.ToTable("Amenities");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(100);
            entity.Property(x => x.Icon).HasMaxLength(50);
            entity.HasIndex(x => x.Name).IsUnique();
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
        });

        modelBuilder.Entity<BookingPartner>(entity =>
        {
            entity.ToTable("BookingPartners");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(100);
            entity.Property(x => x.Variant).HasMaxLength(20);
            entity.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<PlaceBookingPartner>(entity =>
        {
            entity.ToTable("PlaceBookingPartners");
            entity.HasKey(x => new { x.PlaceId, x.BookingPartnerId });

            entity.HasOne(x => x.Place)
                .WithMany(x => x.PlaceBookingPartners)
                .HasForeignKey(x => x.PlaceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.BookingPartner)
                .WithMany(x => x.PlaceBookingPartners)
                .HasForeignKey(x => x.BookingPartnerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Favorite>(entity =>
        {
            entity.ToTable("Favorites");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EntityType).HasMaxLength(50);
            entity.Property(x => x.CreatedAt).IsRequired();

            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.UserId, x.EntityType, x.EntityId }).IsUnique();
        });

        // Extended field configurations for Place
        modelBuilder.Entity<Place>(entity =>
        {
            entity.Property(x => x.Subtitle).HasMaxLength(500);
            entity.Property(x => x.ImageUrl).HasMaxLength(1000);
            entity.Property(x => x.Address).HasMaxLength(500);
            entity.Property(x => x.OpeningHours).HasMaxLength(200);
            entity.Property(x => x.PricePerNight).HasColumnType("decimal(10,2)");
        });

        // Extended field configurations for Event
        modelBuilder.Entity<Event>(entity =>
        {
            entity.Property(x => x.ImageUrl).HasMaxLength(1000);
            entity.Property(x => x.TicketPrice).HasMaxLength(100);
            entity.Property(x => x.Duration).HasMaxLength(100);
            entity.Property(x => x.HostedBy).HasMaxLength(200);
            entity.Property(x => x.VenueDescription).HasMaxLength(2000);
            entity.Property(x => x.ArrivalTips).HasMaxLength(2000);
            entity.Property(x => x.Accessibility).HasMaxLength(1000);
        });

        // Extended field configurations for User
        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(x => x.AvatarUrl).HasMaxLength(1000);
            entity.Property(x => x.DisplayName).HasMaxLength(100);
            entity.Property(x => x.Location).HasMaxLength(200);
        });

        // ImageUrl fields for other entities
        modelBuilder.Entity<Destination>(entity =>
        {
            entity.Property(x => x.ImageUrl).HasMaxLength(1000);
        });

        modelBuilder.Entity<Location>(entity =>
        {
            entity.Property(x => x.ImageUrl).HasMaxLength(1000);
        });

        modelBuilder.Entity<Tag>(entity =>
        {
            entity.Property(x => x.ImageUrl).HasMaxLength(1000);
        });
    }
}
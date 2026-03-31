using Geotrack.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure;

public class AppDbContext : DbContext
{

    public DbSet<Destination> Destinations => Set<Destination>();
    public DbSet<DestinationType> DestinationTypes => Set<DestinationType>();

    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventType> EventTypes => Set<EventType>();

    public DbSet<Location> Locations => Set<Location>();
    public DbSet<LocationType> LocationTypes => Set<LocationType>();

    public DbSet<Offer> Offers => Set<Offer>();
    public DbSet<OfferDestination> OfferDestinations => Set<OfferDestination>();

    public DbSet<Place> Places => Set<Place>();
    public DbSet<PlaceType> PlaceTypes => Set<PlaceType>();

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<TravelAgency> TravelAgencies => Set<TravelAgency>();

    public DbSet<User> Users => Set<User>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();

    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<DestinationTag> DestinationTags => Set<DestinationTag>();
    public DbSet<PlaceTag> PlaceTags => Set<PlaceTag>();
    public DbSet<EventTag> EventTags => Set<EventTag>();

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Destination>(entity =>
        {
            entity.ToTable("Destinations");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(e => e.DestinationTypeId)
                .IsRequired();

            entity.HasOne(e => e.DestinationType)
                .WithMany(e => e.Destinations)
                .HasForeignKey(e => e.DestinationTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.DestinationTypeId);
        });

        modelBuilder.Entity<DestinationType>(entity =>
        {
            entity.ToTable("DestinationTypes");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.HasIndex(e => e.Name)
                .IsUnique();
        });

        modelBuilder.Entity<Event>(entity =>
        {
            entity.ToTable("Events");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(e => e.EventTypeId).IsRequired();
            entity.Property(e => e.LocationId).IsRequired();
            entity.Property(e => e.PlaceId).IsRequired(false);

            entity.HasOne(e => e.EventType)
                .WithMany(e => e.Events)
                .HasForeignKey(e => e.EventTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Location)
                .WithMany(e => e.Events)
                .HasForeignKey(e => e.LocationId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Place)
                .WithMany(e => e.Events)
                .HasForeignKey(e => e.PlaceId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.EventTypeId);
            entity.HasIndex(e => e.LocationId);
            entity.HasIndex(e => e.PlaceId);
        });

        modelBuilder.Entity<EventType>(entity =>
        {
            entity.ToTable("EventTypes");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.HasIndex(e => e.Name)
                .IsUnique();
        });

        modelBuilder.Entity<Location>(entity =>
        {
            entity.ToTable("Locations");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(e => e.DestinationId).IsRequired();
            entity.Property(e => e.LocationTypeId).IsRequired();

            entity.HasOne(e => e.Destination)
                .WithMany(e => e.Locations)
                .HasForeignKey(e => e.DestinationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.LocationType)
                .WithMany(e => e.Locations)
                .HasForeignKey(e => e.LocationTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.DestinationId);
            entity.HasIndex(e => e.LocationTypeId);
        });

        modelBuilder.Entity<LocationType>(entity =>
        {
            entity.ToTable("LocationTypes");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.HasIndex(e => e.Name)
                .IsUnique();
        });

        modelBuilder.Entity<Offer>(entity =>
        {
            entity.ToTable("Offers");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(e => e.TravelAgencyId)
                .IsRequired();

            entity.HasOne(e => e.Agency)
                .WithMany(e => e.Offers)
                .HasForeignKey(e => e.TravelAgencyId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.TravelAgencyId);
        });

        modelBuilder.Entity<OfferDestination>(entity =>
        {
            entity.ToTable("OfferDestinations");

            entity.HasKey(e => new { e.OfferId, e.DestinationId });

            entity.Property(e => e.OfferId).IsRequired();
            entity.Property(e => e.DestinationId).IsRequired();

            entity.HasOne(e => e.Offer)
                .WithMany(e => e.OfferDestinations)
                .HasForeignKey(e => e.OfferId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Destination)
                .WithMany()
                .HasForeignKey(e => e.DestinationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.OfferId);
            entity.HasIndex(e => e.DestinationId);
        });

        modelBuilder.Entity<Place>(entity =>
        {
            entity.ToTable("Places");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(e => e.PlaceTypeId).IsRequired();
            entity.Property(e => e.LocationId).IsRequired();

            entity.HasOne(e => e.PlaceType)
                .WithMany(e => e.Places)
                .HasForeignKey(e => e.PlaceTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Location)
                .WithMany(e => e.Places)
                .HasForeignKey(e => e.LocationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.PlaceTypeId);
            entity.HasIndex(e => e.LocationId);
        });

        modelBuilder.Entity<PlaceType>(entity =>
        {
            entity.ToTable("PlaceTypes");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.HasIndex(e => e.Name)
                .IsUnique();
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("Roles");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.HasIndex(e => e.Name)
                .IsUnique();
        });

        modelBuilder.Entity<TravelAgency>(entity =>
        {
            entity.ToTable("TravelAgencies");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.HasIndex(e => e.Name)
                .IsUnique();
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Username)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Email)
                .IsRequired()
                .HasMaxLength(200);

            entity.HasIndex(e => e.Username)
                .IsUnique();

            entity.HasIndex(e => e.Email)
                .IsUnique();
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.ToTable("UserRoles");

            entity.HasKey(e => new { e.UserId, e.RoleId });

            entity.HasOne(e => e.User)
                .WithMany(e => e.UserRoles)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Role)
                .WithMany(e => e.UserRoles)
                .HasForeignKey(e => e.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Tag>(entity =>
        {
            entity.ToTable("Tags");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.HasIndex(x => x.Name)
                .IsUnique();

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
    }
}
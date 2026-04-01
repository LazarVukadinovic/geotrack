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

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(x => x.Description)
                .HasMaxLength(2000);

            entity.Property(x => x.Latitude)
                .IsRequired();

            entity.Property(x => x.Longitude)
                .IsRequired();

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.Property(x => x.UpdatedAt)
                .IsRequired(false);

            entity.HasOne(x => x.DestinationType)
                .WithMany(x => x.Destinations)
                .HasForeignKey(x => x.DestinationTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => x.DestinationTypeId);
        });

        modelBuilder.Entity<DestinationType>(entity =>
        {
            entity.ToTable("DestinationTypes");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.Description)
                .HasMaxLength(1000);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.Property(x => x.UpdatedAt)
                .IsRequired(false);

            entity.HasIndex(x => x.Name)
                .IsUnique();
        });

        modelBuilder.Entity<Location>(entity =>
        {
            entity.ToTable("Locations");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(x => x.Latitude)
                .IsRequired();

            entity.Property(x => x.Longitude)
                .IsRequired();

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.Property(x => x.UpdatedAt)
                .IsRequired(false);

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
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.Description)
                .HasMaxLength(1000);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.Property(x => x.UpdatedAt)
                .IsRequired(false);

            entity.HasIndex(x => x.Name)
                .IsUnique();
        });

        modelBuilder.Entity<Place>(entity =>
        {
            entity.ToTable("Places");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(x => x.Description)
                .HasMaxLength(2000);

            entity.Property(x => x.Latitude)
                .IsRequired();

            entity.Property(x => x.Longitude)
                .IsRequired();

            entity.Property(x => x.ContactEmail)
                .HasMaxLength(200)
                .IsRequired(false);

            entity.Property(x => x.Number)
                .HasMaxLength(50)
                .IsRequired(false);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.Property(x => x.UpdatedAt)
                .IsRequired(false);

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
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.Description)
                .HasMaxLength(1000);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.Property(x => x.UpdatedAt)
                .IsRequired(false);

            entity.HasIndex(x => x.Name)
                .IsUnique();
        });

        modelBuilder.Entity<Event>(entity =>
        {
            entity.ToTable("Events");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(x => x.Description)
                .HasMaxLength(2000);

            entity.Property(x => x.StartDate)
                .IsRequired();

            entity.Property(x => x.EndDate)
                .IsRequired();

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.Property(x => x.UpdatedAt)
                .IsRequired(false);

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
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.Description)
                .HasMaxLength(1000);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.Property(x => x.UpdatedAt)
                .IsRequired(false);

            entity.HasIndex(x => x.Name)
                .IsUnique();
        });

        modelBuilder.Entity<TravelAgency>(entity =>
        {
            entity.ToTable("TravelAgencies");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(x => x.Email)
                .HasMaxLength(200);

            entity.Property(x => x.PhoneNumber)
                .HasMaxLength(50);

            entity.Property(x => x.Address)
                .HasMaxLength(300);

            entity.Property(x => x.Website)
                .HasMaxLength(300);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.Property(x => x.UpdatedAt)
                .IsRequired(false);

            entity.Property(x => x.IsActive)
                .IsRequired();

            entity.HasIndex(x => x.Name)
                .IsUnique();

            entity.HasIndex(x => x.Email)
                .IsUnique(false);
        });

        modelBuilder.Entity<Offer>(entity =>
        {
            entity.ToTable("Offers");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(x => x.Description)
                .HasMaxLength(2000);

            entity.Property(x => x.Price)
                .HasColumnType("decimal(18,2)");

            entity.Property(x => x.StartDate)
                .IsRequired();

            entity.Property(x => x.EndDate)
                .IsRequired();

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.Property(x => x.UpdatedAt)
                .IsRequired(false);

            entity.HasOne(x => x.Agency)
                .WithMany(x => x.Offers)
                .HasForeignKey(x => x.TravelAgencyId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.TravelAgencyId);
        });

        modelBuilder.Entity<OfferDestination>(entity =>
        {
            entity.ToTable("OfferDestinations");

            entity.HasKey(x => new { x.OfferId, x.DestinationId });

            entity.HasOne(x => x.Offer)
                .WithMany(x => x.OfferDestinations)
                .HasForeignKey(x => x.OfferId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Destination)
                .WithMany()
                .HasForeignKey(x => x.DestinationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.DestinationId);
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
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.LastName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.PasswordHash)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(x => x.PhoneNumber)
                .HasMaxLength(50);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.Property(x => x.UpdatedAt)
                .IsRequired(false);

            entity.HasIndex(x => x.Username)
                .IsUnique();

            entity.HasIndex(x => x.Email)
                .IsUnique();
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("Roles");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.Description)
                .HasMaxLength(1000);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.Property(x => x.UpdatedAt)
                .IsRequired(false);

            entity.HasIndex(x => x.Name)
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
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.Description)
                .HasMaxLength(1000);

            entity.Property(x => x.CreatedAt)
                .IsRequired();

            entity.Property(x => x.UpdatedAt)
                .IsRequired(false);

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
    }
}
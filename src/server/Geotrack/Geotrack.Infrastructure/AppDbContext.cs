using System.Collections.Generic;
using Geotrack.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure;

public class AppDbContext : DbContext
{
    public DbSet<Destination> Destinations => Set<Destination>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Activity> Activities => Set<Activity>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Destination>(builder =>
        {
            builder.HasKey(x => x.Id);

            builder.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(200);

            builder.HasMany(x => x.Locations)
                .WithOne(x => x.Destination)
                .HasForeignKey(x => x.DestinationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Location>(builder =>
        {
            builder.HasKey(x => x.Id);

            builder.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(200);

            builder.HasOne(x => x.Destination)
                .WithMany(x => x.Locations)
                .HasForeignKey(x => x.DestinationId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(x => x.Events)
                .WithOne(x => x.Location)
                .HasForeignKey(x => x.LocationId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(x => x.Activities)
                .WithMany(x => x.Locations)
                .UsingEntity("LocationActivities");
        });

        modelBuilder.Entity<Activity>(builder =>
        {
            builder.HasKey(x => x.Id);

            builder.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(100);
        });

        modelBuilder.Entity<Event>(builder =>
        {
            builder.HasKey(x => x.Id);

            builder.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(200);

            builder.HasOne(x => x.Location)
                .WithMany(x => x.Events)
                .HasForeignKey(x => x.LocationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<User>(builder =>
        {
            builder.HasKey(x => x.Id);

            builder.Property(x => x.Username)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(x => x.Email)
                .IsRequired()
                .HasMaxLength(200);

            builder.HasIndex(x => x.Email)
                .IsUnique();

            builder.HasMany(x => x.Roles)
                .WithMany(x => x.Users)
                .UsingEntity("UserRoles");
        });

        modelBuilder.Entity<Role>(builder =>
        {
            builder.HasKey(x => x.Id);

            builder.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(50);

            builder.HasIndex(x => x.Name)
                .IsUnique();
        });
    }
}
using System.Text.RegularExpressions;
using Geotrack.Domain.Entities.Tags;
using Geotrack.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

internal static partial class RegionalTagWriter
{
    public static async Task<Tag> CreateScopedTagAsync(
        AppDbContext db,
        string? rawName,
        RegionalTagScope scope)
    {
        var name = NormalizeName(rawName);
        var slug = Slugify(name);

        var tag = await db.Tags
            .Include(item => item.TagType)
            .SingleOrDefaultAsync(item => item.Slug == slug);
        var now = DateTime.UtcNow;

        if (tag is not null)
        {
            if (HasScope(tag, scope))
                return tag;

            if (HasAnyScope(tag))
            {
                throw new InvalidOperationException(
                    "A tag with this name already exists for another entity type. Use a more specific tag name.");
            }

            ApplyOnlyScope(tag, scope);
            tag.UpdatedAt = now;
            await db.SaveChangesAsync();
            return tag;
        }

        tag = new Tag
        {
            Name = name,
            Slug = slug,
            Description = string.Empty,
            CreatedAt = now,
            UpdatedAt = now
        };
        ApplyOnlyScope(tag, scope);

        db.Tags.Add(tag);
        await db.SaveChangesAsync();
        return tag;
    }

    private static bool HasScope(Tag tag, RegionalTagScope scope) => scope switch
    {
        RegionalTagScope.Destination => tag.ForDestination,
        RegionalTagScope.Location => tag.ForLocation,
        RegionalTagScope.Place => tag.ForPlace,
        RegionalTagScope.Event => tag.ForEvent,
        _ => false
    };

    private static bool HasAnyScope(Tag tag) =>
        tag.ForDestination || tag.ForLocation || tag.ForPlace || tag.ForEvent;

    private static void ApplyOnlyScope(Tag tag, RegionalTagScope scope)
    {
        tag.ForDestination = scope == RegionalTagScope.Destination;
        tag.ForLocation = scope == RegionalTagScope.Location;
        tag.ForPlace = scope == RegionalTagScope.Place;
        tag.ForEvent = scope == RegionalTagScope.Event;
    }

    private static string NormalizeName(string? value)
    {
        var normalized = WhitespaceRegex().Replace(value?.Trim() ?? string.Empty, " ");
        if (normalized.Length < 2)
            throw new InvalidOperationException("Tag name must be at least 2 characters long.");

        if (normalized.Length > 100)
            throw new InvalidOperationException("Tag name cannot be longer than 100 characters.");

        return normalized;
    }

    private static string Slugify(string value)
    {
        var slug = SlugRegex().Replace(value.Trim().ToLowerInvariant(), "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? $"tag-{Guid.NewGuid():N}" : slug;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex SlugRegex();
}

internal enum RegionalTagScope
{
    Destination,
    Location,
    Place,
    Event
}

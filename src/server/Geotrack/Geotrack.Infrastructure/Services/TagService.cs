using Geotrack.Application.DTOs.Tags;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities;
using Geotrack.Domain.Entities.Tags;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Services;

public class TagService : ITagService
{
    private static readonly string[] UserInterestSlugs =
    {
        "food-drink",
        "history",
        "beaches",
        "nature",
        "museums",
        "local-events",
        "nightlife",
        "festivals",
        "adventure",
        "scenic-walks",
        "culture",
        "shopping",
    };

    private readonly AppDbContext _db;

    public TagService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<TagDto>> GetTagsAsync(bool? forUser = null)
    {
        var query = _db.Tags.AsQueryable();

        if (forUser == true)
        {
            query = query.Where(t => UserInterestSlugs.Contains(t.Slug));
        }

        var tags = await query
            .OrderBy(t => t.Name)
            .ToListAsync();

        return tags.Select(t => new TagDto
        {
            Id = t.Id,
            Name = t.Name,
            Slug = t.Slug
        }).ToList();
    }

    public async Task SaveUserTagsAsync(int userId, List<int> tagIds)
    {
        var existing = await _db.UserTags
            .Where(ut => ut.UserId == userId)
            .ToListAsync();
        _db.UserTags.RemoveRange(existing);

        foreach (var tagId in tagIds)
        {
            _db.UserTags.Add(new UserTag
            {
                UserId = userId,
                TagId = tagId,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
    }
}

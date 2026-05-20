using Geotrack.Application.Interfaces.Repository;
using Geotrack.Domain.Entities;
using Geotrack.Domain.Entities.Tags;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Repositories;

public class TagRepository : ITagRepository
{
    private readonly AppDbContext _db;

    public TagRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<Tag>> GetTagsAsync(IReadOnlyCollection<string>? slugs = null)
    {
        var query = _db.Tags
            .AsNoTracking()
            .Include(tag => tag.TagType)
                .ThenInclude(tagType => tagType!.PlaceTypes)
            .AsQueryable();

        if (slugs is { Count: > 0 })
        {
            query = query.Where(tag => slugs.Contains(tag.Slug));
        }

        return await query
            .OrderBy(tag => tag.TagType != null ? tag.TagType.Name : string.Empty)
            .ThenBy(tag => tag.Name)
            .ToListAsync();
    }

    public async Task<List<Tag>> GetTagsForActiveInterestTopicsAsync()
    {
        var tagIds = await _db.InterestTopicTags
            .AsNoTracking()
            .Where(link => link.InterestTopic.IsActive && link.InterestTopic.ShowOnOnboarding)
            .Select(link => link.TagId)
            .Distinct()
            .ToListAsync();

        return await _db.Tags
            .AsNoTracking()
            .Include(tag => tag.TagType)
                .ThenInclude(tagType => tagType!.PlaceTypes)
            .Where(tag => tagIds.Contains(tag.Id))
            .OrderBy(tag => tag.TagType != null ? tag.TagType.Name : string.Empty)
            .ThenBy(tag => tag.Name)
            .ToListAsync();
    }

    public async Task ReplaceUserTagsAsync(int userId, IEnumerable<int> tagIds)
    {
        var existing = await _db.UserTags
            .Where(userTag => userTag.UserId == userId)
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
using Geotrack.Application.DTOs.Tags;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Tags;

namespace Geotrack.Application.Services;

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

    private readonly ITagRepository _tagRepository;

    public TagService(ITagRepository tagRepository)
    {
        _tagRepository = tagRepository;
    }

    public async Task<List<TagDto>> GetTagsAsync(bool? forUser = null)
    {
        IReadOnlyCollection<string>? allowedSlugs = forUser == true
            ? UserInterestSlugs
            : null;

        var tags = await _tagRepository.GetTagsAsync(allowedSlugs);

        return tags.Select(MapTag).ToList();
    }

    public async Task SaveUserTagsAsync(int userId, List<int> tagIds)
    {
        await _tagRepository.ReplaceUserTagsAsync(userId, tagIds.Distinct());
    }

    private static TagDto MapTag(Tag tag) => new()
    {
        Id = tag.Id,
        Name = tag.Name,
        Slug = tag.Slug
    };
}

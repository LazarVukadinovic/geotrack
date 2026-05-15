using Geotrack.Application.DTOs.Tags;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Tags;

namespace Geotrack.Application.Services;

public class TagService : ITagService
{
    private readonly ITagRepository _tagRepository;

    public TagService(ITagRepository tagRepository)
    {
        _tagRepository = tagRepository;
    }

    public async Task<List<TagDto>> GetTagsAsync(bool? forUser = null)
    {
        var tags = forUser == true
            ? await _tagRepository.GetTagsForActiveInterestTopicsAsync()
            : await _tagRepository.GetTagsAsync();

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

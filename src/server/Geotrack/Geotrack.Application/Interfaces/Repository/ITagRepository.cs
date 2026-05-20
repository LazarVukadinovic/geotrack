using Geotrack.Domain.Entities.Tags;

namespace Geotrack.Application.Interfaces.Repository;

public interface ITagRepository
{
    Task<List<Tag>> GetTagsAsync(IReadOnlyCollection<string>? slugs = null);
    Task<List<Tag>> GetTagsForActiveInterestTopicsAsync();
    Task ReplaceUserTagsAsync(int userId, IEnumerable<int> tagIds);
}

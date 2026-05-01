using Geotrack.Application.DTOs.Tags;

namespace Geotrack.Application.Interfaces.Service;

public interface ITagService
{
    Task<List<TagDto>> GetTagsAsync(bool? forUser = null);
    Task SaveUserTagsAsync(int userId, List<int> tagIds);
}

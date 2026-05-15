using Geotrack.Application.DTOs.Interests;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Interests;

namespace Geotrack.Application.Services;

public class InterestTopicService : IInterestTopicService
{
    private readonly IInterestTopicRepository _interestTopicRepository;

    public InterestTopicService(IInterestTopicRepository interestTopicRepository)
    {
        _interestTopicRepository = interestTopicRepository;
    }

    public async Task<List<InterestTopicGroupDto>> GetOnboardingTopicsAsync()
    {
        var topics = await _interestTopicRepository.GetOnboardingTopicsAsync();

        return topics
            .Select(MapTopic)
            .GroupBy(topic => string.IsNullOrWhiteSpace(topic.Group) ? "Interests" : topic.Group)
            .OrderBy(group => group.Min(topic => topic.SortOrder))
            .ThenBy(group => group.Key)
            .Select(group => new InterestTopicGroupDto
            {
                Group = group.Key,
                Items = group
                    .OrderBy(topic => topic.SortOrder)
                    .ThenBy(topic => topic.Name)
                    .ToList()
            })
            .ToList();
    }

    private static InterestTopicDto MapTopic(InterestTopic topic) => new()
    {
        Id = topic.Id,
        Name = topic.Name,
        Slug = topic.Slug,
        Description = topic.Description,
        IconKey = topic.IconKey,
        Group = topic.Group,
        SortOrder = topic.SortOrder,
        Tags = topic.TopicTags
            .OrderByDescending(x => x.Weight)
            .ThenBy(x => x.Tag.Name)
            .Select(x => new InterestTopicTagDto
            {
                TagId = x.TagId,
                Name = x.Tag.Name,
                Slug = x.Tag.Slug,
                Weight = x.Weight
            })
            .ToList()
    };
}

using Geotrack.Application.Interfaces.Repository;
using Geotrack.Domain.Entities.Interests;
using Microsoft.EntityFrameworkCore;

namespace Geotrack.Infrastructure.Repositories;

public class InterestTopicRepository : IInterestTopicRepository
{
    private readonly AppDbContext _db;

    public InterestTopicRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<InterestTopic>> GetOnboardingTopicsAsync()
    {
        return await _db.InterestTopics
            .AsNoTracking()
            .Include(topic => topic.TopicTags)
                .ThenInclude(link => link.Tag)
            .Where(topic => topic.IsActive && topic.ShowOnOnboarding)
            .OrderBy(topic => topic.SortOrder)
            .ThenBy(topic => topic.Name)
            .ToListAsync();
    }
}

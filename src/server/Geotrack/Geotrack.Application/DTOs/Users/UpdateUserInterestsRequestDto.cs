namespace Geotrack.Application.DTOs.Users;

public class UpdateUserInterestsRequestDto
{
    public List<int> InterestTopicIds { get; set; } = new();

    // Backwards-compatible alias. Older frontend code sends { tagIds: [...] }.
    public List<int>? TagIds { get; set; }
}

namespace Geotrack.Application.DTOs.Reviews;

public class ReviewDto
{
    public int Id { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public int Rating { get; set; }
    public string? TimeAgo { get; set; }
    public string Body { get; set; } = string.Empty;
    public int HelpfulCount { get; set; }
}

public class CreateReviewDto
{
    public int Rating { get; set; }
    public string Comment { get; set; } = string.Empty;
}

namespace Geotrack.Application.DTOs.Reviews;

using System.ComponentModel.DataAnnotations;

public class ReviewDto
{
    public int Id { get; set; }
    public int AuthorUserId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public int Rating { get; set; }
    public string? TimeAgo { get; set; }
    public string Body { get; set; } = string.Empty;
    public int HelpfulCount { get; set; }
    public bool ReportedByCurrentUser { get; set; }
    public bool IsOwnReview { get; set; }
}

public class PagedReviewsResponseDto
{
    public List<ReviewDto> Items { get; set; } = new();
    public double? AverageRating { get; set; }
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public int NextSkip { get; set; }
    public bool HasMore { get; set; }
}

public class CreateReviewDto
{
    [Range(1, 5)]
    public int Rating { get; set; }

    [MaxLength(1000)]
    public string Comment { get; set; } = string.Empty;
}

public class UserReviewListItemDto
{
    public int Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string Body { get; set; } = string.Empty;
    public string TimeAgo { get; set; } = string.Empty;
}

public class PagedUserReviewsResponseDto
{
    public List<UserReviewListItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public int NextSkip { get; set; }
    public bool HasMore { get; set; }
}

public class ReviewReasonDto
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}
